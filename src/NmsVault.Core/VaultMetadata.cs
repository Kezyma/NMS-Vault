using NmsVault.Json;

namespace NmsVault.Core;

/// <summary>
/// The gallery's own fields, stored in a vault document under the <c>Vault</c> key.
/// <para>
/// These live in their own block rather than as extra keys on the game object, and that
/// is not a style preference. <c>JsonNameMapper.ToKey</c> passes unknown names through
/// unchanged, so any stray key left inside a live <c>ShipOwnership</c> entry would be
/// written verbatim into the player's real save - the same failure NMSE still carries
/// scrubbing code for with its legacy <c>__ShipCustomisation</c> key.
/// </para>
/// </summary>
public sealed record VaultMetadata
{
    /// <summary>The schema version written by this build.</summary>
    public const int CurrentSchemaVersion = 1;

    /// <summary>Schema version of the document this came from.</summary>
    public int SchemaVersion { get; init; } = CurrentSchemaVersion;

    /// <summary>URL slug. Stable forever once published - it is the item's permalink.</summary>
    public required string Id { get; init; }

    /// <summary>The name shown in the gallery, which need not match the in-game name.</summary>
    public required string DisplayName { get; init; }

    /// <summary>
    /// Other names people search for - community nicknames, the in-game name when it
    /// differs from the display name. Gallery search only; no editor format carries these.
    /// </summary>
    public IReadOnlyList<string> AlternativeNames { get; init; } = [];

    /// <summary>
    /// One line, shown on the card. Kept apart from <see cref="Description"/> because a card
    /// has room for a line and a modal has room for paragraphs, and truncating the long one
    /// to fit the card gives a sentence that stops mid-thought.
    /// </summary>
    public string Summary { get; init; } = "";

    /// <summary>
    /// The full text, shown in the item modal and carried into formats that have a
    /// description field. May run to several paragraphs.
    /// </summary>
    public string Description { get; init; } = "";

    /// <summary>
    /// Gallery-relative image paths, best first. The first is the card thumbnail.
    /// Kaii takes up to six (<c>Thumbnail</c>, <c>Thumbnail2</c>-<c>Thumbnail6</c>);
    /// NomNom takes up to six <c>Preview</c> slots.
    /// </summary>
    public IReadOnlyList<string> Images { get; init; } = [];

    /// <summary>Filter tags. Gallery search only; no editor format carries these.</summary>
    public IReadOnlyList<string> Tags { get; init; } = [];

    /// <summary>Who contributed the item, if they want the credit.</summary>
    public string? Author { get; init; }

    /// <summary>When the item was added to the gallery.</summary>
    public DateTimeOffset? DateAdded { get; init; }

    /// <summary>
    /// The game version the item was captured from, e.g. "7.03". Worth recording: payload
    /// shapes drift between game updates, and an item captured long ago may not import
    /// cleanly into a much newer save.
    /// </summary>
    public string? GameVersion { get; init; }

    /// <summary>Reads a metadata block from a vault document's <c>Vault</c> object.</summary>
    public static VaultMetadata FromJson(JsonObject vault) => new()
    {
        SchemaVersion = vault.Get("SchemaVersion") is int v ? v : CurrentSchemaVersion,
        Id = vault.GetString("Id") ?? "",
        DisplayName = vault.GetString("DisplayName") ?? "",
        AlternativeNames = ReadStrings(vault.GetArray("AlternativeNames")),
        Summary = vault.GetString("Summary") ?? "",
        Description = vault.GetString("Description") ?? "",
        Images = ReadStrings(vault.GetArray("Images")),
        Tags = ReadStrings(vault.GetArray("Tags")),
        Author = vault.GetString("Author"),
        DateAdded = DateTimeOffset.TryParse(vault.GetString("DateAdded"), out var d) ? d : null,
        GameVersion = vault.GetString("GameVersion"),
    };

    /// <summary>Writes this metadata as a <c>Vault</c> object.</summary>
    public JsonObject ToJson()
    {
        var obj = new JsonObject();
        obj.Set("SchemaVersion", SchemaVersion);
        obj.Set("Id", Id);
        obj.Set("DisplayName", DisplayName);
        if (AlternativeNames.Count > 0) obj.Set("AlternativeNames", WriteStrings(AlternativeNames));
        if (Summary.Length > 0) obj.Set("Summary", Summary);
        if (Description.Length > 0) obj.Set("Description", Description);
        if (Images.Count > 0) obj.Set("Images", WriteStrings(Images));
        if (Tags.Count > 0) obj.Set("Tags", WriteStrings(Tags));
        if (Author is not null) obj.Set("Author", Author);
        if (DateAdded is not null) obj.Set("DateAdded", DateAdded.Value.ToString("O"));
        if (GameVersion is not null) obj.Set("GameVersion", GameVersion);
        return obj;
    }

    private static IReadOnlyList<string> ReadStrings(JsonArray? arr)
    {
        if (arr is null || arr.Length == 0) return [];
        var list = new List<string>(arr.Length);
        for (int i = 0; i < arr.Length; i++)
            if (arr.Get(i) is string s) list.Add(s);
        return list;
    }

    private static JsonArray WriteStrings(IReadOnlyList<string> values)
    {
        var arr = new JsonArray();
        foreach (var v in values) arr.Add(v);
        return arr;
    }
}
