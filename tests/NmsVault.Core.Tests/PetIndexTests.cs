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
}
