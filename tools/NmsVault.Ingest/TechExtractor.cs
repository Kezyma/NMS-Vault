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
/// <param name="Bytes">Total size of the written icons.</param>
public readonly record struct TechExtractionResult(
    int Technologies,
    int IconsWritten,
    int IconsMissing,
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
        return new TechExtractionResult(entries.Count, written, missing, bytes);
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

    private static long Downscale(string source, string target)
    {
        using var original = SKBitmap.Decode(source)
            ?? throw new InvalidDataException("not a readable image");

        var info = new SKImageInfo(IconSize, IconSize, SKColorType.Rgba8888, SKAlphaType.Premul);
        using var resized = original.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? throw new InvalidDataException("could not be resized");

        using var image = SKImage.FromBitmap(resized);
        using var data = image.Encode(SKEncodedImageFormat.Webp, IconQuality);
        using var output = File.Create(target);
        data.SaveTo(output);

        return data.Size;
    }
}
