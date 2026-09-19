using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>
/// Everything the gallery needs to render a card, fill a filter and sort a column, derived
/// from an item's payload.
/// </summary>
/// <remarks>
/// <para>
/// Computed at ingest and written into <c>index.json</c> rather than derived in the browser.
/// That is not a performance nicety: filtering on installed technology or on a stat would
/// otherwise mean fetching every item document before the first filter could be applied.
/// The full document is fetched only when someone opens an item.
/// </para>
/// </remarks>
/// <param name="Type">Display type - "Hauler", "Staff", "Cat".</param>
/// <param name="IsModifiedResource">
/// True when the model path resolved only by keyword, which usually means a modded or
/// hand-edited resource. Worth a badge.
/// </param>
/// <param name="Family">Ownership family, which governs what technology fits.</param>
/// <param name="Class">Inventory class - S, A, B, C - or null where the kind has none.</param>
/// <param name="Seeds">Seeds worth showing, labelled. Ships have one; companions have four.</param>
/// <param name="Stats">
/// The game's base stats in the order it presents them, followed by the slot counts the
/// gallery derives - see <see cref="SlotCounts"/>. One list, because a reader comparing two
/// ships does not care which numbers the save carried and which were counted.
/// </param>
/// <param name="InstalledTech">Distinct base technology ids installed.</param>
/// <param name="Biome">
/// Where a creature is from, e.g. "Lush". Null for everything else - a ship has no biome.
/// </param>
/// <param name="RolledTech">
/// The subset of <paramref name="InstalledTech"/> that are procedurally rolled upgrade
/// modules. Recorded separately so the filter can leave them out while the count on the card
/// still says how much is actually installed.
/// </param>
public sealed record ItemFacts(
    string Type,
    bool IsModifiedResource,
    string Family,
    string? Class,
    IReadOnlyList<LabelledSeed> Seeds,
    IReadOnlyList<ItemStat> Stats,
    IReadOnlyList<string> InstalledTech,
    IReadOnlyList<string> RolledTech,
    string? Biome = null)
{
    /// <summary>Derives the facts for an item.</summary>
    public static ItemFacts For(VaultItem item) => item.Kind switch
    {
        EntityKind.Starship => ForStarship(item.Payload),
        EntityKind.Multitool => ForMultitool(item.Payload),
        EntityKind.Companion => ForCompanion(item.Payload),
        _ => Minimal(item.Kind),
    };

    private static ItemFacts ForStarship(JsonObject ship)
    {
        var type = ShipTypes.FromShip(ship);
        var inventory = ship.GetObject("Inventory");
        var tech = TechGrids.Build(ship.GetObject("Inventory_TechOnly"));

        return new ItemFacts(
            type.Type,
            type.IsModified,
            type.Family.ToString(),
            inventory?.GetObject("Class")?.GetString("InventoryClass"),
            SeedReader.ForShip(ship),
            [.. ItemStats.ForShip(ship), .. SlotCounts.ForShip(ship)],
            tech?.InstalledBaseIds ?? [],
            tech?.RolledBaseIds ?? []);
    }

    private static ItemFacts ForMultitool(JsonObject multitool)
    {
        var store = multitool.GetObject("Store");
        var tech = TechGrids.Build(store);

        return new ItemFacts(
            MultitoolTypes.FromMultitool(multitool),
            IsModifiedResource: false,
            Family: "Multitool",
            store?.GetObject("Class")?.GetString("InventoryClass"),
            SeedReader.ForMultitool(multitool),
            [.. ItemStats.ForMultitool(multitool), .. SlotCounts.ForMultitool(multitool)],
            tech?.InstalledBaseIds ?? [],
            tech?.RolledBaseIds ?? []);
    }

    /// <summary>
    /// What a companion is, read straight out of its payload.
    /// </summary>
    /// <remarks>
    /// No database is involved. The game writes the creature's type and its biome into the
    /// pet object as plain words - <c>Passive</c>, <c>Lush</c> - so the two things anyone
    /// browses creatures by are already there.
    /// <para>
    /// What is not here is the battle classes: <c>PetBattlerCoreStatClassOverrides</c> holds
    /// three of them, but only applies when <c>PetBattlerUseCoreStatClassOverrides</c> is
    /// set, and on a hatched creature it is not - the real classes are procedural and would
    /// need the rules the game rolls them with. Showing the overrides regardless would be
    /// inventing three letters.
    /// </para>
    /// </remarks>
    private static ItemFacts ForCompanion(JsonObject pet)
        => new(
            pet.GetObject("CreatureType")?.GetString("CreatureType") ?? "Companion",
            IsModifiedResource: false,
            Family: "Companion",
            Class: null,
            SeedReader.ForCompanion(pet),
            Stats: [],
            InstalledTech: [],
            RolledTech: [],
            Biome: pet.GetObject("Biome")?.GetString("Biome"));

    private static ItemFacts Minimal(EntityKind kind)
        => new(kind.ToString(), false, kind.ToString(), null, [], [], [], []);

    /// <summary>Writes these facts into an index entry.</summary>
    public void WriteTo(JsonObject entry)
    {
        entry.Set("Type", Type);
        if (IsModifiedResource) entry.Set("Modified", true);
        entry.Set("Family", Family);
        if (Class is not null) entry.Set("Class", Class);
        if (Biome is not null) entry.Set("Biome", Biome);

        if (Seeds.Count > 0)
        {
            var seeds = new JsonObject();
            foreach (var seed in Seeds) seeds.Set(seed.Label, seed.Value);
            entry.Set("Seeds", seeds);
        }

        if (Stats.Count > 0)
        {
            var stats = new JsonObject();
            foreach (var stat in Stats) stats.Set(stat.Label, stat.Value);
            entry.Set("Stats", stats);
        }

        if (InstalledTech.Count > 0) entry.Set("Tech", ToArray(InstalledTech));
        if (RolledTech.Count > 0) entry.Set("Rolled", ToArray(RolledTech));
    }

    private static JsonArray ToArray(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (string value in values) array.Add(value);
        return array;
    }
}

/// <summary>A seed worth showing, with the label it goes under.</summary>
/// <param name="Label">What this seed governs, e.g. "Seed" or "Colour".</param>
/// <param name="Value">The seed, as the save stores it - a <c>0x</c>-prefixed hex string.</param>
public readonly record struct LabelledSeed(string Label, string Value);

/// <summary>
/// Reads the seeds an item carries.
/// </summary>
/// <remarks>
/// Ships and multitools keep theirs in different places, which is easy to get wrong: a ship's
/// is <c>Resource.Seed[1]</c>, but a multitool's is <c>Seed[1]</c> on the object root. The
/// multitool also has a <c>Resource.Seed</c>, which NMSE never displays.
/// </remarks>
public static class SeedReader
{
    /// <summary>The seed a ship was generated from.</summary>
    public static IReadOnlyList<LabelledSeed> ForShip(JsonObject ship)
        => Read(ship.GetObject("Resource")?.GetArray("Seed")) is { } seed
            ? [new LabelledSeed("Seed", seed)]
            : [];

    /// <summary>The seed a multitool was generated from - from the root, not Resource.</summary>
    public static IReadOnlyList<LabelledSeed> ForMultitool(JsonObject multitool)
        => Read(multitool.GetArray("Seed")) is { } seed
            ? [new LabelledSeed("Seed", seed)]
            : [];

    /// <summary>
    /// The two seeds that decide what a creature is.
    /// </summary>
    /// <remarks>
    /// Stored as plain hex strings rather than the <c>[occupied, "0xHEX"]</c> pair every other
    /// seed uses. The pet object has three more - <c>CreatureSeed</c>, <c>ColourBaseSeed</c>
    /// and <c>BoneScaleSeed</c> - but those are pairs whose flag is unset on a hatched
    /// creature, so there is nothing in them to show.
    /// </remarks>
    public static IReadOnlyList<LabelledSeed> ForCompanion(JsonObject pet)
    {
        var seeds = new List<LabelledSeed>(2);

        if (pet.GetString("SpeciesSeed") is { Length: > 0 } species)
            seeds.Add(new LabelledSeed("Species", species));

        if (pet.GetString("GenusSeed") is { Length: > 0 } genus)
            seeds.Add(new LabelledSeed("Genus", genus));

        return seeds;
    }

    /// <summary>
    /// Reads the display half of a seed pair. The save stores <c>[occupied, "0xHEX"]</c>;
    /// element 0 is a slot-occupancy flag, not part of the seed.
    /// </summary>
    private static string? Read(JsonArray? seedPair)
    {
        if (seedPair is null || seedPair.Length < 2) return null;
        string? value = seedPair.Get(1)?.ToString();
        return string.IsNullOrEmpty(value) ? null : value;
    }
}
