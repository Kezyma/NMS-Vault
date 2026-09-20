using System.Text;
using NmsVault.Core;
using NmsVault.Core.Derived;
using NmsVault.Json;
using SkiaSharp;

namespace NmsVault.Ingest;

/// <summary>
/// The gallery's on-disk layout: one JSON document per item, plus a generated manifest the
/// three pages browse.
/// </summary>
/// <remarks>
/// <para>
/// <c>index.json</c> is derived, never hand-edited - it is regenerated from the item files
/// on every write so it cannot drift out of sync with them. Items are the source of truth.
/// </para>
/// </remarks>
public sealed class GalleryStore(string root)
{
    /// <summary>Where item documents live.</summary>
    public string ItemsDirectory { get; } = Path.Combine(root, "items");

    /// <summary>Where item images live.</summary>
    public string ImagesDirectory { get; } = Path.Combine(root, "img");

    /// <summary>The generated browse manifest.</summary>
    public string IndexPath { get; } = Path.Combine(root, "index.json");

    /// <summary>Path a given item's document would occupy.</summary>
    public string PathFor(string id) => Path.Combine(ItemsDirectory, id + ".json");

    /// <summary>Whether an item already exists.</summary>
    public bool Exists(string id) => File.Exists(PathFor(id));

    /// <summary>Writes an item document, creating directories as needed.</summary>
    public void Write(VaultItem item)
    {
        Directory.CreateDirectory(ItemsDirectory);
        File.WriteAllBytes(PathFor(item.Meta.Id), item.ToBytes());
    }

    /// <summary>Reads every stored item, ordered by id for stable output.</summary>
    public IEnumerable<(string Path, VaultItem Item)> ReadAll()
    {
        if (!Directory.Exists(ItemsDirectory)) yield break;

        foreach (var path in Directory.EnumerateFiles(ItemsDirectory, "*.json").OrderBy(p => p, StringComparer.Ordinal))
        {
            VaultItem item;
            try
            {
                item = VaultItem.FromBytes(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
            }
            catch (Exception ex)
            {
                throw new InvalidDataException($"{Path.GetFileName(path)} is not a readable vault item: {ex.Message}", ex);
            }
            yield return (path, item);
        }
    }

    /// <summary>
    /// Rebuilds <c>index.json</c> from the item files. Holds only what the gallery needs to
    /// render a card and filter - not the payloads, which would make the manifest enormous.
    /// </summary>
    public int RebuildIndex()
    {
        var items = new JsonArray();
        int count = 0;

        foreach (var (_, item) in ReadAll())
        {
            var entry = new JsonObject();
            entry.Set("Id", item.Meta.Id);
            entry.Set("Kind", item.Kind.ToString());
            entry.Set("Page", item.Kind.Page());
            entry.Set("DisplayName", item.Meta.DisplayName);

            // Type, class, seeds, stats and installed tech, derived from the payload. They
            // live here rather than being computed in the browser because filtering on any
            // of them would otherwise mean fetching every item document first.
            ItemFacts.For(item).WriteTo(entry);

            // The card line goes in the manifest; the full text does not, because the only
            // place it is shown is the item's own view, and that fetches the document anyway.
            if (item.Meta.Summary.Length > 0) entry.Set("Summary", item.Meta.Summary);
            if (item.Meta.Images.Count > 0) entry.Set("Image", item.Meta.Images[0]);
            if (item.Meta.Tags.Count > 0) entry.Set("Tags", ToArray(item.Meta.Tags));
            if (item.Meta.AlternativeNames.Count > 0)
                entry.Set("AlternativeNames", ToArray(item.Meta.AlternativeNames));
            if (item.Meta.Author is { } author) entry.Set("Author", author);
            if (item.Meta.GameVersion is { } version) entry.Set("GameVersion", version);

            items.Add(entry);
            count++;
        }

        var root = new JsonObject();
        root.Set("SchemaVersion", VaultMetadata.CurrentSchemaVersion);
        root.Set("Items", items);

        Directory.CreateDirectory(Path.GetDirectoryName(IndexPath)!);
        File.WriteAllBytes(IndexPath, Encoding.Latin1.GetBytes(root.ToExportString()));
        return count;
    }

    /// <summary>The extensions an item picture may arrive as, best format first.</summary>
    private static readonly string[] PictureExtensions = [".webp", ".png", ".jpg", ".jpeg"];

    /// <summary>
    /// The longest edge a stored picture is allowed. A capture is 3840 across and the widest
    /// it is ever drawn is the item view, at about 700 - but it is worth keeping enough to
    /// look right on a dense screen, and worth not keeping ten times that.
    /// </summary>
    private const int PictureEdge = 1600;

    /// <summary>WebP quality. Indistinguishable here and roughly a fifth the size of the JPEG.</summary>
    private const int PictureQuality = 82;

    /// <summary>
    /// Stores an image beside the gallery and returns its gallery-relative path.
    /// </summary>
    /// <remarks>
    /// Re-encoded to WebP and bounded rather than copied. A capture out of the game is a
    /// 4K JPEG of about a megabyte, and a gallery of a few hundred of those is most of what
    /// a reader would download to look at a page of cards.
    /// </remarks>
    /// <param name="sourcePath">The picture to store.</param>
    /// <param name="id">The item it belongs to.</param>
    /// <param name="ordinal">Its position, zero first.</param>
    /// <returns>The gallery-relative path.</returns>
    public string AddImage(string sourcePath, string id, int ordinal)
    {
        Directory.CreateDirectory(ImagesDirectory);

        string fileName = ordinal == 0 ? $"{id}.webp" : $"{id}-{ordinal + 1}.webp";
        string target = Path.Combine(ImagesDirectory, fileName);

        using var source = SKBitmap.Decode(sourcePath)
            ?? throw new InvalidDataException($"'{Path.GetFileName(sourcePath)}' is not an image this can read.");

        using var bitmap = Fit(source);
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Webp, PictureQuality);
        using var file = File.Create(target);
        data.SaveTo(file);

        return $"img/{fileName}";
    }

    /// <summary>Shrinks to the longest-edge bound, keeping the shape. Never enlarges.</summary>
    private static SKBitmap Fit(SKBitmap source)
    {
        int longest = Math.Max(source.Width, source.Height);
        if (longest <= PictureEdge) return source.Copy();

        double scale = (double)PictureEdge / longest;
        var info = new SKImageInfo(
            (int)Math.Round(source.Width * scale),
            (int)Math.Round(source.Height * scale),
            SKColorType.Rgba8888,
            SKAlphaType.Premul);

        return source.Resize(info, new SKSamplingOptions(SKFilterMode.Linear, SKMipmapMode.Linear))
            ?? source.Copy();
    }

    /// <summary>
    /// The pictures stored beside an export, which is where an item's pictures live.
    /// </summary>
    /// <remarks>
    /// A capture sits next to the backup it is of, under the same name - so
    /// <c>[START] Radiant Pillar BC1.jpg</c> belongs to
    /// <c>[START] Radiant Pillar BC1.nmsship</c>. Further pictures are numbered from two, as
    /// <c>[START] Radiant Pillar BC1-2.jpg</c>, which is the same shape the gallery stores
    /// them in.
    /// </remarks>
    /// <param name="exportPath">The export to look beside.</param>
    /// <returns>Its pictures, first one first. Empty when there are none.</returns>
    public static IReadOnlyList<string> PicturesBeside(string exportPath)
    {
        string? folder = Path.GetDirectoryName(exportPath);
        if (folder is null) return [];

        string stem = Path.GetFileNameWithoutExtension(exportPath);
        var found = new List<string>();

        for (int ordinal = 0; ; ordinal++)
        {
            string name = ordinal == 0 ? stem : $"{stem}-{ordinal + 1}";
            string? picture = PictureExtensions
                .Select(extension => Path.Combine(folder, name + extension))
                .FirstOrDefault(File.Exists);

            if (picture is null) return found;
            found.Add(picture);
        }
    }

    private static JsonArray ToArray(IReadOnlyList<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(v);
        return arr;
    }
}
