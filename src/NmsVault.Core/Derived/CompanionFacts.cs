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
/// What is here is what a creature <em>is</em>. What a particular save happened to leave - its
/// trust and its two moods - is not read at all: it is the last owner's state rather than the
/// creature's, and across twelve real creatures it was identical on every one of them.
/// </para>
/// <para>
/// The traits are not quite either. Comparing each creature with the egg it hatched from shows
/// them <em>drifting</em> - eight of twelve pairs differ, by a little: +0.90 against +0.82 -
/// so a personality is not fixed at hatching the way a seed is. It is close enough to a fact
/// about the creature to be worth showing, and far enough from one to be worth saying so.
/// </para>
/// </remarks>
/// <param name="CreatureType">Passive, Predator and so on, as the game writes it.</param>
/// <param name="Biome">Where it is from - Lush, Toxic, Barren.</param>
/// <param name="SpeciesId">The game's creature id, without its caret.</param>
/// <param name="IsPredator">Whether it hunts.</param>
/// <param name="HasFur">Whether it is furred.</param>
/// <param name="Traits">Helpfulness, aggression and independence, as stored.</param>
/// <param name="Seeds">Every seed the creature carries, labelled.</param>
/// <param name="BattleMoves">Its pet battle moves, as the game's ids without their carets.</param>
/// <param name="AccessorySlots">How many accessory slots it has.</param>
/// <param name="AccessoriesWorn">How many of them hold something.</param>
/// <param name="EggModified">Whether its egg was edited in the Sequencer before it hatched.</param>
/// <param name="CustomName">The name its owner gave it, or null.</param>
/// <param name="SpeciesName">
/// The loc id of the game's own name for its species, or null. Resolved through
/// <see cref="PetIndex.SpeciesName"/>, which is the only thing that knows the text behind it.
/// </param>
public sealed record CompanionFacts(
    string CreatureType,
    string? Biome,
    string? SpeciesId,
    bool IsPredator,
    bool HasFur,
    IReadOnlyList<CompanionValue> Traits,
    IReadOnlyList<LabelledSeed> Seeds,
    IReadOnlyList<string> BattleMoves,
    int AccessorySlots,
    int AccessoriesWorn,
    bool EggModified,
    string? CustomName,
    string? SpeciesName)
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
            pet.Get("Predator") is true,
            pet.Get("HasFur") is true,
            Read(pet.GetArray("Traits"), TraitLabels),
            SeedReader.ForCompanion(pet),
            Moves(pet.GetArray("PetBattlerMoves")),
            accessories?.Length ?? 0,
            Worn(accessories),

            // Whether the egg was edited in the Sequencer, which is what "hatched" rather than
            // "tamed" actually means.
            pet.Get("EggModified") is true,

            // What its owner called it, as opposed to what the gallery lists it under.
            Blank(pet.GetString("CustomName")),

            // A loc id, not a name: what it reads as needs the game's strings.
            Blank(pet.GetString("CustomSpeciesName")));
    }

    /// <summary>
    /// The numbers worth putting on a card beside a ship's stats.
    /// </summary>
    /// <remarks>
    /// <para>
    /// None, and none is the honest answer. A ship's stats rank it against other ships - more
    /// damage is better - and a creature has nothing of that shape. Scale was the last thing
    /// here and is not a score: 1.0 is small for a Diplodocus and enormous for a beetle, so a
    /// column of them compares nothing. Measuring it against the species range did not rescue
    /// it either, because that range describes wild spawns and a quarter of the creatures to
    /// hand fall outside their own.
    /// </para>
    /// <para>
    /// The three traits were here too, as the signed numbers the payload stores, and were the
    /// wrong shape for a column for their own reason: each names one end of an axis, so -1
    /// under "Aggression" is a wholly gentle creature and sorting put the gentlest and the
    /// fiercest at opposite ends of one scale. They are resolved into the game's own words at
    /// ingest instead - see <see cref="PetIndex.Traits"/>.
    /// </para>
    /// </remarks>
    /// <returns>Nothing. Kept so a creature answers the same question every other kind does.</returns>
    public IReadOnlyList<ItemStat> AsStats() => [];

    private static IReadOnlyList<CompanionValue> Read(JsonArray? values, string[] labels)
    {
        if (values is null) return [];

        var read = new List<CompanionValue>(labels.Length);
        for (int i = 0; i < labels.Length && i < values.Length; i++)
            read.Add(new CompanionValue(labels[i], Number(values.Get(i))));

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
