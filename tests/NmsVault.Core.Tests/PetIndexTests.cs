using NmsVault.Core.Derived;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// Affinity resolution and move naming, against the gallery's own companion data.
/// </summary>
/// <remarks>
/// The corpus holds one creature, and it resolves through the biome path. The cases below cover
/// the species-override path too, using creatures from the author's older exports - the point
/// being that an override has to beat the biome, which is only visible on a creature whose two
/// answers differ.
/// </remarks>
public class PetIndexTests
{
    private static PetIndex Index()
    {
        string path = Path.Combine(AppContext.BaseDirectory, "gallery", "pets.json");

        // Published beside the gallery rather than embedded, the same as tech.json. When it is
        // missing the tests below are the thing that says so.
        Assert.True(File.Exists(path), $"No pets.json at {path}. Run nmsvault-ingest extract-pets.");

        return PetIndex.FromBytes(File.ReadAllBytes(path));
    }

    [Theory]
    // The override wins, whatever world the creature came from.
    [InlineData("^FIEND", "Dead", "TOXIC", "Toxic")]
    [InlineData("^QUAD_PET", "Scorched", "MECHANICAL", "Mech")]
    [InlineData("^FLYINGSNAKE", "Scorched", "DESERT", "Barren")]
    // The case that proves the ordering: forced Lush on a radioactive world.
    [InlineData("^BONECAT", "Radioactive", "TROPICAL", "Lush")]
    // No override, so the biome decides.
    [InlineData("^TRICERATOPS", "Barren", "DESERT", "Barren")]
    [InlineData("^BLOB", "Lush", "TROPICAL", "Lush")]
    [InlineData("^TREX", "Lava", "FIRE", "Fire")]
    [InlineData("^FLYINGBEETLE", "Swamp", "TOXIC", "Toxic")]
    [InlineData("^DIPLO_PET", "Lush", "TROPICAL", "Lush")]
    public void AffinityIsTheSpeciesOverrideAndThenTheBiome(
        string creatureId, string biome, string expectedName, string expectedId)
    {
        var affinity = Index().Affinity(creatureId, biome);

        Assert.NotNull(affinity);
        Assert.Equal(expectedId, affinity!.Id);
        Assert.Equal(expectedName, affinity.Name);
    }

    [Fact]
    public void EveryAffinityACreatureCanHaveHasAGlyph()
    {
        // Except Normal, which means "none" and which no creature reaches: a species whose
        // forced affinity is Normal falls back to its biome, and every biome maps elsewhere.
        var index = Index();

        foreach (string biome in (string[])["Lush", "Toxic", "Scorched", "Radioactive", "Frozen",
                                            "Barren", "Dead", "Weird", "Swamp", "Lava"])
        {
            var affinity = index.Affinity(null, biome);

            Assert.NotNull(affinity);
            Assert.False(string.IsNullOrEmpty(affinity!.Icon), $"{biome} resolves to {affinity.Id}, which has no glyph");
        }
    }

    [Theory]
    [InlineData("^ATTACK_AFF", "Lush", "Lash")]
    [InlineData("^ATTACK_AFF", "Cold", "Freeze")]
    [InlineData("^BARRAGE_AFF", "Lush", "Thunderstorm")]
    [InlineData("^DOT_BOMB", "Lush", "Solar Ray")]
    [InlineData("^SELF_HOT", "Lush", "Regrowth")]
    [InlineData("^STUN", "Lush", "Snaring Roots")]
    [InlineData("^STUN", "Cold", "Icy Chains")]
    public void AMoveIsNamedForTheCreaturesAffinity(string moveId, string affinityId, string expected)
    {
        var index = Index();
        var affinity = index.Affinity(null, Biome(affinityId));

        Assert.Equal(expected, index.Move(moveId, affinity).Name);
    }

    /// <summary>A biome that resolves to the affinity a case wants, so the test can state one.</summary>
    private static string Biome(string affinityId) => affinityId switch
    {
        "Lush" => "Lush",
        "Cold" => "Frozen",
        _ => throw new ArgumentOutOfRangeException(nameof(affinityId)),
    };

    [Fact]
    public void AMoveCarriesItsIconAndTheGamesOwnSummary()
    {
        var index = Index();
        var move = index.Move("^STUN", index.Affinity(null, "Lush"));

        Assert.Equal("cooldown.webp", move.Icon);
        Assert.Equal("the opposing creature", move.Target);
        Assert.False(string.IsNullOrWhiteSpace(move.Description));
    }

    [Fact]
    public void AnUnknownMoveComesBackUnderItsOwnName()
    {
        // Never null. An id the tables do not know shows as the id, which is what every move
        // showed before any of this existed - worse, but not broken.
        var move = Index().Move("^NOT_A_REAL_MOVE", null);

        Assert.Equal("NOT_A_REAL_MOVE", move.Name);
        Assert.Null(move.Icon);
    }

    [Fact]
    public void AnEmptyIndexResolvesNothingRatherThanThrowing()
    {
        // What the site does when pets.json has not been published: raw ids, no affinity, no
        // exception.
        Assert.Null(PetIndex.Empty.Affinity("^DIPLO_PET", "Lush"));
        Assert.Equal("STUN", PetIndex.Empty.Move("^STUN", null).Name);
    }

    [Fact]
    public void TheDiplodocusInTheGalleryResolvesEndToEnd()
    {
        // The one creature actually published, read from its own payload rather than from
        // literals, so a change to the importer shows up here too.
        string pet = Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse", "companions",
            "[EXP-23-R] Diplodocus.nmspet");

        var facts = CompanionFacts.For(JsonObject.FromBytes(File.ReadAllBytes(pet)));
        var index = Index();
        var affinity = index.Affinity(facts.SpeciesId, facts.Biome);

        Assert.Equal("TROPICAL", affinity?.Name);
        Assert.Equal(
            ["Lash", "Thunderstorm", "Solar Ray", "Regrowth", "Snaring Roots"],
            facts.BattleMoves.Select(m => index.Move(m, affinity).Name));
    }

    [Theory]
    [InlineData("Lush", "TROPICAL")]
    [InlineData("Frozen", "FROST")]
    [InlineData("Scorched", "FIRE")]
    [InlineData("Toxic", "TOXIC")]
    [InlineData("Barren", "DESERT")]
    [InlineData("Radioactive", "RADIOACTIVE")]
    [InlineData("Weird", "ANOMALOUS")]
    public void EveryAffinityKnowsWhatBeatsItAndWhatItBeats(string biome, string expected)
    {
        var index = Index();
        var affinity = index.Affinity(null, biome);

        Assert.Equal(expected, affinity?.Name);

        var matchup = index.Matchup(affinity);

        Assert.NotNull(matchup);
        Assert.Equal(2, matchup!.Weak.Count);
        Assert.Equal(2, matchup.Strong.Count);

        // Nothing beats itself, and nothing is on both of its own lists.
        Assert.DoesNotContain(affinity!.Id, matchup.Weak.Select(a => a.Id));
        Assert.DoesNotContain(affinity.Id, matchup.Strong.Select(a => a.Id));
    }

    [Fact]
    public void TheMatchupTableAgreesWithItself()
    {
        // Every pairing is stated twice - once as A's weakness, once as B's strength - so the
        // two halves have to line up. This is the test that catches a typo in a hand-written
        // table, which is what this one is.
        var index = Index();

        var affinities = (string[])["Lush", "Cold", "Fire", "Toxic", "Barren", "Radioactive", "Weird", "Mech"];
        var matchups = affinities.ToDictionary(id => id, id => index.Matchup(Affinity(index, id)));

        foreach (string id in affinities)
        {
            Assert.NotNull(matchups[id]);

            foreach (var beaten in matchups[id]!.Strong)
                Assert.Contains(id, matchups[beaten.Id]!.Weak.Select(a => a.Id));

            foreach (var beats in matchups[id]!.Weak)
                Assert.Contains(id, matchups[beats.Id]!.Strong.Select(a => a.Id));
        }
    }

    [Fact]
    public void TropicalIsTheOneTheGalleryActuallyShows()
    {
        // The only creature published, so this is the matchup a reader will see.
        var index = Index();
        var matchup = index.Matchup(index.Affinity("^DIPLO_PET", "Lush"));

        Assert.Equal(["TOXIC", "MECHANICAL"], matchup!.Weak.Select(a => a.Name));
        Assert.Equal(["DESERT", "ANOMALOUS"], matchup.Strong.Select(a => a.Name));
    }

    [Theory]
    // Typed in the id, so the glyph is the same whatever knows the move.
    [InlineData("^ATTACK_COLD", "Cold")]
    [InlineData("^ATTACK_HOT", "Fire")]
    [InlineData("^ATTACK_DUST", "Barren")]
    // Typed to the creature.
    [InlineData("^ATTACK_AFF", "Lush")]
    [InlineData("^STUN", "Lush")]
    // The trap: SELF_HOT is a heal over time, not a fire move, so reading the suffix is wrong.
    [InlineData("^SELF_HOT", "Lush")]
    public void AMoveIsTypedByItsIdOrByTheCreature(string moveId, string expected)
    {
        var index = Index();
        var move = index.Move(moveId, index.Affinity(null, "Lush"));

        Assert.Equal(expected, move.Affinity?.Id);
    }

    [Fact]
    public void AnUntypedMoveDrawsNoAffinityGlyph()
    {
        var index = Index();

        Assert.Null(index.Move("^ATTACK_NORM", index.Affinity(null, "Lush")).Affinity);
        Assert.Null(index.Move("^ATTACK_DOT_NORM", index.Affinity(null, "Lush")).Affinity);
    }

    [Theory]
    [InlineData("^SELF_SHIELD", "defence.webp")]
    [InlineData("^BUFF_DODGE", "stealth.webp")]
    [InlineData("^BUFF_ACCURACY", "accuracy.webp")]
    [InlineData("^SELF_HEAL", "health.webp")]
    [InlineData("^SELF_HOT", "health.webp")]
    [InlineData("^SELF_RESET_CD", "cooldown.webp")]
    [InlineData("^BUFF_SPEED", "speed.webp")]
    [InlineData("^BUFF_DAMAGE", "power.webp")]
    [InlineData("^ATTACK_AFF", "attack.webp")]
    public void AMoveIsDrawnWithTheStatItTouches(string moveId, string expected)
    {
        // Not IconStyle, which says "Attack" for 55 of the 61 moves including both heals.
        Assert.Equal(expected, Index().Move(moveId, null).Icon);
    }

    [Theory]
    [InlineData("Lush", "Verdant")]
    [InlineData("Dead", "Airless")]
    [InlineData("Scorched", "Scorched")]
    [InlineData("Lava", "Volcanic")]
    [InlineData("Weird", "Unusual")]
    [InlineData("Waterworld", "Water-bound")]
    [InlineData("GasGiant", "Gaseous")]
    [InlineData("Barren", "Barren")]
    public void AClimateReadsAsTheGameWritesIt(string biome, string expected)
        => Assert.Equal(expected, Index().Climate(biome));

    [Fact]
    public void EveryBiomeACreatureCanCarryHasAClimate()
    {
        var index = Index();

        foreach (string biome in (string[])["Lush", "Toxic", "Scorched", "Radioactive", "Frozen",
                                            "Barren", "Dead", "Weird", "Swamp", "Lava",
                                            "Waterworld", "GasGiant"])
        {
            // Never the raw id, which is what the gallery printed before this existed. Two of
            // them do happen to match - Toxic and Swamp are their own names - so the check is
            // that the table answered, not that the answer differs.
            Assert.NotNull(index.Climate(biome));
        }
    }

    [Fact]
    public void AnUnknownBiomeFallsBackToItsOwnName()
        => Assert.Equal("Somewhere", Index().Climate("Somewhere"));

    [Theory]
    // The three the payload stores, at the sign and magnitude the Diplodocus carries.
    [InlineData(0.5, -1.0, 0.5, "Helpfulness", "Gentleness", "Independence")]
    // The other end of all three.
    [InlineData(-0.5, 1.0, -0.5, "Playfulness", "Aggression", "Devotion")]
    public void TheSignPicksWhichEndOfTheAxisACreatureIsOn(
        double first, double second, double third, string a, string b, string c)
    {
        var traits = Index().Traits("^DIPLO_PET", [first, second, third]);

        Assert.Equal([a, b, c], traits.Select(t => t.Name));
    }

    [Theory]
    // Read off screenshots of the game's own companion register. These are not what the tables
    // predict - they are what the game printed - so a change to the pairing, the percentage or
    // the class bands that breaks any of them is a change away from the game.
    [InlineData("^DIPLO_PET", 0.5, -1.0, 0.5,
        "Helpfulness 50% (Cooperative)|Gentleness 100% (Sweet Tempered)|Independence 50% (Free Spirited)")]
    [InlineData("^QUAD_PET", 0.1784112006, 0.7080084085, 0.7908408046,
        "Helpfulness 18% (Compliant)|Aggression 71% (Fierce)|Independence 79% (Autonomous)")]
    // The one that proved the pairing: a negative first slot reads as Playfulness, not as a
    // lack of Helpfulness.
    [InlineData("^FLYINGSNAKE", -0.8269261122, 0.3639779985, 0.2314400077,
        "Playfulness 83% (Whimsical)|Aggression 36% (Territorial)|Independence 23% (Aloof)")]
    [InlineData("^BONECOW", -0.75, -0.3000000119, 0.8500000238,
        "Playfulness 75% (Frolicsome)|Gentleness 30% (Tolerant)|Independence 85% (Adventurous)")]
    // The two that fixed the top band: 80 is A and 82 is S.
    [InlineData("^ROBO_RODENT", 0.8000000119, -0.5, 0.2000000030,
        "Helpfulness 80% (Diligent)|Gentleness 50% (Patient)|Independence 20% (Aloof)")]
    [InlineData("^ROBO_PET", 1.0, 0.22, 0.82,
        "Helpfulness 100% (Dutiful)|Aggression 22% (Passionate)|Independence 82% (Adventurous)")]
    public void ACreatureReadsAsTheGameReadsIt(
        string creatureId, double first, double second, double third, string expected)
    {
        var traits = Index().Traits(creatureId, [first, second, third]);

        Assert.Equal(expected, string.Join("|", traits.Select(t => $"{t.Name} {t.Percent}% ({t.Word})")));
    }

    [Theory]
    // Not even quarters, which is what this assumed before the game was consulted. The observed
    // boundaries fall in (30, 36], (50, 71] and (80, 82]; these are the edges of the bands
    // fitted inside them.
    [InlineData(0.0, "C")]
    [InlineData(0.30, "C")]
    [InlineData(0.33, "C")]
    [InlineData(0.34, "B")]
    [InlineData(0.50, "B")]
    [InlineData(0.66, "B")]
    [InlineData(0.67, "A")]
    [InlineData(0.80, "A")]
    [InlineData(0.81, "S")]
    [InlineData(1.0, "S")]
    // The magnitude is what counts, so the bands are symmetrical about zero.
    [InlineData(-1.0, "S")]
    [InlineData(-0.1, "C")]
    public void TheClassIsFittedToWhatTheGamePrints(double value, string expected)
    {
        var trait = Index().Traits("^DIPLO_PET", [value]).Single();

        Assert.Equal(expected, trait.Class);
        Assert.Equal((int)Math.Round(Math.Abs(value) * 100), trait.Percent);
    }

    [Fact]
    public void EveryPoleHasAWordAtEveryClass()
    {
        // Six poles, four classes, two vocabularies - and a missing one would show as a bare
        // percentage with an empty bracket after it.
        var index = Index();

        foreach (string species in (string[])["^DIPLO_PET", "^FIEND"])
            foreach (double value in (double[])[0.9, 0.6, 0.3, 0.1, -0.9, -0.6, -0.3, -0.1])
                foreach (var trait in index.Traits(species, [value, value, value]))
                    Assert.False(string.IsNullOrWhiteSpace(trait.Word),
                        $"{species} has no word for {trait.Name} at class {trait.Class}");
    }

    [Fact]
    public void AFiendReadsInItsOwnWords()
    {
        // The game gives fiends a separate vocabulary for the same six poles, and the species
        // id is what picks it.
        var index = Index();

        Assert.Equal("Sweet Tempered", index.Traits("^DIPLO_PET", [0, -1.0, 0])[1].Word);
        Assert.Equal("Docile", index.Traits("^FIEND", [0, -1.0, 0])[1].Word);
    }

    [Fact]
    public void ASpeciesCarriesItsHabits()
    {
        // No scale. The table's MinScale and MaxScale describe wild spawns rather than
        // companions - three of the twelve creatures to hand fall outside their own species'
        // range - so they are deliberately not published.
        var species = Index().Species("^DIPLO_PET");

        Assert.NotNull(species);
        Assert.Equal("Ground", species!.MoveArea);
        Assert.Equal("Uncommon", species.Rarity);
        Assert.Equal("DEFAULT", species.EggType);
        Assert.True(species.CanBattle);
    }

    [Fact]
    public void AMachineHatchesFromAMachinesEgg()
        => Assert.Equal("ROBO", Index().Species("^QUAD_PET")?.EggType);

    [Theory]
    // What a creature's type actually is, in the way a ship's is Fighter.
    [InlineData("^UI_DIPLO_PET_SPECIES", "Prehistoric Giant")]
    [InlineData("^UI_FIEND_NAME", "Burrowing Monstrosity")]
    [InlineData("^UI_PETWORM_SPECIES", "Maggotling")]
    // The loc id does not follow from the creature id - SCUTTLER_PET takes the fiend's name
    // and BUGFIEND takes a marker tag - so only the payload knows which to ask for.
    [InlineData("^UI_MINIFIEND_SPECIES", "Scuttling Horror")]
    [InlineData("^UI_MARKER_TAG_BUGFIEND", "Vile Broodling")]
    public void ASpeciesReadsAsTheGameNamesIt(string locId, string expected)
        => Assert.Equal(expected, Index().SpeciesName(locId));

    [Fact]
    public void ACreatureWithNoSpeciesNameGetsNoneRatherThanAWrongOne()
    {
        // One of the twelve to hand stores a bare caret. The caller falls back to CreatureType,
        // which is worse but true.
        var index = Index();

        Assert.Null(index.SpeciesName("^"));
        Assert.Null(index.SpeciesName(null));
        Assert.Null(index.SpeciesName("^UI_NOT_A_REAL_KEY"));
    }

    [Fact]
    public void AnEmptyIndexNamesNoTraitsRatherThanGuessing()
    {
        // The caller falls back to the raw numbers, which is better than three wrong words.
        Assert.Empty(PetIndex.Empty.Traits("^DIPLO_PET", [0.5, -1.0, 0.5]));
        Assert.Null(PetIndex.Empty.Matchup(null));
        Assert.Null(PetIndex.Empty.Species("^DIPLO_PET"));
        Assert.Equal("Lush", PetIndex.Empty.Climate("Lush"));
    }

    /// <summary>An affinity by id, via a biome that resolves to it.</summary>
    private static PetAffinity? Affinity(PetIndex index, string id) => id switch
    {
        "Lush" => index.Affinity(null, "Lush"),
        "Cold" => index.Affinity(null, "Frozen"),
        "Fire" => index.Affinity(null, "Scorched"),
        "Toxic" => index.Affinity(null, "Toxic"),
        "Barren" => index.Affinity(null, "Barren"),
        "Radioactive" => index.Affinity(null, "Radioactive"),
        "Weird" => index.Affinity(null, "Weird"),
        "Mech" => index.Affinity("^QUAD_PET", "Lush"),
        _ => throw new ArgumentOutOfRangeException(nameof(id)),
    };
}
