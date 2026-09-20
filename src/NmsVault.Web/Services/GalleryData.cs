using NmsVault.Core;
using NmsVault.Json;

namespace NmsVault.Web.Services;

/// <summary>One row of the gallery, as <c>index.json</c> carries it.</summary>
/// <remarks>
/// Everything here is derived at ingest. The item's full payload is a separate document,
/// fetched only when someone opens it - filtering a few hundred items must not mean
/// downloading a few hundred ship objects first.
/// </remarks>
public sealed record GalleryRow
{
    /// <summary>Permalink slug.</summary>
    public required string Id { get; init; }

    /// <summary>What kind of item this is.</summary>
    public required EntityKind Kind { get; init; }

    /// <summary>Which page it belongs on.</summary>
    public required string Page { get; init; }

    /// <summary>The name shown on the card.</summary>
    public required string DisplayName { get; init; }

    /// <summary>Display type - "Hauler", "Voltaic Staff".</summary>
    public string Type { get; init; } = "";

    /// <summary>Whether the model path looked modded.</summary>
    public bool IsModified { get; init; }

    /// <summary>Inventory class, or null where the kind has none.</summary>
    public string? Class { get; init; }

    /// <summary>Where a creature is from, e.g. "Lush". Null for everything else.</summary>
    public string? Biome { get; init; }

    /// <summary>Seeds, keyed by what each governs.</summary>
    public IReadOnlyDictionary<string, string> Seeds { get; init; } = new Dictionary<string, string>();

    /// <summary>Base stats, keyed by label, in presentation order.</summary>
    public IReadOnlyList<KeyValuePair<string, double>> Stats { get; init; } = [];

    /// <summary>Installed technology ids, with procedural suffixes already stripped.</summary>
    public IReadOnlyList<string> Tech { get; init; } = [];

    /// <summary>
    /// The subset of <see cref="Tech"/> that are procedurally rolled upgrade modules.
    /// </summary>
    public IReadOnlyList<string> Rolled { get; init; } = [];

    /// <summary>
    /// The technologies worth offering as a filter: everything installed except the rolled
    /// upgrade modules, whose names either repeat the technology they boost or say nothing
    /// beyond their class, and which nearly every ship carries some of.
    /// </summary>
    /// <remarks>
    /// Worked out on each read rather than stored, so a row built any way at all - read from
    /// the index, or constructed directly in a test - gives the same answer. The common case
    /// costs nothing: a row with no rolled modules hands back the list it already has.
    /// </remarks>
    public IReadOnlyList<string> NamedTech =>
        Rolled.Count == 0 ? Tech : [.. Tech.Except(Rolled, StringComparer.Ordinal)];

    /// <summary>Gallery tags.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Other names this is searchable under.</summary>
    public IReadOnlyList<string> AlternativeNames { get; init; } = [];

    /// <summary>
    /// One line, shown on the card. The full description is deliberately not here: the only
    /// place it is shown is the item's own view, which fetches the whole document anyway, and
    /// putting paragraphs of prose in the manifest would make every page load carry them.
    /// </summary>
    public string Summary { get; init; } = "";

    /// <summary>
    /// The item's pictures, gallery-relative, best first. Empty when it has none.
    /// </summary>
    public IReadOnlyList<string> Images { get; init; } = [];

    /// <summary>
    /// The one picture that stands for the item - its thumbnail in the table, and the first
    /// the card shows. Null when it has no pictures.
    /// </summary>
    public string? Image => Images.Count > 0 ? Images[0] : null;

    /// <summary>Contributor credit.</summary>
    public string? Author { get; init; }

    /// <summary>Game version captured from.</summary>
    public string? GameVersion { get; init; }

    /// <summary>Everything a search should match against, concatenated once.</summary>
    public string SearchText { get; private init; } = "";

    internal static GalleryRow FromJson(JsonObject entry)
    {
        var row = new GalleryRow
        {
            Id = entry.GetString("Id") ?? "",
            Kind = Enum.TryParse<EntityKind>(entry.GetString("Kind"), out var kind) ? kind : EntityKind.Starship,
            Page = entry.GetString("Page") ?? "",
            DisplayName = entry.GetString("DisplayName") ?? "",
            Type = entry.GetString("Type") ?? "",
            IsModified = entry.Get("Modified") is true,
            Class = entry.GetString("Class"),
            Biome = entry.GetString("Biome"),
            Seeds = ReadMap(entry.GetObject("Seeds")),
            Stats = ReadStats(entry.GetObject("Stats")),
            Tech = ReadList(entry.GetArray("Tech")),
            Rolled = ReadList(entry.GetArray("Rolled")),
            Tags = ReadList(entry.GetArray("Tags")),
            AlternativeNames = ReadList(entry.GetArray("AlternativeNames")),
            Summary = entry.GetString("Summary") ?? "",
            Images = ReadList(entry.GetArray("Images")),
            Author = entry.GetString("Author"),
            GameVersion = entry.GetString("GameVersion"),
        };

        // Built once here rather than per keystroke. Alternative names are searched so
        // someone typing a community nickname finds the item under its real name.
        return row with
        {
            SearchText = string.Join(' ',
                new[] { row.DisplayName, row.Type, row.Summary, row.Biome ?? "" }
                    .Concat(row.AlternativeNames)
                    .Concat(row.Tags)),
        };
    }

    private static IReadOnlyDictionary<string, string> ReadMap(JsonObject? obj)
    {
        if (obj is null) return new Dictionary<string, string>();
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var name in obj.Names())
            if (obj.Get(name)?.ToString() is { } value) map[name] = value;
        return map;
    }

    private static IReadOnlyList<KeyValuePair<string, double>> ReadStats(JsonObject? obj)
    {
        if (obj is null) return [];
        var stats = new List<KeyValuePair<string, double>>(obj.Length);
        foreach (var name in obj.Names())
            stats.Add(new(name, obj.Get(name) is RawDouble r ? r.Value : Convert.ToDouble(obj.Get(name) ?? 0d)));
        return stats;
    }

    private static IReadOnlyList<string> ReadList(JsonArray? arr)
    {
        if (arr is null || arr.Length == 0) return [];
        var list = new List<string>(arr.Length);
        for (int i = 0; i < arr.Length; i++)
            if (arr.Get(i)?.ToString() is { } value) list.Add(value);
        return list;
    }
}

/// <summary>
/// Loads the gallery manifest, once, and hands the same rows to every page.
/// </summary>
public sealed class GalleryData(HttpClient http)
{
    private readonly HttpClient _http = http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<GalleryRow>? _rows;

    /// <summary>Every item in the gallery, ordered by name.</summary>
    /// <returns>The rows, loaded on first call and cached after.</returns>
    public async Task<IReadOnlyList<GalleryRow>> RowsAsync(CancellationToken cancellationToken = default)
    {
        if (_rows is not null) return _rows;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_rows is not null) return _rows;

            // Taken as bytes and then parsed, rather than deserialised from the stream. In a
            // browser the response stream crosses a bridge into JavaScript, and pulling a
            // large document through it a chunk at a time stalls.
            byte[] bytes = await _http.GetByteArrayAsync("gallery/index.json", cancellationToken)
                .ConfigureAwait(false);

            var root = JsonObject.FromBytes(bytes);
            var items = root.GetArray("Items");

            var rows = new List<GalleryRow>(items?.Length ?? 0);
            for (int i = 0; i < (items?.Length ?? 0); i++)
                if (items!.GetObject(i) is { } entry)
                    rows.Add(GalleryRow.FromJson(entry));

            rows.Sort((a, b) => string.Compare(a.DisplayName, b.DisplayName, StringComparison.OrdinalIgnoreCase));
            return _rows = rows;
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>The rows belonging to one page.</summary>
    /// <param name="page">Page name, as <c>EntityKinds.Page</c> produces it.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>That page's rows.</returns>
    public async Task<IReadOnlyList<GalleryRow>> ForPageAsync(string page, CancellationToken cancellationToken = default)
        => [.. (await RowsAsync(cancellationToken).ConfigureAwait(false))
            .Where(r => string.Equals(r.Page, page, StringComparison.Ordinal))];
}
