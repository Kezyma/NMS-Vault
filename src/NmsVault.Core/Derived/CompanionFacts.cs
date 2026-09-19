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
    int AccessoriesWorn)
{
    /// <summary>What the three <c>Traits</c> entries mean, in order.</summary>
    /// <remarks>NMSE's own labels - see its CompanionPanel, which writes to these positions.</remarks>
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
            Worn(accessories));
    }

    /// <summary>
    /// The numbers worth putting on a card beside a ship's stats: its size, then what it is
    /// like. Everything here is settled when the creature hatches.
    /// </summary>
    /// <returns>The stats, in the order they should read.</returns>
    public IReadOnlyList<ItemStat> AsStats() =>
    [
        new ItemStat("#SCALE", "Scale", Scale),
        .. Traits.Select(t => new ItemStat($"#{t.Label.ToUpperInvariant()}", t.Label, t.Value)),
    ];

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
