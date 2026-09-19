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

        // Companions have no class and no technology, but they do have numbers of their own -
        // the size and the three traits settled when they hatched. Trust and the moods are
        // deliberately absent: those drift with play, so ordering by them would rank
        // creatures by how their last owner left them.
        Assert.Equal(
            ["Name", "Type", "Scale", "Helpfulness", "Aggression", "Independence"],
            GalleryFacets.SortsFor("Stable").Select(s => s.Label));
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
