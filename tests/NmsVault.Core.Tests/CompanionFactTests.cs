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
    public void MoodsAreReadInNmsesOrder()
    {
        var moods = Facts().Moods;

        Assert.Equal(["Hungry", "Lonely"], moods.Select(m => m.Label));
        Assert.All(moods, m => Assert.InRange(m.Value, 0, 1));
    }

    [Fact]
    public void TheCardTakesWhatWasSettledAtHatchingAndLeavesTheRest()
    {
        // Size and the three traits are the creature. Trust and the moods drift with play, so
        // a gallery ordered by them would rank creatures by how their last owner left them.
        var stats = Facts().AsStats();

        Assert.Equal(["Scale", "Helpfulness", "Aggression", "Independence"], stats.Select(s => s.Label));
        Assert.All(stats, s => Assert.StartsWith("#", s.Id, StringComparison.Ordinal));
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
    public void EverySeedTheCreatureCarriesIsRead()
    {
        // Two plain hex strings that name the species, plus the paired seeds that describe how
        // it looks - and only the pairs whose flag says they hold something.
        var seeds = Facts().Seeds;

        Assert.Equal(["Species", "Genus", "Creature", "Bone scale"], seeds.Select(s => s.Label));
        Assert.All(seeds, s => Assert.StartsWith("0x", s.Value, StringComparison.Ordinal));

        // ColourBaseSeed is [false, "0x0"] here, so it is absent rather than shown as zero.
        Assert.DoesNotContain(seeds, s => s.Label == "Colour");
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
        Assert.Empty(facts.Moods);
        Assert.Empty(facts.Seeds);
        Assert.Empty(facts.BattleMoves);
        Assert.Equal(0, facts.Scale);
    }
}
