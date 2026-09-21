using NmsVault.Core;
using NmsVault.Web.Services;

namespace NmsVault.Web.Tests;

/// <summary>
/// Tests for the narrowing rule the filter bar rests on.
/// <para>
/// The interesting case is the one that reads identically in the UI but behaves differently:
/// ticking two values under one heading versus one value under each of two headings.
/// </para>
/// </summary>
public class FilteringTests
{
    private static GalleryRow Row(
        string name, string type, string? cls = null,
        string[]? tags = null, string[]? tech = null, double damage = 0)
        => new()
        {
            Id = name.ToLowerInvariant().Replace(' ', '-'),
            Kind = EntityKind.Starship,
            Page = "Shipyard",
            DisplayName = name,
            Type = type,
            Class = cls,
            Tags = tags ?? [],
            Tech = tech ?? [],
            Stats = [new("Damage", damage)],
        };

    private static readonly IReadOnlyList<GalleryRow> Rows =
    [
        Row("Iron Vulture", "Hauler", "S", ["dropship"], ["^HYPERDRIVE", "^LAUNCHER"], 77),
        Row("Golden Vector", "Golden Vector", "S", ["gold"], ["^HYPERDRIVE"], 60),
        Row("Rasamama S36", "Fighter", "C", ["starter"], ["^LAUNCHER"], 14),
        Row("Alpha Vector", "Fighter", "C", [], [], 9),
    ];

    private static Sifter<GalleryRow> NewSifter()
        => new(GalleryFacets.For("Shipyard"), r => $"{r.DisplayName} {r.Type}");

    private static IReadOnlyList<GalleryRow> Narrow(Sifter<GalleryRow> sifter)
        => [.. Rows.Where(sifter.Matches)];

    // --- The matching rule --------------------------------------------

    [Fact]
    public void NothingTickedShowsEverything()
    {
        var sifter = NewSifter();

        Assert.False(sifter.Any);
        Assert.Equal(4, Narrow(sifter).Count);
    }

    [Fact]
    public void TwoValuesUnderOneHeadingMatchEither()
    {
        // Ticking Fighter and Hauler means "either of those", not "both at once", which
        // nothing could satisfy.
        var sifter = NewSifter();
        sifter.Ticked("type").Add("Fighter");
        sifter.Ticked("type").Add("Hauler");

        Assert.Equal(3, Narrow(sifter).Count);
    }

    [Fact]
    public void ValuesUnderDifferentHeadingsMustAllMatch()
    {
        // Fighter and class S: the two narrow each other rather than widening the result.
        var sifter = NewSifter();
        sifter.Ticked("type").Add("Fighter");
        sifter.Ticked("class").Add("S");

        Assert.Empty(Narrow(sifter));

        sifter.Ticked("class").Clear();
        sifter.Ticked("class").Add("C");
        Assert.Equal(2, Narrow(sifter).Count);
    }

    [Fact]
    public void AMultiValuedHeadingMatchesOnAnyOfARowsValues()
    {
        // A ship holds several technologies at once; ticking one finds every ship with it.
        var sifter = NewSifter();
        sifter.Ticked("tech").Add("^HYPERDRIVE");

        Assert.Equal(["Golden Vector", "Iron Vulture"],
            Narrow(sifter).Select(r => r.DisplayName).Order());
    }

    // --- Search -------------------------------------------------------

    [Fact]
    public void SearchMatchesAnywhereInTheText()
    {
        // Not a prefix: someone looking for "vulture" should find "Iron Vulture" without
        // having to know what it is called first.
        var sifter = NewSifter();
        sifter.Search = "vulture";

        Assert.Equal(["Iron Vulture"], Narrow(sifter).Select(r => r.DisplayName));
    }

    [Fact]
    public void SearchIsCaseInsensitiveAndAppliesAlongsideTicks()
    {
        var sifter = NewSifter();
        sifter.Search = "VECTOR";
        Assert.Equal(2, Narrow(sifter).Count);

        sifter.Ticked("class").Add("S");
        Assert.Equal(["Golden Vector"], Narrow(sifter).Select(r => r.DisplayName));
    }

    [Fact]
    public void ClearRemovesSearchAndTicksTogether()
    {
        var sifter = NewSifter();
        sifter.Search = "vector";
        sifter.Ticked("class").Add("S");
        Assert.True(sifter.Any);

        sifter.Clear();

        Assert.False(sifter.Any);
        Assert.Equal(4, Narrow(sifter).Count);
    }

    // --- What a heading offers ----------------------------------------

    [Fact]
    public void AHeadingOffersOnlyValuesSomethingActuallyHas()
    {
        // A filter for a type nothing in the gallery is would be noise, so it is not offered.
        var offered = GalleryFacets.For("Shipyard")
            .First(f => f.Key == "type")
            .Offer(Rows);

        Assert.Equal(["Fighter", "Golden Vector", "Hauler"],
            offered.Select(o => o.Value.Key).Order());
    }

    [Fact]
    public void OfferedValuesCarryTheirCountsMostCommonFirst()
    {
        var offered = GalleryFacets.For("Shipyard")
            .First(f => f.Key == "type")
            .Offer(Rows);

        Assert.Equal("Fighter", offered[0].Value.Key);
        Assert.Equal(2, offered[0].Count);
    }

    [Fact]
    public void RowsWithNoValueUnderAHeadingAreSimplyNotCounted()
    {
        // Alpha Vector has no tags; it should not appear as a blank option.
        var offered = GalleryFacets.For("Shipyard")
            .First(f => f.Key == "tags")
            .Offer(Rows);

        Assert.DoesNotContain(offered, o => o.Value.Key.Length == 0);
        Assert.Equal(3, offered.Count);
    }

    // --- Per-page differences -----------------------------------------

    [Fact]
    public void CompanionsGetNoClassOrTechHeadings()
    {
        // They have neither, so those controls would be empty rather than useful.
        var keys = GalleryFacets.For("Stable").Select(f => f.Key).ToList();

        Assert.Contains("type", keys);
        Assert.Contains("tags", keys);
        Assert.DoesNotContain("class", keys);
        Assert.DoesNotContain("tech", keys);
    }

    [Fact]
    public void EachPageNamesItsTypeHeadingForWhatItHolds()
    {
        Assert.Equal("Ship type", Label("Shipyard"));
        Assert.Equal("Tool type", Label("Armoury"));
        Assert.Equal("Creature type", Label("Stable"));

        static string Label(string page)
            => GalleryFacets.For(page).First(f => f.Key == "type").Label;
    }

    // --- Sorting ------------------------------------------------------

    [Fact]
    public void SortsOfferEveryColumnWorthOrderingBy()
    {
        // One list serves the dropdown over the cards and the headings on the table, so it
        // covers the columns as well as the stats - including the slot counts the gallery
        // derives, which a reader sorts by exactly as they sort by damage.
        Assert.Equal(
            ["Name", "Type", "Class", "Damage", "Shield", "Hyperdrive", "Manoeuvrability",
             "Tech Slots", "Storage", "Tech Installed"],
            GalleryFacets.SortsFor("Shipyard").Select(s => s.Label));

        // No storage for a multi-tool: it has one inventory and it is all technology.
        Assert.Equal(
            ["Name", "Type", "Class", "Damage", "Mining", "Scan", "Tech Slots", "Tech Installed"],
            GalleryFacets.SortsFor("Armoury").Select(s => s.Label));

        // Companions have no class, no technology and no numbers at all. Type is the species -
        // Prehistoric Giant - and affinity groups them the way a class ranks a ship. Scale is a
        // size rather than a score, trust and the moods drift with play, and each of the three
        // traits names one end of an axis, so there is no order to sort any of them into that a
        // reader would be looking for.
        Assert.Equal(
            ["Name", "Type", "Affinity"],
            GalleryFacets.SortsFor("Stable").Select(s => s.Label));
    }

    [Fact]
    public void ACreaturesNatureReadsAsLabelledFieldsInAFixedOrder()
    {
        // The item view lists these and the table shows the same ones as columns, so the order
        // lives in one place. Egg-modified is deliberately not among them: it is false on every
        // creature and every egg to hand, so the row only ever said no.
        var nature = new GalleryNature("Airless", "Common", "Walking", "Standard",
            IsPredator: true, HasFur: false, NoBattle: false);

        Assert.Equal(
            ["Native climate", "Rarity", "Movement", "Predator", "Fur", "Egg"],
            nature.Fields.Select(f => f.Key));

        Assert.Equal(
            ["Airless", "Common", "Walking", "Yes", "No", "Standard"],
            nature.Fields.Select(f => f.Value));
    }

    [Fact]
    public void AFieldACreatureHasNothingForIsLeftOutRatherThanLeftBlank()
    {
        // A species the tables do not know keeps its yes-or-no answers and drops the rest, so
        // the table draws no column for a field nothing on the page carries.
        var unknown = new GalleryNature(null, null, null, null,
            IsPredator: false, HasFur: true, NoBattle: false);

        Assert.Equal(["Predator", "Fur"], unknown.Fields.Select(f => f.Key));
    }

    [Fact]
    public void TheArenaFieldAppearsOnlyWhenTheAnswerIsNo()
    {
        // Ten of twelve can fight. A column of Yes with two No in it says less than one that is
        // there only where the answer is interesting.
        var canFight = new GalleryNature("Verdant", null, null, null, false, false, NoBattle: false);
        var cannot = new GalleryNature("Scorched", null, null, null, false, false, NoBattle: true);

        Assert.DoesNotContain("Pet battles", canFight.Fields.Select(f => f.Key));
        Assert.Equal("No", cannot.Fields.Single(f => f.Key == "Pet battles").Value);
    }

    [Fact]
    public void StatsSortHighestFirst()
    {
        var damage = GalleryFacets.SortsFor("Shipyard").First(s => s.Label == "Damage");

        Assert.Equal(["Iron Vulture", "Golden Vector", "Rasamama S36", "Alpha Vector"],
            GalleryFacets.Sort(Rows, damage).Select(r => r.DisplayName));
    }

    [Fact]
    public void NameBreaksTiesSoEqualStatsStayInAReadableOrder()
    {
        var tied = new[] { Row("Zephyr", "Fighter", damage: 5), Row("Apex", "Fighter", damage: 5) };
        var damage = GalleryFacets.SortsFor("Shipyard").First(s => s.Label == "Damage");

        Assert.Equal(["Apex", "Zephyr"], GalleryFacets.Sort(tied, damage).Select(r => r.DisplayName));
    }

    [Fact]
    public void SortingByNameIsAlphabeticalRegardlessOfStats()
    {
        var byName = GalleryFacets.SortsFor("Shipyard")[0];

        Assert.Equal(["Alpha Vector", "Golden Vector", "Iron Vulture", "Rasamama S36"],
            GalleryFacets.Sort(Rows, byName).Select(r => r.DisplayName));
    }
}
