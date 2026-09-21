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
/// <param name="Icon">Glyph for the stat the move touches, or null.</param>
/// <param name="Affinity">The affinity it is typed to, or null when it is typed to none.</param>
/// <param name="Target">Who it acts on, in the game's words.</param>
/// <param name="Description">The game's own one-line summary.</param>
public sealed record PetMove(
    string Id, string Name, string? Icon, PetAffinity? Affinity, string? Target, string? Description);

/// <summary>One personality trait, read off the value the creature stores.</summary>
/// <param name="Name">The pole the value falls on - Helpfulness or Playfulness, not both.</param>
/// <param name="Percent">How far along that pole, 0 to 100.</param>
/// <param name="Class">The class letter the percentage falls in.</param>
/// <param name="Word">The game's word for that class - "Diligent".</param>
public sealed record PetTrait(string Name, int Percent, string Class, string? Word);

/// <summary>What an affinity fares badly and well against.</summary>
/// <param name="Weak">Affinities that beat it.</param>
/// <param name="Strong">Affinities it beats.</param>
public sealed record PetMatchup(IReadOnlyList<PetAffinity> Weak, IReadOnlyList<PetAffinity> Strong);

/// <summary>What the game's species table says about a creature's kind.</summary>
/// <param name="MoveArea">Where it gets about - Ground, Water, Air.</param>
/// <param name="Rarity">How often it turns up - Common through SuperRare.</param>
/// <param name="EggType">Which kind of egg it hatches from - DEFAULT or ROBO.</param>
/// <param name="CanBattle">Whether it can be taken into the arena at all.</param>
/// <remarks>
/// The species table also states a MinScale and a MaxScale, which are not read. They describe
/// wild spawns rather than companions and a quarter of the creatures to hand fall outside them,
/// so a creature's size measured against them said something false.
/// </remarks>
public sealed record PetSpecies(string? MoveArea, string? Rarity, string? EggType, bool CanBattle);

/// <summary>
/// Works out what the game would say about a creature: its affinity, what its moves are
/// called, what its personality reads as, and what its kind is.
/// </summary>
/// <remarks>
/// <para>
/// Lookups the gallery cannot do without the game's own tables.
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
/// <para>
/// <b>Traits</b> are three signed numbers and six words. Each slot is an axis and the sign picks
/// which end of it the creature is on. Which word belongs to which slot is inferred rather than
/// stated - see the remarks on the table in <c>PetExtractor</c>, which is the one place to
/// change if the game disagrees.
/// </para>
/// </remarks>
public sealed class PetIndex
{
    private readonly IReadOnlyDictionary<string, PetAffinity> _affinities;
    private readonly IReadOnlyDictionary<string, string> _biomes;
    private readonly IReadOnlyDictionary<string, string> _forced;
    private readonly IReadOnlyDictionary<string, string> _climates;
    private readonly IReadOnlyDictionary<string, string> _speciesNames;
    private readonly IReadOnlyDictionary<string, MoveEntry> _moves;
    private readonly IReadOnlyDictionary<string, (string[] Weak, string[] Strong)> _matchups;
    private readonly IReadOnlyDictionary<string, SpeciesEntry> _species;
    private readonly IReadOnlyDictionary<string, IReadOnlyList<Axis>> _traits;
    private readonly IReadOnlyList<(string Class, double From)> _bands;

    private sealed record MoveEntry(
        string Id, string? Icon, string? Affinity, string? Target, string? Description,
        IReadOnlyDictionary<string, string> Names);


    private sealed record SpeciesEntry(PetSpecies Facts, string TraitSet);

    private sealed record Pole(string Name, IReadOnlyDictionary<string, string> Classes);

    private sealed record Axis(Pole Positive, Pole Negative);

    private PetIndex(
        IReadOnlyDictionary<string, PetAffinity> affinities,
        IReadOnlyDictionary<string, string> biomes,
        IReadOnlyDictionary<string, string> forced,
        IReadOnlyDictionary<string, string> climates,
        IReadOnlyDictionary<string, string> speciesNames,
        IReadOnlyDictionary<string, MoveEntry> moves,
        IReadOnlyDictionary<string, (string[] Weak, string[] Strong)> matchups,
        IReadOnlyDictionary<string, SpeciesEntry> species,
        IReadOnlyDictionary<string, IReadOnlyList<Axis>> traits,
        IReadOnlyList<(string Class, double From)> bands)
    {
        _affinities = affinities;
        _biomes = biomes;
        _forced = forced;
        _climates = climates;
        _speciesNames = speciesNames;
        _moves = moves;
        _matchups = matchups;
        _species = species;
        _traits = traits;
        _bands = bands;
    }

    /// <summary>An index with nothing in it, for when the companion data has not loaded.</summary>
    public static PetIndex Empty { get; } = new(
        new Dictionary<string, PetAffinity>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, string>(StringComparer.Ordinal),
        new Dictionary<string, MoveEntry>(StringComparer.Ordinal),
        new Dictionary<string, (string[], string[])>(StringComparer.Ordinal),
        new Dictionary<string, SpeciesEntry>(StringComparer.Ordinal),
        new Dictionary<string, IReadOnlyList<Axis>>(StringComparer.Ordinal),
        []);

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
                entry.GetString("Affinity"),
                entry.GetString("Target"),
                entry.GetString("Description"),
                Pairs(entry.GetObject("Names")));
        }

        return new PetIndex(
            affinities,
            Pairs(root.GetObject("BiomeAffinities")),
            Pairs(root.GetObject("ForcedAffinities")),
            Pairs(root.GetObject("Climates")),
            Pairs(root.GetObject("SpeciesNames")),
            moves,
            Matchups(root.GetObject("Matchups")),
            Species(root.GetObject("Species")),
            TraitSets(root.GetObject("Traits")),
            Bands(root.GetObject("Traits")?.GetArray("Bands")));
    }

    private static Dictionary<string, string> Pairs(JsonObject? source)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (source is null) return map;

        foreach (string name in source.Names())
            if (source.GetString(name) is { Length: > 0 } value) map[name] = value;

        return map;
    }

    private static string[] Texts(JsonArray? source)
    {
        if (source is null) return [];

        var values = new List<string>(source.Length);

        for (int i = 0; i < source.Length; i++)
            if (source.Get(i) is string value) values.Add(value);

        return [.. values];
    }

    private static Dictionary<string, (string[] Weak, string[] Strong)> Matchups(JsonObject? source)
    {
        var map = new Dictionary<string, (string[], string[])>(StringComparer.Ordinal);
        if (source is null) return map;

        foreach (string name in source.Names())
            if (source.GetObject(name) is { } entry)
                map[name] = (Texts(entry.GetArray("Weak")), Texts(entry.GetArray("Strong")));

        return map;
    }

    private static Dictionary<string, SpeciesEntry> Species(JsonObject? source)
    {
        var map = new Dictionary<string, SpeciesEntry>(StringComparer.Ordinal);
        if (source is null) return map;

        foreach (string name in source.Names())
        {
            if (source.GetObject(name) is not { } entry) continue;

            map[name] = new SpeciesEntry(
                new PetSpecies(
                    entry.GetString("MoveArea"),
                    entry.GetString("Rarity"),
                    entry.GetString("EggType"),
                    // Written only when the species cannot fight, because nearly all can.
                    entry.Get("NoBattle") is not true),
                entry.GetString("TraitSet") ?? "Default");
        }

        return map;
    }

    private static Dictionary<string, IReadOnlyList<Axis>> TraitSets(JsonObject? source)
    {
        var map = new Dictionary<string, IReadOnlyList<Axis>>(StringComparer.Ordinal);
        if (source is null) return map;

        foreach (string name in source.Names())
        {
            if (source.GetArray(name) is not { } axes) continue;

            var read = new List<Axis>(axes.Length);

            for (int i = 0; i < axes.Length; i++)
            {
                var axis = axes.GetObject(i);
                if (ReadPole(axis?.GetObject("Positive")) is not { } positive) continue;
                if (ReadPole(axis?.GetObject("Negative")) is not { } negative) continue;

                read.Add(new Axis(positive, negative));
            }

            if (read.Count > 0) map[name] = read;
        }

        return map;
    }

    /// <summary>A number, or null where the property is absent rather than zero.</summary>
    private static double? Number(JsonObject source, string name) => source.Get(name) switch
    {
        RawDouble raw => raw.Value,
        double value => value,
        long value => value,
        int value => value,
        _ => null,
    };

    private static Pole? ReadPole(JsonObject? source)
        => source?.GetString("Name") is { Length: > 0 } name
            ? new Pole(name, Pairs(source.GetObject("Classes")))
            : null;

    private static (string Class, double From)[] Bands(JsonArray? source)
    {
        if (source is null) return [];

        var bands = new List<(string, double)>(source.Length);

        for (int i = 0; i < source.Length; i++)
        {
            var entry = source.GetObject(i);
            if (entry?.GetString("Class") is not { Length: > 0 } cls) continue;

            bands.Add((cls, Number(entry, "From") ?? 0));
        }

        // Strongest first, so the first band a value clears is the one it belongs to.
        return [.. bands.OrderByDescending(b => b.Item2)];
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
            return new PetMove(id, id, null, null, null, null);

        string name = affinity is not null && entry.Names.TryGetValue(affinity.Id, out string? named)
            ? named
            : id;

        // "Self" means the move takes whatever the creature is; anything else names a fixed
        // affinity; absent means the move is typed to nothing and draws no affinity glyph.
        var typed = entry.Affinity switch
        {
            "Self" => affinity,
            { Length: > 0 } fixedId => _affinities.GetValueOrDefault(fixedId),
            _ => null,
        };

        return new PetMove(id, name, entry.Icon, typed, entry.Target, entry.Description);
    }

    /// <summary>
    /// The creature's personality, one entry per trait it stores.
    /// </summary>
    /// <param name="creatureId">The creature's id, caret and all.</param>
    /// <param name="values">The stored trait values, each between -1 and 1.</param>
    /// <returns>
    /// One trait per value the tables can name, in stored order. Empty when the companion data
    /// has not loaded, so the caller can fall back to showing the raw numbers.
    /// </returns>
    public IReadOnlyList<PetTrait> Traits(string? creatureId, IReadOnlyList<double> values)
    {
        string set = Bare(creatureId) is { Length: > 0 } species
            && _species.TryGetValue(species, out var entry)
                ? entry.TraitSet
                : "Default";

        if (!_traits.TryGetValue(set, out var axes) && !_traits.TryGetValue("Default", out axes))
            return [];

        var traits = new List<PetTrait>(values.Count);

        for (int i = 0; i < values.Count && i < axes.Count; i++)
        {
            double value = values[i];

            // The sign picks the end of the axis; the magnitude says how far along it.
            var pole = value < 0 ? axes[i].Negative : axes[i].Positive;
            int percent = (int)Math.Round(Math.Clamp(Math.Abs(value), 0, 1) * 100);
            string cls = Class(percent);

            traits.Add(new PetTrait(pole.Name, percent, cls, pole.Classes.GetValueOrDefault(cls)));
        }

        return traits;
    }

    /// <summary>The class a trait percentage falls in.</summary>
    private string Class(int percent)
    {
        foreach (var (cls, from) in _bands)
            if (percent >= from) return cls;

        return _bands.Count > 0 ? _bands[^1].Class : "C";
    }

    /// <summary>
    /// What an affinity fares badly and well against, or null when it is not known.
    /// </summary>
    /// <param name="affinity">The affinity to look up.</param>
    /// <returns>The matchup, or null.</returns>
    public PetMatchup? Matchup(PetAffinity? affinity)
    {
        if (affinity is null || !_matchups.TryGetValue(affinity.Id, out var found)) return null;

        return new PetMatchup(Resolve(found.Weak), Resolve(found.Strong));
    }

    private PetAffinity[] Resolve(IReadOnlyList<string> ids)
        => [.. ids.Select(id => _affinities.GetValueOrDefault(id)).OfType<PetAffinity>()];

    /// <summary>
    /// The climate a creature came from, in the game's words.
    /// </summary>
    /// <param name="biome">The biome as the payload spells it.</param>
    /// <returns>
    /// The game's name for it - a Lush world is Verdant - falling back to the raw biome, which
    /// is what the gallery showed before.
    /// </returns>
    public string? Climate(string? biome)
        => biome is { Length: > 0 } ? _climates.GetValueOrDefault(biome, biome) : null;

    /// <summary>
    /// What the game's species table says about a creature's kind, or null if it says nothing.
    /// </summary>
    /// <param name="creatureId">The creature's id, caret and all.</param>
    /// <returns>The species facts, or null.</returns>
    public PetSpecies? Species(string? creatureId)
        => Bare(creatureId) is { Length: > 0 } species && _species.TryGetValue(species, out var entry)
            ? entry.Facts
            : null;

    /// <summary>
    /// The game's own name for a creature's species, or null when it has none.
    /// </summary>
    /// <remarks>
    /// This is a creature's type in the sense a ship's is Fighter: what kind of thing it is.
    /// <c>CreatureType</c> is not - it reads Passive on half of them and names a body shape on
    /// the rest - so the gallery asks here first and falls back to that only when the payload
    /// carries no name, which one of the twelve creatures to hand does not.
    /// </remarks>
    /// <param name="customSpeciesName">The loc id the creature stores, caret and all.</param>
    /// <returns>The name, or null.</returns>
    public string? SpeciesName(string? customSpeciesName)
        => Bare(customSpeciesName) is { Length: > 0 } key
            ? _speciesNames.GetValueOrDefault(key)
            : null;

    /// <summary>Strips the caret that marks a game identifier.</summary>
    private static string? Bare(string? id)
        => id is { Length: > 0 } ? id.TrimStart('^') : null;
}
