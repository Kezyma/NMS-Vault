using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>One technology, as the gallery needs to show it.</summary>
/// <param name="Id">The game's id, without a leading caret.</param>
/// <param name="Name">Display name.</param>
/// <param name="Subtitle">The game's own grouping line, e.g. "Lightspeed Warp Drive".</param>
/// <param name="Description">Flavour and effect text.</param>
/// <param name="Icon">Icon filename, relative to the gallery's technology icon folder.</param>
/// <param name="Category">Which equipment this fits - Ship, Weapon, AlienShip and so on.</param>
public sealed record TechEntry(
    string Id,
    string Name,
    string? Subtitle,
    string? Description,
    string? Icon,
    string? Category);

/// <summary>
/// Looks up what an installed technology id actually is.
/// </summary>
/// <remarks>
/// <para>
/// Resolution has to undo three separate conventions before an id matches the database, and
/// all three appear in real saves:
/// </para>
/// <list type="bullet">
/// <item>A leading caret marks a game identifier: <c>^HYPERDRIVE</c>.</item>
/// <item>
/// A <c>#NNNNN</c> suffix is a procedural variant - <c>^UP_PULSE4#35271</c> is one roll of
/// <c>UP_PULSE4</c>. The variants share a database entry.
/// </item>
/// <item>
/// Some cosmetics are stored with a <c>T_</c> prefix the database does not carry:
/// <c>^T_BOBBLE_ATLAS</c> is <c>BOBBLE_ATLAS</c>. NMSE has the same fallback.
/// </item>
/// </list>
/// </remarks>
public sealed class TechIndex
{
    private readonly IReadOnlyDictionary<string, TechEntry> _byId;

    private TechIndex(IReadOnlyDictionary<string, TechEntry> byId) => _byId = byId;

    /// <summary>An index with nothing in it, for when the technology data has not loaded.</summary>
    public static TechIndex Empty { get; } = new(new Dictionary<string, TechEntry>(StringComparer.Ordinal));

    /// <summary>How many technologies are known.</summary>
    public int Count => _byId.Count;

    /// <summary>Reads an index from a <c>tech.json</c> document.</summary>
    /// <param name="bytes">The document bytes.</param>
    /// <returns>The loaded index.</returns>
    public static TechIndex FromBytes(ReadOnlySpan<byte> bytes)
    {
        var root = JsonObject.FromBytes(bytes);
        var entries = root.GetArray("Technologies");

        var byId = new Dictionary<string, TechEntry>(entries?.Length ?? 0, StringComparer.Ordinal);
        for (int i = 0; i < (entries?.Length ?? 0); i++)
        {
            var entry = entries!.GetObject(i);
            if (entry?.GetString("Id") is not { Length: > 0 } id) continue;

            byId[id] = new TechEntry(
                id,
                entry.GetString("Name") ?? id,
                entry.GetString("Subtitle"),
                entry.GetString("Description"),
                entry.GetString("Icon"),
                entry.GetString("Category"));
        }

        return new TechIndex(byId);
    }

    /// <summary>Builds an index from entries already in memory.</summary>
    /// <param name="entries">The technologies.</param>
    /// <returns>The index.</returns>
    public static TechIndex From(IEnumerable<TechEntry> entries)
        => new(entries.GroupBy(e => e.Id, StringComparer.Ordinal)
                      .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal));

    /// <summary>Resolves an installed id, or null when nothing matches.</summary>
    /// <param name="installedId">The id as the save stores it, carets and variants included.</param>
    /// <returns>The technology, or null.</returns>
    public TechEntry? Find(string? installedId)
    {
        if (string.IsNullOrEmpty(installedId)) return null;

        string key = Normalise(installedId);
        if (_byId.TryGetValue(key, out var entry)) return entry;

        // Cosmetics are stored with a T_ prefix the database does not carry.
        return key.StartsWith("T_", StringComparison.Ordinal)
            && _byId.TryGetValue(key[2..], out var stripped) ? stripped : null;
    }

    /// <summary>
    /// Strips the caret and any procedural variant suffix, leaving the database key.
    /// </summary>
    /// <param name="installedId">The id as the save stores it.</param>
    /// <returns>The bare id.</returns>
    public static string Normalise(string installedId)
        => TechSlot.StripVariant(installedId).TrimStart('^');
}
