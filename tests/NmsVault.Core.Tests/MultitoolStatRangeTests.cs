using NmsVault.Core.Derived;

namespace NmsVault.Core.Tests;

/// <summary>
/// Tests for the stat range table that identifies a shared-model multitool.
/// </summary>
/// <remarks>
/// The table is the only thing separating pistols, rifles, experimental and alien tools, so
/// what matters is not just that individual lookups work but that the table is <em>capable</em>
/// of separating them: the shared-model boxes have to be disjoint within a class, or a lookup
/// has no single answer. That invariant is asserted here rather than assumed.
/// </remarks>
public class MultitoolStatRangeTests
{
    private static MultitoolStatRange Row(string type, string cls)
        => MultitoolStatRanges.All.Single(r => r.Type == type && r.Class == cls);

    /// <summary>The middle of a range, which must resolve to the type that owns it.</summary>
    private static (double D, double M, double S) Middle(MultitoolStatRange r)
        => ((r.DamageMin + r.DamageMax) / 2, (r.MiningMin + r.MiningMax) / 2, (r.ScanMin + r.ScanMax) / 2);

    [Fact]
    public void EveryTypeHasAllFourClasses()
    {
        foreach (var group in MultitoolStatRanges.All.GroupBy(r => r.Type))
            Assert.Equal(["A", "B", "C", "S"], group.Select(r => r.Class).Order(StringComparer.Ordinal));
    }

    [Fact]
    public void SharedModelRangesDoNotOverlapWithinAClass()
    {
        // Boxes overlap only when they overlap on all three axes. Any overlapping pair would
        // make Match ambiguous, so this is what lets the table be used at all.
        var shared = MultitoolStatRanges.All
            .Where(r => MultitoolStatRanges.SharedModelTypes.Contains(r.Type)).ToArray();

        foreach (var cls in (string[])["C", "B", "A", "S"])
        {
            var rows = shared.Where(r => r.Class == cls).ToArray();

            for (int i = 0; i < rows.Length; i++)
                for (int j = i + 1; j < rows.Length; j++)
                {
                    bool overlaps =
                        rows[i].DamageMin <= rows[j].DamageMax && rows[j].DamageMin <= rows[i].DamageMax &&
                        rows[i].MiningMin <= rows[j].MiningMax && rows[j].MiningMin <= rows[i].MiningMax &&
                        rows[i].ScanMin <= rows[j].ScanMax && rows[j].ScanMin <= rows[i].ScanMax;

                    Assert.False(overlaps, $"{rows[i].Type} and {rows[j].Type} overlap at class {cls}");
                }
        }
    }

    [Theory]
    [InlineData("Pistol")]
    [InlineData("Rifle")]
    [InlineData("Experimental")]
    [InlineData("Alien")]
    public void TheMiddleOfEachRangeResolvesToItsOwnType(string type)
    {
        foreach (var cls in (string[])["C", "B", "A", "S"])
        {
            var (d, m, s) = Middle(Row(type, cls));
            Assert.Equal(type, MultitoolStatRanges.Match(cls, d, m, s));
        }
    }

    [Theory]
    [InlineData("Pistol")]
    [InlineData("Rifle")]
    [InlineData("Experimental")]
    [InlineData("Alien")]
    public void BothEndsOfEachRangeAreInclusive(string type)
    {
        foreach (var cls in (string[])["C", "B", "A", "S"])
        {
            var r = Row(type, cls);

            // Inclusive in both directions, because the game's own tables are written as
            // "5 to 10" and a tool that rolled exactly 10 is not a different type.
            Assert.True(r.Contains(r.DamageMin, r.MiningMin, r.ScanMin));
            Assert.True(r.Contains(r.DamageMax, r.MiningMax, r.ScanMax));
        }
    }

    [Fact]
    public void StatsOutsideEveryRangeResolveToNothing()
    {
        // Above every S-class bound, so no row can claim it.
        Assert.Null(MultitoolStatRanges.Match("S", 999, 999, 999));

        // A real shape that belongs to no shared type: pistol mining with rifle damage.
        Assert.Null(MultitoolStatRanges.Match("C", 3, 7, 3));
    }

    [Fact]
    public void AnUnknownOrAbsentClassResolvesToNothing()
    {
        Assert.Null(MultitoolStatRanges.Match(null, 0, 0, 0));
        Assert.Null(MultitoolStatRanges.Match("X", 0, 0, 0));
    }

    [Fact]
    public void ClassIsMatchedWithoutRegardToCase()
    {
        var (d, m, s) = Middle(Row("Alien", "S"));
        Assert.Equal("Alien", MultitoolStatRanges.Match("s", d, m, s));
    }

    [Fact]
    public void AllZeroStatsAreARifleAndNothingElse()
    {
        // The one case where the table gives an answer that reads oddly. A tool that has
        // rolled nothing sits inside Rifle C exactly and outside every other C range - a
        // C pistol needs mining of at least 5 - so an unrolled tool is a rifle by these
        // ranges. Asserted so the behaviour is a decision rather than a surprise.
        Assert.Equal("Rifle", MultitoolStatRanges.Match("C", 0, 0, 0));

        foreach (var cls in (string[])["B", "A", "S"])
            Assert.Null(MultitoolStatRanges.Match(cls, 0, 0, 0));
    }

    [Fact]
    public void ExperimentalAndRoyalAreIndistinguishableByStats()
    {
        // Documented in the table's remarks, and true only because Royal has its own model
        // file. If these ever diverge the remark is wrong and should be rewritten.
        foreach (var cls in (string[])["C", "B", "A", "S"])
            Assert.Equal(Row("Experimental", cls) with { Type = "Royal" }, Row("Royal", cls));
    }

    [Fact]
    public void SentinelAndStaffDifferOnlyInSClassScanning()
    {
        foreach (var cls in (string[])["C", "B", "A"])
            Assert.Equal(Row("Sentinel", cls) with { Type = "Staff" }, Row("Staff", cls));

        Assert.Equal(50, Row("Sentinel", "S").ScanMax);
        Assert.Equal(55, Row("Staff", "S").ScanMax);
        Assert.Equal(Row("Sentinel", "S") with { Type = "Staff", ScanMax = 55 }, Row("Staff", "S"));
    }

    [Fact]
    public void OnlySharedModelTypesAreEverMatched()
    {
        // Royal, Sentinel, Staff and Atlantid are in the table for completeness but are
        // identified by their model file. Matching them here would make Experimental and
        // Royal ambiguous and collapse both to nothing.
        var (d, m, s) = Middle(Row("Atlantid", "S"));
        Assert.NotEqual("Atlantid", MultitoolStatRanges.Match("S", d, m, s));
    }
}
