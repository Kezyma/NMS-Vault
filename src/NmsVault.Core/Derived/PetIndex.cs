using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>One pet-battle affinity.</summary>
/// <param name="Id">The game's key - <c>Lush</c>, <c>Mech</c> and so on.</param>
/// <param name="Name">What the game calls it on screen: Lush is TROPICAL.</param>
/// <param name="Icon">Icon filename, or null for the one affinity with no glyph.</param>
public sealed record PetAffinity(string Id, string Name, string? Icon);

/// <summary>One battle move, resolved for a particular creature.</summary>
/// <param name="Id">The move id, without a leading caret.</param>
/// <param name="Name">Its name for this creature's affinity, or the bare id if unknown.</param>
/// <param name="Icon">Icon filename for its style, or null.</param>
/// <param name="Target">Who it acts on, in the game's words.</param>
/// <param name="Description">The game's own one-line summary.</param>
public sealed record PetMove(string Id, string Name, string? Icon, string? Target, string? Description);

/// <summary>
/// Works out a creature's affinity, and what its battle moves are called.
/// </summary>
/// <remarks>
/// <para>
/// Two lookups the gallery cannot do without the game's own tables.
/// </para>
/// <para>
/// <b>Affinity</b> is a species override first and the creature's biome second. A species whose
/// <c>PetBattlerForcedAffinity</c> is anything but <c>Normal</c> carries that whatever world it
/// came from - a Bonecat is Lush on a radioactive planet. <c>Normal</c> means "not forced", so
/// the biome decides, and every biome maps to one of the eight real affinities. Nothing in a
/// real save ends up with the ninth, which is why it has no icon.
/// </para>
/// <para>
/// <b>Move names</b> depend on the affinity as well as the move. The same ATTACK is "Lash" on a
/// tropical creature and "Freeze" on a frost one, so an id alone cannot be turned into a name -
/// which is why the gallery used to print the id.
/// </para>
/// </remarks>
public sealed class PetIndex
{
    private readonly IReadOnlyDictionary<string, PetAffinity> _affinities;
    private readonly IReadOnlyDictionary<string, string> _biomes;
    private readonly IReadOnlyDictionary<string, string> _forced;
    private readonly IReadOnlyDictionary<string, MoveEntry> _moves;

    private sealed record MoveEntry(
        string Id, string? Icon, string? Target, string? Description,
        IReadOnlyDictionary<string, string> Names);

    private PetIndex(
        IReadOnlyDictionary<string, PetAffinity> affinities,
        IReadOnlyDictionary<string, string> biomes,
        IReadOnlyDictionary<string, string> forced,
        IReadOnlyDictionary<string, MoveEntry> moves)
    {
        _affinities = affinities;
        _biomes = biomes;
        _forced = forced;
        _moves = moves;
    }

    /// <summary>An index with nothing in it, for when the companion data has not loaded.</summary>
    public static PetIndex Empty { get; } = new(
        new Dictionary<string, PetAffinity>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, MoveEntry>(StringComparer.Ordinal));

    /// <summary>How many affinities are known.</summary>
    public int Count => _affinities.Count;

    /// <summary>Reads an index from a <c>pets.json</c> document.</summary>
    /// <param name="bytes">The document bytes.</param>
    /// <returns>The loaded index.</returns>
    public static PetIndex FromBytes(ReadOnlySpan<byte> bytes)
    {
        var root = JsonObject.FromBytes(bytes);

        var affinities = new Dictionary<string, PetAffinity>(StringComparer.Ordinal);
        var list = root.GetArray("Affinities");

        for (int i = 0; i < (list?.Length ?? 0); i++)
        {
            var entry = list!.GetObject(i);
            if (entry?.GetString("Id") is not { Length: > 0 } id) continue;

            affinities[id] = new PetAffinity(id, entry.GetString("Name") ?? id, entry.GetString("Icon"));
        }

        var moves = new Dictionary<string, MoveEntry>(StringComparer.Ordinal);
        var written = root.GetArray("Moves");

        for (int i = 0; i < (written?.Length ?? 0); i++)
        {
            var entry = written!.GetObject(i);
            if (entry?.GetString("Id") is not { Length: > 0 } id) continue;

            moves[id] = new MoveEntry(
                id,
                entry.GetString("Icon"),
                entry.GetString("Target"),
                entry.GetString("Description"),
                Pairs(entry.GetObject("Names")));
        }

        return new PetIndex(affinities, Pairs(root.GetObject("BiomeAffinities")),
            Pairs(root.GetObject("ForcedAffinities")), moves);
    }

    private static Dictionary<string, string> Pairs(JsonObject? source)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source is null) return map;

        foreach (string name in source.Names())
            if (source.GetString(name) is { Length: > 0 } value) map[name] = value;

        return map;
    }

    /// <summary>
    /// The affinity a creature fights with, or null when it cannot be worked out.
    /// </summary>
    /// <param name="creatureId">The creature's id, caret and all.</param>
    /// <param name="biome">The creature's biome, as the payload spells it.</param>
    /// <returns>The affinity, or null.</returns>
    public PetAffinity? Affinity(string? creatureId, string? biome)
    {
        // The species override wins, which is the whole point of it being called forced.
        if (Bare(creatureId) is { Length: > 0 } species
            && _forced.TryGetValue(species, out string? overridden)
            && _affinities.TryGetValue(overridden, out var forced))
        {
            return forced;
        }

        if (biome is { Length: > 0 }
            && _biomes.TryGetValue(biome, out string? fromBiome)
            && _affinities.TryGetValue(fromBiome, out var derived))
        {
            return derived;
        }

        return null;
    }

    /// <summary>
    /// A battle move, named for the creature's affinity.
    /// </summary>
    /// <param name="moveId">The move id, caret and all.</param>
    /// <param name="affinity">The creature's affinity, or null if it is not known.</param>
    /// <returns>
    /// The move. Never null: an id nothing recognises comes back under its own name, which is
    /// what the gallery showed for every move before this existed.
    /// </returns>
    public PetMove Move(string moveId, PetAffinity? affinity)
    {
        string id = Bare(moveId) ?? "";

        if (!_moves.TryGetValue(id, out var entry))
            return new PetMove(id, id, null, null, null);

        string name = affinity is not null && entry.Names.TryGetValue(affinity.Id, out string? named)
            ? named
            : id;

        return new PetMove(id, name, entry.Icon, entry.Target, entry.Description);
    }

    /// <summary>Strips the caret that marks a game identifier.</summary>
    private static string? Bare(string? id)
        => id is { Length: > 0 } ? id.TrimStart('^') : null;
}
