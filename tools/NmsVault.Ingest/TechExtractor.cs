using System.Text;
using System.Text.Json;
using NmsVault.Core.Derived;
using NmsVault.Json;
using SkiaSharp;

namespace NmsVault.Ingest;

/// <summary>What an extraction run produced.</summary>
/// <param name="Technologies">How many entries were written.</param>
/// <param name="IconsWritten">How many icons were re-encoded.</param>
/// <param name="IconsMissing">Icons named by an entry but absent from the source.</param>
/// <param name="ClassIcons">How many class badges were written.</param>
/// <param name="Favicon">Whether the square favicon was written.</param>
/// <param name="Bytes">Total size of the written icons.</param>
public readonly record struct TechExtractionResult(
    int Technologies,
    int IconsWritten,
    int IconsMissing,
    int ClassIcons,
    bool Favicon,
    long Bytes);

/// <summary>
/// Builds the gallery's technology lookup from an NMSE resources folder.
/// </summary>
/// <remarks>
/// <para>
/// Run when the game updates, not on every ingest. The output is committed, so the Pages
/// workflow can publish without an NMSE checkout - the same reason the reference project
/// commits its extracted game data.
/// </para>
/// <para>
/// Everything in the source files is extracted rather than a filtered subset. Filtering by
/// category looks tidier but quietly misses the cosmetics that occupy technology slots -
/// ship trails and bobbleheads sit in <c>Others.json</c> and <c>Products.json</c> - and the
/// whole trimmed set is under 90 KB gzipped, so there is nothing to gain by being clever.
/// </para>
/// </remarks>
public static class TechExtractor
{
    /// <summary>
    /// The files technology can live in. Spread across all of these in the game's own data,
    /// so all of them are read - <c>Others</c> and <c>Products</c> included, despite the names.
    /// </summary>
    private static readonly string[] SourceFiles =
    [
        "Technology.json",
        "Upgrades.json",
        "Technology Module.json",
        "Others.json",
        "Products.json",
        "Constructed Technology.json",
    ];

    /// <summary>
    /// The game's own class badges. Already 64px, so they are re-encoded only to put them in
    /// the same format as everything else rather than to shrink them.
    /// </summary>
    private static readonly string[] ClassIcons =
        ["CLASSMINI.C.png", "CLASSMINI.B.png", "CLASSMINI.A.png", "CLASSMINI.S.png",
         "CLASSMINI.X.png", "CLASSMINI.SENTINEL.png"];

    /// <summary>
    /// The badge the site is identified by. S class, because a vault of saved ships is a
    /// vault of the good ones.
    /// </summary>
    private const string FaviconIcon = "CLASSMINI.S.png";

    /// <summary>Icons are re-encoded to this size, which is ample for a slot in a grid.</summary>
    private const int IconSize = 64;

    /// <summary>WebP quality. 80 is visually indistinguishable here and roughly a third the size.</summary>
    private const int IconQuality = 80;

    /// <summary>
    /// Extracts technology names and icons into the gallery.
    /// </summary>
    /// <param name="nmseResources">Path to NMSE's <c>Resources</c> folder.</param>
    /// <param name="galleryRoot">The gallery folder to write into.</param>
    /// <param name="log">Called with progress lines.</param>
    /// <returns>What was produced.</returns>
    public static TechExtractionResult Extract(string nmseResources, string galleryRoot, Action<string> log)
    {
        string jsonDir = Path.Combine(nmseResources, "json");
        string imageDir = Path.Combine(nmseResources, "images");

        if (!Directory.Exists(jsonDir))
            throw new DirectoryNotFoundException($"No json folder under '{nmseResources}'. Point --nmse at NMSE's Resources directory.");

        var entries = ReadEntries(jsonDir, log);
        log($"  read {entries.Count} technologies from {SourceFiles.Length} files");

        WriteIndex(entries, galleryRoot);

        var (written, missing, bytes) = WriteIcons(entries, imageDir, galleryRoot, log);
        var (classIcons, classBytes) = WriteClassIcons(imageDir, galleryRoot, log);
        long faviconBytes = WriteFavicon(imageDir, galleryRoot, log);

        return new TechExtractionResult(
            entries.Count, written, missing, classIcons, faviconBytes > 0, bytes + classBytes + faviconBytes);
    }

    private static List<TechEntry> ReadEntries(string jsonDir, Action<string> log)
    {
        // Keyed so a later file cannot silently shadow an earlier one; first wins, matching
        // the order the files are listed in.
        var byId = new Dictionary<string, TechEntry>(StringComparer.Ordinal);

        foreach (string file in SourceFiles)
        {
            string path = Path.Combine(jsonDir, file);
            if (!File.Exists(path))
            {
                log($"  skipped {file} (not present)");
                continue;
            }

            using var document = JsonDocument.Parse(File.ReadAllBytes(path));
            if (document.RootElement.ValueKind != JsonValueKind.Array) continue;

            foreach (var element in document.RootElement.EnumerateArray())
            {
                if (element.ValueKind != JsonValueKind.Object) continue;
                if (Text(element, "Id") is not { Length: > 0 } id) continue;
                if (byId.ContainsKey(id)) continue;

                byId[id] = new TechEntry(
                    id,
                    // Tech inventories show the lower-case variant where there is one, as
                    // NMSE does; it is the name the game itself uses in a slot.
                    Text(element, "NameLower") ?? Text(element, "Name") ?? id,
                    Text(element, "Group"),
                    Text(element, "Description"),
                    Text(element, "Icon"),
                    Text(element, "Category"));
            }
        }

        return [.. byId.Values.OrderBy(e => e.Id, StringComparer.Ordinal)];
    }

    private static string? Text(JsonElement element, string property)
        => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() is { Length: > 0 } s ? s : null
            : null;

    private static void WriteIndex(List<TechEntry> entries, string galleryRoot)
    {
        var array = new JsonArray();
        foreach (var entry in entries)
        {
            var obj = new JsonObject();
            obj.Set("Id", entry.Id);
            obj.Set("Name", entry.Name);
            if (entry.Subtitle is not null) obj.Set("Subtitle", entry.Subtitle);
            if (entry.Description is not null) obj.Set("Description", entry.Description);
            if (entry.Icon is not null) obj.Set("Icon", Path.ChangeExtension(entry.Icon, ".webp"));
            if (entry.Category is not null) obj.Set("Category", entry.Category);
            array.Add(obj);
        }

        var root = new JsonObject();
        root.Set("SchemaVersion", 1);
        root.Set("Technologies", array);

        Directory.CreateDirectory(galleryRoot);
        File.WriteAllBytes(Path.Combine(galleryRoot, "tech.json"),
            Encoding.Latin1.GetBytes(root.ToExportString()));
    }

    private static (int Written, int Missing, long Bytes) WriteIcons(
        List<TechEntry> entries, string imageDir, string galleryRoot, Action<string> log)
    {
        string outDir = Path.Combine(galleryRoot, "img", "tech");
        Directory.CreateDirectory(outDir);

        if (!Directory.Exists(imageDir))
        {
            log($"  no images folder at '{imageDir}'; wrote names only");
            return (0, 0, 0);
        }

        // The source icons are 256px PNGs averaging about 68 KB, which is fifteen times more
        // than a 64px slot can show. Re-encoding is the difference between a ship's tech grid
        // costing a megabyte and costing tens of kilobytes.
        var wanted = entries.Where(e => e.Icon is not null)
                            .Select(e => e.Icon!)
                            .Distinct(StringComparer.OrdinalIgnoreCase)
                            .ToList();

        int written = 0, missing = 0, reported = 0;
        long bytes = 0;

        foreach (string icon in wanted)
        {
            string source = Path.Combine(imageDir, icon);
            if (!File.Exists(source)) { missing++; continue; }

            string target = Path.Combine(outDir, Path.ChangeExtension(icon, ".webp"));
            try
            {
                bytes += Downscale(source, target);
                written++;
            }
            catch (Exception ex)
            {
                log($"  could not re-encode {icon}: {ex.Message}");
                missing++;
            }

            if (written - reported >= 250) { reported = written; log($"  {written}/{wanted.Count} icons"); }
        }

        return (written, missing, bytes);
    }

    /// <summary>
    /// Copies the game's class badges across. Showing the game's own S, A, B and C marks
    /// rather than a coloured letter is the difference between a gallery that looks like it
    /// belongs to the game and one that merely lists its contents.
    /// </summary>
    private static (int Written, long Bytes) WriteClassIcons(string imageDir, string galleryRoot, Action<string> log)
    {
        string outDir = Path.Combine(galleryRoot, "img", "class");
        Directory.CreateDirectory(outDir);

        int written = 0;
        long bytes = 0;

        foreach (string icon in ClassIcons)
        {
            string source = Path.Combine(imageDir, icon);
            if (!File.Exists(source)) { log($"  class badge {icon} not found"); continue; }

            // Named for the class alone - CLASSMINI.S.png becomes s.webp - so a lookup is
            // just the class letter lowercased.
            string name = icon.Replace("CLASSMINI.", "", StringComparison.Ordinal)
                              .Replace(".png", "", StringComparison.Ordinal)
                              .ToLowerInvariant();

            // Trimmed, unlike the technology icons: a class badge is a tall shield drawn
            // inside a square frame, and most of that frame is nothing.
            bytes += Downscale(source, Path.Combine(outDir, name + ".webp"), trim: true);
            written++;
        }

        log($"  {written} class badge(s)");
        return (written, bytes);
    }

    /// <summary>
    /// Writes the site's favicon: the S badge centred on a square.
    /// </summary>
    /// <remarks>
    /// Pointing the favicon straight at the published class badge does not work. That badge is
    /// trimmed to the shield, so it is taller than it is wide, and a browser draws a favicon
    /// into a square slot by stretching whatever it is given to fit - which makes the S come
    /// out wide and squat. Padding it back out to a square here keeps the shape the game drew.
    /// </remarks>
    /// <returns>The bytes written, or zero if the source badge was not there.</returns>
    private static long WriteFavicon(string imageDir, string galleryRoot, Action<string> log)
    {
        string source = Path.Combine(imageDir, FaviconIcon);
        if (!File.Exists(source)) { log($"  favicon source {FaviconIcon} not found"); return 0; }

        using var original = SKBitmap.Decode(source)
            ?? throw new InvalidDataException($"'{FaviconIcon}' is not a readable image");

        // Trimmed first for the same reason the badges are: the shield sits off-centre in its
        // frame, so padding the frame back out would keep it off-centre.
        using var cropped = Trim(original);
        var badge = cropped ?? original;

        double scale = (double)IconSize / Math.Max(badge.Width, badge.Height);
        var fitted = new SKImageInfo(
            (int)Math.Round(badge.Width * scale),
            (int)Math.Round(badge.Height * scale),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var resized = badge.Resize(fitted, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidDataException($"'{FaviconIcon}' could not be resized");

        using var square = new SKBitmap(
            new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul));

        using (var canvas = new SKCanvas(square))
        using (var drawn = SKImage.FromBitmap(resized))
        {
            canvas.Clear(SKColors.Transparent);

            // Drawn at its own size, so the sampling never gets a chance to matter.
            canvas.DrawImage(
                drawn,
                new SKPoint((IconSize - fitted.Width) / 2f, (IconSize - fitted.Height) / 2f),
                new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.None));
        }

        string target = Path.Combine(galleryRoot, "img", "favicon.webp");
        Directory.CreateDirectory(Path.GetDirectoryName(target)!);

        using var image = SKImage.FromBitmap(square);
        using var data = image.Encode(SKEncodedImageFormat.Webp, IconQuality);
        using var output = File.Create(target);
        data.SaveTo(output);

        log($"  favicon ({fitted.Width}x{fitted.Height} badge on a {IconSize}x{IconSize} square)");
        return data.Size;
    }

    /// <summary>
    /// Re-encodes an icon at display size.
    /// </summary>
    /// <param name="source">The source PNG.</param>
    /// <param name="target">Where to write the WebP.</param>
    /// <param name="trim">
    /// Whether to cut the transparent margin off first, and keep the shape of what is left
    /// rather than squaring it. False for a technology icon, which fills its frame; true for
    /// a class badge, which does not.
    /// </param>
    /// <returns>The bytes written.</returns>
    private static long Downscale(string source, string target, bool trim = false)
    {
        using var original = SKBitmap.Decode(source)
            ?? throw new InvalidDataException("not a readable image");

        using var cropped = trim ? Trim(original) : null;
        var bitmap = cropped ?? original;

        // A trimmed badge keeps its proportions; an icon that filled a square stays square.
        double scale = (double)IconSize / Math.Max(bitmap.Width, bitmap.Height);
        var info = new SKImageInfo(
            trim ? (int)Math.Round(bitmap.Width * scale) : IconSize,
            trim ? (int)Math.Round(bitmap.Height * scale) : IconSize,
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        using var resized = bitmap.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidDataException("could not be resized");

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Webp, IconQuality);
        using var output = File.Create(target);
        data.SaveTo(output);

        return data.Size;
    }

    /// <summary>
    /// The picture with its transparent border cut away, or null when there is nothing to cut.
    /// </summary>
    /// <remarks>
    /// The game's class badges are a shield about 36 by 56 sitting in a 64 by 64 frame, and
    /// not centred in it either - so drawn at a given size they come out smaller than anything
    /// beside them and a little off to one side. Cutting the frame away at extraction is the
    /// fix; compensating in a stylesheet would mean a magic negative margin per badge.
    /// </remarks>
    private static SKBitmap? Trim(SKBitmap source)
    {
        const byte Threshold = 8;   // below this an edge pixel is noise, not the drawing

        int left = source.Width, top = source.Height, right = -1, bottom = -1;

        for (int y = 0; y < source.Height; y++)
        {
            for (int x = 0; x < source.Width; x++)
            {
                if (source.GetPixel(x, y).Alpha < Threshold) continue;

                if (x < left) left = x;
                if (x > right) right = x;
                if (y < top) top = y;
                if (y > bottom) bottom = y;
            }
        }

        // Nothing drawn at all, or drawn edge to edge: leave it alone either way.
        if (right < left || bottom < top) return null;
        if (left == 0 && top == 0 && right == source.Width - 1 && bottom == source.Height - 1) return null;

        // Pixel for pixel at this stage - the resize afterwards is what scales it, and doing
        // it in one step here would resample twice.
        var cut = new SKBitmap(right - left + 1, bottom - top + 1, SKColorType.Rgba8888, SKAlphaType.Premul);
        if (!source.ExtractSubset(cut, new SKRectI(left, top, right + 1, bottom + 1)))
        {
            cut.Dispose();
            return null;
        }

        return cut;
    }
}
