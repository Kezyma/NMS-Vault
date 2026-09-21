using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>A named number read off a creature.</summary>
/// <param name="Label">What it is called.</param>
/// <param name="Value">Its value.</param>
public readonly record struct CompanionValue(string Label, double Value);

/// <summary>
/// Everything a creature's payload says about it.
/// </summary>
/// <remarks>
/// <para>
/// A pet object holds far more than a ship does, and almost none of it is named: traits and
/// moods are bare arrays of numbers, and which position means what is knowable only from an
/// editor that reads them. The labels here are NMSE's, taken from its companion panel, so a
/// reader who edits a creature there and looks it up here sees the same words for the same
/// fields.
/// </para>
/// <para>
/// The split that matters is between what a creature <em>is</em> and how a particular save
/// left it. Scale and the three traits are fixed at hatching and are what anyone would choose
/// a companion by; trust and the two moods drift with play and say more about the last owner
/// than about the creature. Both are shown, and they are shown apart.
/// </para>
/// </remarks>
/// <param name="CreatureType">Passive, Predator and so on, as the game writes it.</param>
/// <param name="Biome">Where it is from - Lush, Toxic, Barren.</param>
/// <param name="SpeciesId">The game's creature id, without its caret.</param>
/// <param name="Scale">Its size. The same species ranges widely.</param>
/// <param name="IsPredator">Whether it hunts.</param>
/// <param name="HasFur">Whether it is furred.</param>
/// <param name="Traits">Helpfulness, aggression and independence - fixed at hatching.</param>
/// <param name="Moods">Hunger and loneliness - how the last save left it.</param>
/// <param name="Trust">How far it had been won over, from nothing to one.</param>
/// <param name="Seeds">Every seed the creature carries, labelled.</param>
/// <param name="BattleMoves">Its pet battle moves, as the game's ids without their carets.</param>
/// <param name="AccessorySlots">How many accessory slots it has.</param>
/// <param name="AccessoriesWorn">How many of them hold something.</param>
/// <param name="UniverseAddress">Where it came from, as the game's packed address. Null if absent.</param>
/// <param name="EggModified">Whether its egg was edited in the Sequencer before it hatched.</param>
/// <param name="CustomName">The name its owner gave it, or null.</param>
/// <param name="BattleClasses">
/// The stored class letter for health, agility and combat effectiveness, in that order. These
/// only mean anything when <paramref name="BattleClassesApply"/> is set.
/// </param>
/// <param name="BattleClassesApply">
/// Whether the game reads those stored classes. False on everything hatched normally, in which
/// case the real classes are generated and the payload does not hold them.
/// </param>
/// <param name="GeneEdits">
/// How far health, agility and combat effectiveness have each been raised by feeding, 0 to 10.
/// </param>
/// <param name="GeneEditsAvailable">How many edits it has banked and not spent.</param>
/// <param name="MutationProgress">How far along it is towards earning the next one, 0 to 1.</param>
/// <param name="ArenaVictories">How many pet battles it has won.</param>
public sealed record CompanionFacts(
    string CreatureType,
    string? Biome,
    string? SpeciesId,
    double Scale,
    bool IsPredator,
    bool HasFur,
    IReadOnlyList<CompanionValue> Traits,
    IReadOnlyList<CompanionValue> Moods,
    double Trust,
    IReadOnlyList<LabelledSeed> Seeds,
    IReadOnlyList<string> BattleMoves,
    int AccessorySlots,
    int AccessoriesWorn,
    string? UniverseAddress,
    bool EggModified,
    string? CustomName,
    IReadOnlyList<string> BattleClasses,
    bool BattleClassesApply,
    IReadOnlyList<int> GeneEdits,
    int GeneEditsAvailable,
    double MutationProgress,
    int ArenaVictories)
{
    /// <summary>What the three <c>Traits</c> entries mean, in order.</summary>
    /// <remarks>
    /// NMSE's own labels - see its CompanionPanel, which writes to these positions. Each names
    /// one end of an axis, so a negative value means the creature is at the other end of it:
    /// -1 under Aggression is a wholly gentle creature, not an aggressive one. These stay as
    /// they are because they head a sortable column, where a signed number down one axis is
    /// what a reader wants; <see cref="PetIndex.Traits"/> turns the same numbers into the
    /// game's own words for the places with room to read them.
    /// </remarks>
    public static readonly string[] TraitLabels = ["Helpfulness", "Aggression", "Independence"];

    /// <summary>What the two <c>Moods</c> entries mean, in order.</summary>
    /// <inheritdoc cref="TraitLabels"/>
    public static readonly string[] MoodLabels = ["Hungry", "Lonely"];

    /// <summary>Reads everything off a creature.</summary>
    /// <param name="pet">The creature payload.</param>
    /// <param name="accessorySlots">
    /// Its accessory slots. A separate argument because the importer lifts them out of the
    /// payload into a sidecar - NMSE splices them in flat and the other editors nest them
    /// differently, so the vault keeps them beside the creature rather than inside it.
    /// </param>
    /// <returns>Its facts.</returns>
    public static CompanionFacts For(JsonObject pet, JsonArray? accessorySlots = null)
    {
        var accessories = accessorySlots ?? pet.GetArray("PetAccessoryCustomisation");

        return new CompanionFacts(
            pet.GetObject("CreatureType")?.GetString("CreatureType") ?? "Companion",
            pet.GetObject("Biome")?.GetString("Biome"),
            pet.GetString("CreatureID")?.TrimStart('^'),
            Number(pet.Get("Scale")),
            pet.Get("Predator") is true,
            pet.Get("HasFur") is true,
            Read(pet.GetArray("Traits"), TraitLabels),
            Read(pet.GetArray("Moods"), MoodLabels),
            Number(pet.Get("Trust")),
            SeedReader.ForCompanion(pet),
            Moves(pet.GetArray("PetBattlerMoves")),
            accessories?.Length ?? 0,
            Worn(accessories),

            // Where it came from. The only thing in the payload that says so, and it decodes
            // to a galaxy, a system and a planet.
            Blank(pet.GetString("UA")),

            // Whether the egg was edited in the Sequencer, which is what "hatched" rather than
            // "tamed" actually means.
            pet.Get("EggModified") is true,

            // What its owner called it, as opposed to what the gallery lists it under.
            Blank(pet.GetString("CustomName")),

            // The arena. Classes first - stored, but inert unless the flag beside them is set,
            // which it is not on anything that hatched the ordinary way.
            Classes(pet.GetArray("PetBattlerCoreStatClassOverrides")),
            pet.Get("PetBattlerUseCoreStatClassOverrides") is true,

            // Then what feeding it has actually changed, which is real on any creature.
            Counts(pet.GetArray("PetBattlerTreatsEaten")),
            (int)Number(pet.Get("PetBattlerTreatsAvailable")),
            Number(pet.Get("PetBattleProgressToTreat")),
            (int)Number(pet.Get("PetBattlerVictories")));
    }

    /// <summary>How many gene edits have been spent across the three stats.</summary>
    public int GeneEditsSpent => GeneEdits.Sum();

    /// <summary>
    /// The most any creature can be improved by feeding: ten edits on each of three stats.
    /// </summary>
    public const int GeneEditLimit = 30;

    /// <summary>
    /// The numbers worth putting on a card beside a ship's stats.
    /// </summary>
    /// <remarks>
    /// Only the size. The three traits used to sit here too, as the signed numbers the payload
    /// stores, and they were the wrong shape for a column: each names one end of an axis, so
    /// -1 under "Aggression" is a wholly gentle creature and sorting the column put the
    /// gentlest and the fiercest at opposite ends of a scale nobody reads that way. They are
    /// resolved into the game's own words at ingest instead - see <see cref="PetIndex.Traits"/>.
    /// </remarks>
    /// <returns>The stats, in the order they should read.</returns>
    public IReadOnlyList<ItemStat> AsStats() => [new ItemStat("#SCALE", "Scale", Scale)];

    private static IReadOnlyList<CompanionValue> Read(JsonArray? values, string[] labels)
    {
        if (values is null) return [];

        var read = new List<CompanionValue>(labels.Length);
        for (int i = 0; i < labels.Length && i < values.Length; i++)
            read.Add(new CompanionValue(labels[i], Number(values.Get(i))));

        return read;
    }

    /// <summary>
    /// The class letters out of a class-override array, which wraps each one in an object.
    /// </summary>
    private static IReadOnlyList<string> Classes(JsonArray? values)
    {
        if (values is null) return [];

        var read = new List<string>(values.Length);
        for (int i = 0; i < values.Length; i++)
            read.Add(values.GetObject(i)?.GetString("InventoryClass") ?? "");

        return read;
    }

    private static IReadOnlyList<int> Counts(JsonArray? values)
    {
        if (values is null) return [];

        var read = new List<int>(values.Length);
        for (int i = 0; i < values.Length; i++)
            read.Add((int)Number(values.Get(i)));

        return read;
    }

    private static IReadOnlyList<string> Moves(JsonArray? moves)
    {
        if (moves is null) return [];

        var read = new List<string>(moves.Length);
        for (int i = 0; i < moves.Length; i++)
            if (moves.Get(i)?.ToString()?.TrimStart('^') is { Length: > 0 } id)
                read.Add(id);

        return read;
    }

    /// <summary>
    /// How many accessory slots hold something. An empty one is the default preset with no
    /// customisation behind it, which is what the game writes for a slot nothing is in.
    /// </summary>
    private static int Worn(JsonArray? accessories)
    {
        int worn = 0;

        for (int i = 0; i < (accessories?.Length ?? 0); i++)
        {
            var slot = accessories!.GetObject(i);
            if (slot is null) continue;

            string preset = slot.GetString("SelectedPreset") ?? "";
            var custom = slot.GetObject("CustomData");

            bool empty = preset is "" or "^" or "^DEFAULT_PET"
                && (custom?.GetArray("DescriptorGroups")?.Length ?? 0) == 0;

            if (!empty) worn++;
        }

        return worn;
    }

    /// <summary>An empty string means the payload does not say, not that it says nothing.</summary>
    private static string? Blank(string? value) => value is { Length: > 0 } ? value : null;

    private static double Number(object? value) => value switch
    {
        double d => d,
        RawDouble r => r.Value,
        int i => i,
        long l => l,
        float f => f,
        _ => 0,
    };
}
