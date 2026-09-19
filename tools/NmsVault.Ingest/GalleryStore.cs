using System.Text;
using NmsVault.Core;
using NmsVault.Json;

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

            if (item.Meta.Description.Length > 0) entry.Set("Description", item.Meta.Description);
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

    /// <summary>
    /// Copies an image alongside the gallery and returns its gallery-relative path.
    /// Deliberately a copy with no resizing: image processing needs a graphics library, and
    /// the browser downscales for display anyway. Resize before ingesting if size matters.
    /// </summary>
    public string AddImage(string sourcePath, string id, int ordinal)
    {
        Directory.CreateDirectory(ImagesDirectory);

        string extension = Path.GetExtension(sourcePath);
        string fileName = ordinal == 0 ? $"{id}{extension}" : $"{id}-{ordinal + 1}{extension}";
        File.Copy(sourcePath, Path.Combine(ImagesDirectory, fileName), overwrite: true);

        return $"img/{fileName}";
    }

    private static JsonArray ToArray(IReadOnlyList<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(v);
        return arr;
    }
}
