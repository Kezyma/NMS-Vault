using NmsVault.Core.Adapters;
using NmsVault.Core.Derived;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// Tests for what a creature's payload says about it.
/// </summary>
/// <remarks>
/// Almost nothing in a pet object is named: traits and moods are bare arrays of numbers, and
/// which position means what is knowable only from an editor that reads them. These pin the
/// positions to NMSE's labels, because getting one wrong would show a creature as aggressive
/// when it is independent and nothing would look broken.
/// </remarks>
public class CompanionFactTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");

    private static VaultItem Diplodocus() => NmseImporter.Read(
        File.ReadAllBytes(Path.Combine(FixtureRoot, "companions", "[EXP-23-R] Diplodocus.nmspet")),
        new VaultMetadata { Id = "d", DisplayName = "Diplodocus" },
        ".nmspet");

    private static CompanionFacts Facts()
    {
        var item = Diplodocus();
        return CompanionFacts.For(item.Payload, item.AccessorySlots);
    }

    [Fact]
    public void TypeAndBiomeArePlainWordsInThePayload()
    {
        var facts = Facts();

        Assert.Equal("Passive", facts.CreatureType);
        Assert.Equal("Lush", facts.Biome);
        Assert.Equal("DIPLO_PET", facts.SpeciesId);
    }

    [Fact]
    public void TraitsAreReadInNmsesOrder()
    {
        // Traits: [0.5, -1.0, 0.5] on this creature, which is only meaningful once the three
        // positions have names. NMSE's CompanionPanel writes helpfulness, aggression and
        // independence to 0, 1 and 2 respectively.
        var traits = Facts().Traits;

        Assert.Equal(["Helpfulness", "Aggression", "Independence"], traits.Select(t => t.Label));
        Assert.Equal([0.5, -1.0, 0.5], traits.Select(t => t.Value));
    }

    [Fact]
    public void ACreatureHasNoNumbersWorthPuttingOnACard()
    {
        // A ship's stats rank it against other ships. A creature has nothing of that shape:
        // scale is a size rather than a score - 1.0 is small for a Diplodocus and enormous for
        // a beetle - trust and the moods drift with play, and each trait names one end of an
        // axis, so a column of signed values put the gentlest creature and the fiercest at
        // opposite ends of one scale. Scale is shown on the creature's own view against its
        // species range; the traits are resolved into the game's own words at ingest.
        Assert.Empty(Facts().AsStats());
    }

    [Fact]
    public void AccessoriesAreCountedFromTheSidecarRatherThanThePayload()
    {
        // The importer lifts PetAccessoryCustomisation out of the pet object, so reading the
        // payload alone counts none of them - which is what it did before this was threaded
        // through, and the modal quietly said a creature had no slots at all.
        var item = Diplodocus();

        Assert.NotNull(item.AccessorySlots);
        Assert.Null(item.Payload.GetArray("PetAccessoryCustomisation"));

        var withSidecar = CompanionFacts.For(item.Payload, item.AccessorySlots);
        var without = CompanionFacts.For(item.Payload);

        Assert.True(withSidecar.AccessorySlots > 0);
        Assert.Equal(0, without.AccessorySlots);

        // Nothing is equipped on this one: every slot is the default preset with no
        // customisation behind it.
        Assert.Equal(0, withSidecar.AccessoriesWorn);
    }

    [Fact]
    public void OnlyTheSeedsThatHoldSomethingAreRead()
    {
        // This creature is a unique model, the way Golden Vector is a unique ship, so its
        // CreatureSeed and BoneScaleSeed are [true, "0x1"] - the flag is set over a
        // placeholder. Reading the flag alone showed "0x1" twice under two labels and said
        // something false about the creature.
        var seeds = Facts().Seeds;

        Assert.Equal(["Species", "Genus"], seeds.Select(s => s.Label));
        Assert.All(seeds, s => Assert.StartsWith("0x", s.Value, StringComparison.Ordinal));
        Assert.All(seeds, s => Assert.True(s.Value.Length > 4, $"{s.Label} is a placeholder: {s.Value}"));
    }

    [Fact]
    public void AnOrdinaryCreatureKeepsItsRealSeeds()
    {
        // The other side of the rule. An ordinary procedural creature carries real values in
        // those same fields, and suppressing the field rather than the placeholder would have
        // thrown them away.
        var pet = Diplodocus().Payload.DeepClone();

        var creature = new JsonArray();
        creature.Add(true);
        creature.Add("0xAAC941FCE76A0E56");
        pet.Set("CreatureSeed", creature);

        var seeds = SeedReader.ForCompanion(pet);

        Assert.Contains(seeds, s => s.Label == "Creature" && s.Value == "0xAAC941FCE76A0E56");
    }

    [Fact]
    public void BoneScaleIsNotShownWhenItRepeatsTheCreatureSeed()
    {
        // Every ordinary creature checked carries the same value in both. Showing it twice is
        // showing one number under two names.
        var pet = Diplodocus().Payload.DeepClone();

        foreach (string key in (string[])["CreatureSeed", "BoneScaleSeed"])
        {
            var pair = new JsonArray();
            pair.Add(true);
            pair.Add("0x4BE6306289EA4D01");
            pet.Set(key, pair);
        }

        var seeds = SeedReader.ForCompanion(pet);

        Assert.Contains(seeds, s => s.Label == "Creature");
        Assert.DoesNotContain(seeds, s => s.Label == "Bone scale");
    }

    [Fact]
    public void AnOccupancyFlagWrittenAsANumberStillCounts()
    {
        // Requiring the boolean dropped a real seed from any file that wrote the flag as 1.
        var pet = Diplodocus().Payload.DeepClone();

        var pair = new JsonArray();
        pair.Add(1);
        pair.Add("0x25A7467C9C347919");
        pet.Set("ColourBaseSeed", pair);

        Assert.Contains(SeedReader.ForCompanion(pet), s => s.Label == "Colour");
    }

    [Fact]
    public void OnlyTheTwoNamingSeedsReachTheCard()
    {
        // A card has room for a line, not a table. The rest are worth reading once someone
        // has opened the item.
        var naming = SeedReader.NamingCompanion(Diplodocus().Payload);

        Assert.Equal(["Species", "Genus"], naming.Select(s => s.Label));
    }

    [Fact]
    public void BattleMovesComeThroughWithoutTheirCarets()
    {
        var moves = Facts().BattleMoves;

        Assert.Equal(["ATTACK_AFF", "BARRAGE_AFF", "DOT_BOMB", "SELF_HOT", "STUN"], moves);
        Assert.All(moves, m => Assert.DoesNotContain('^', m));
    }

    [Fact]
    public void ACreatureWithNothingInItReadsAsEmptyRatherThanThrowing()
    {
        // The gallery must survive a payload that is missing anything at all: a creature
        // exported by an older editor, or a kind the game adds later.
        var facts = CompanionFacts.For(JsonObject.Parse("""{ "CreatureID": "^X" }"""));

        Assert.Equal("Companion", facts.CreatureType);
        Assert.Null(facts.Biome);
        Assert.Empty(facts.Traits);
        Assert.Empty(facts.Seeds);
        Assert.Empty(facts.BattleMoves);
        Assert.Null(facts.SpeciesName);
    }
}
