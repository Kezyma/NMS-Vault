using System.Security.Cryptography;
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
        var pets = ReadPets();
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

            // A creature's affinity, resolved here rather than in the browser. The card shows
            // it beside the name the way a ship shows its class, and a card only ever reads
            // this file - working it out in the page would mean every grid waiting on
            // pets.json before it could draw a single name.
            if (item.Kind == EntityKind.Companion)
            {
                var creature = CompanionFacts.For(item.Payload, item.AccessorySlots);

                if (Affinity(pets, creature) is { } affinity) entry.Set("Affinity", affinity);
                if (Traits(pets, creature) is { } traits) entry.Set("Traits", traits);
            }

            // The card line goes in the manifest; the full text does not, because the only
            // place it is shown is the item's own view, and that fetches the document anyway.
            if (item.Meta.Summary.Length > 0) entry.Set("Summary", item.Meta.Summary);
            // Every picture, not just the first. The card pages through them in place, so the
            // manifest has to carry the whole set - fetching each item document to find out
            // whether it has a second picture is the thing this file exists to avoid.
            if (item.Meta.Images.Count > 0) entry.Set("Images", ToArray(item.Meta.Images));
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

    /// <summary>What a hand-written metadata file beside an export is called.</summary>
    public const string MetadataExtension = ".json";

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
    /// <returns>The gallery-relative URL, fingerprinted so a replacement is a new URL.</returns>
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
        using (var file = File.Create(target))
            data.SaveTo(file);

        return $"img/{fileName}?v={Fingerprint(data.ToArray())}";
    }

    /// <summary>
    /// A short content fingerprint, appended to a stored picture's URL.
    /// </summary>
    /// <remarks>
    /// A stored picture is named after the item, not after the file it came from, so replacing
    /// one leaves the URL exactly as it was and a browser that already has the old picture
    /// never asks for the new one. Tying the URL to the content instead means a replaced
    /// picture is simply a different URL, so nothing anywhere has to be told to expire -
    /// which matters most on Pages, behind a CDN that caches far harder than a dev server.
    /// Eight hex characters is four billion to one against a collision between two pictures
    /// of the same ship, and the whole string is only ever compared, never decoded.
    /// </remarks>
    private static string Fingerprint(byte[] content) =>
        Convert.ToHexStringLower(SHA256.HashData(content))[..8];

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
    /// Applies the metadata written beside an export, where there is any.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A hand-written block of an item's own fields, stored under the export's name with a
    /// <c>.json</c> extension - so <c>[EXP-13-R] Iron Vulture.json</c> beside
    /// <c>[EXP-13-R] Iron Vulture.nmsship</c>. The same arrangement pictures use: everything
    /// about an item lives beside the backup it came from, and is re-read when that is.
    /// See <c>docs/item-template.json</c>.
    /// </para>
    /// <para>
    /// Only the keys present are applied, so a field can be left out rather than filled in -
    /// and a field written as empty is an instruction to clear it, which is how something set
    /// earlier is removed. A block wrapped under <c>Vault</c> is accepted as well as a bare
    /// one, so a block copied out of a stored item works as the template does.
    /// </para>
    /// </remarks>
    /// <param name="meta">What to start from.</param>
    /// <param name="exportPath">The export to look beside.</param>
    /// <returns>The metadata with the file applied, or unchanged when there is no file.</returns>
    /// <exception cref="InvalidDataException">If the file is not readable JSON.</exception>
    public static VaultMetadata ApplyMetadataBeside(VaultMetadata meta, string exportPath)
    {
        string path = Path.ChangeExtension(exportPath, ".json");
        if (!File.Exists(path)) return meta;

        byte[] bytes = File.ReadAllBytes(path);

        // A file typed on Windows arrives with a byte order mark as often as not, and the
        // parser reads bytes as Latin-1 - so those three turn into three characters in front
        // of the opening brace and it refuses the lot. Skipped rather than parsed.
        if (bytes is [0xEF, 0xBB, 0xBF, ..]) bytes = bytes[3..];

        JsonObject document;
        try
        {
            document = JsonObject.FromBytes(bytes);
        }
        catch (Exception ex) when (ex is JsonException or InvalidDataException)
        {
            throw new InvalidDataException($"'{Path.GetFileName(path)}' is not readable JSON: {ex.Message}", ex);
        }

        var block = document.GetObject(VaultItem.VaultKey) ?? document;

        if (block.GetString("Id") is { Length: > 0 } id) meta = meta with { Id = Slug.From(id) };
        if (block.GetString("DisplayName") is { Length: > 0 } name) meta = meta with { DisplayName = name };
        if (block.Contains("Summary")) meta = meta with { Summary = block.GetString("Summary") ?? "" };
        if (block.Contains("Description")) meta = meta with { Description = block.GetString("Description") ?? "" };
        if (block.Contains("Author")) meta = meta with { Author = Blank(block.GetString("Author")) };
        if (block.Contains("GameVersion")) meta = meta with { GameVersion = Blank(block.GetString("GameVersion")) };
        if (block.Contains("AlternativeNames")) meta = meta with { AlternativeNames = Strings(block.GetArray("AlternativeNames")) };
        if (block.Contains("Tags")) meta = meta with { Tags = Strings(block.GetArray("Tags")) };

        // Read, not just written, because the gallery is rebuilt from this folder: without it
        // every item is dated the moment the build ran, and dated again on the next build.
        if (block.GetString("DateAdded") is { Length: > 0 } added
            && DateTimeOffset.TryParse(added, System.Globalization.CultureInfo.InvariantCulture,
                                       System.Globalization.DateTimeStyles.RoundtripKind, out var when))
        {
            meta = meta with { DateAdded = when };
        }

        return meta;
    }

    /// <summary>An empty string means "nothing here", not the empty string.</summary>
    private static string? Blank(string? value) => value is { Length: > 0 } ? value : null;

    private static IReadOnlyList<string> Strings(JsonArray? array)
    {
        if (array is null) return [];

        var read = new List<string>(array.Length);
        for (int i = 0; i < array.Length; i++)
            if (array.Get(i)?.ToString() is { Length: > 0 } value) read.Add(value);

        return read;
    }

    /// <summary>The favicon, which lives in the image folder but is not an item's picture.</summary>
    private const string FaviconName = "favicon.webp";

    /// <summary>
    /// Removes every stored item, its pictures and the index, leaving the extracted game data.
    /// </summary>
    /// <remarks>
    /// A rebuild has to start from nothing, or an item deleted from the source folder lingers
    /// in the gallery forever - the index is built from what is on disk, not from what was
    /// just read. Only the item pictures go with it. <c>img/tech</c>, <c>img/class</c> and the
    /// favicon come out of the game's own files rather than out of the source folder, take an
    /// NMSE checkout to produce, and are committed precisely so that a build does not need one.
    /// </remarks>
    /// <returns>How many stored items were removed.</returns>
    public int Clear()
    {
        int removed = 0;

        if (Directory.Exists(ItemsDirectory))
            foreach (string file in Directory.EnumerateFiles(ItemsDirectory, "*.json"))
            {
                File.Delete(file);
                removed++;
            }

        // An item's pictures sit directly in img/, and everything extracted from the game is
        // either in a subfolder of it or is the favicon - so the top level, minus that one
        // file, is exactly what a build owns and may throw away.
        if (Directory.Exists(ImagesDirectory))
            foreach (string file in Directory.EnumerateFiles(ImagesDirectory, "*.webp"))
                if (!Path.GetFileName(file).Equals(FaviconName, StringComparison.OrdinalIgnoreCase))
                    File.Delete(file);

        if (File.Exists(IndexPath)) File.Delete(IndexPath);

        return removed;
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

    /// <summary>
    /// The companion lookup published beside the gallery, or an empty one when it has not been
    /// extracted yet. Missing it costs the affinity badge, not the manifest.
    /// </summary>
    private PetIndex ReadPets()
    {
        string path = Path.Combine(root, "pets.json");
        return File.Exists(path) ? PetIndex.FromBytes(File.ReadAllBytes(path)) : PetIndex.Empty;
    }

    /// <summary>
    /// The creature's personality in the game's words, or null where the tables cannot name it.
    /// </summary>
    /// <remarks>
    /// Resolved here for the same reason the affinity is: it needs the species table, and a
    /// card has only the manifest to draw from.
    /// </remarks>
    private static JsonArray? Traits(PetIndex pets, CompanionFacts creature)
    {
        var traits = pets.Traits(creature.SpeciesId, [.. creature.Traits.Select(t => t.Value)]);
        if (traits.Count == 0) return null;

        var written = new JsonArray();

        foreach (var trait in traits)
        {
            var entry = new JsonObject();
            entry.Set("Name", trait.Name);
            entry.Set("Percent", trait.Percent);
            entry.Set("Class", trait.Class);
            if (trait.Word is { Length: > 0 } word) entry.Set("Word", word);
            written.Add(entry);
        }

        return written;
    }

    /// <summary>The affinity to write into a creature's manifest entry, or null if unknown.</summary>
    private static JsonObject? Affinity(PetIndex pets, CompanionFacts creature)
    {
        if (pets.Affinity(creature.SpeciesId, creature.Biome) is not { } affinity) return null;

        var written = new JsonObject();
        written.Set("Id", affinity.Id);
        written.Set("Name", affinity.Name);
        if (affinity.Icon is { Length: > 0 } icon) written.Set("Icon", icon);

        return written;
    }

    private static JsonArray ToArray(IReadOnlyList<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(v);
        return arr;
    }
}
