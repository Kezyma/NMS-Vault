using System.Text;
using NmsVault.Core.Derived;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// Tests for resolving an installed technology id to what it actually is.
/// <para>
/// Three separate conventions stand between the id in a save and the key in the database,
/// and all three appear in the fixture corpus, so each gets a test.
/// </para>
/// </summary>
public class TechIndexTests
{
    private static TechIndex Index => TechIndex.From(
    [
        new TechEntry("HYPERDRIVE", "Hyperdrive", "Lightspeed Warp Drive", "FTL propulsion.", "HYPERDRIVE.png", "Ship"),
        new TechEntry("UP_PULSE4", "Instability Drive", "Pulse Engine Upgrade", null, "UP_PULSE4.png", "AllShipsExceptAlien"),
        new TechEntry("BOBBLE_ATLAS", "Atlas Figurine", "Cockpit Adornment", null, "BOBBLE_ATLAS.png", null),
    ]);

    [Fact]
    public void PlainIdResolves() => Assert.Equal("Hyperdrive", Index.Find("HYPERDRIVE")?.Name);

    [Fact]
    public void LeadingCaretIsStripped() => Assert.Equal("Hyperdrive", Index.Find("^HYPERDRIVE")?.Name);

    [Fact]
    public void ProceduralVariantsShareAnEntry()
    {
        // ^UP_PULSE4#35271 and #41088 are two rolls of the same upgrade.
        Assert.Equal("Instability Drive", Index.Find("^UP_PULSE4#35271")?.Name);
        Assert.Equal("Instability Drive", Index.Find("^UP_PULSE4#41088")?.Name);
    }

    [Fact]
    public void CosmeticsResolveThroughTheTPrefixFallback()
    {
        // The save stores ^T_BOBBLE_ATLAS; the database key is BOBBLE_ATLAS. NMSE has the
        // same fallback, and without it every cockpit adornment shows as a raw id.
        Assert.Equal("Atlas Figurine", Index.Find("^T_BOBBLE_ATLAS")?.Name);
    }

    [Fact]
    public void TheFallbackDoesNotInventMatches()
    {
        // T_ is only tried after the literal id misses, and only for ids that carry it.
        Assert.Null(Index.Find("^T_NOT_A_REAL_THING"));
        Assert.Null(Index.Find("^NOTHING"));
        Assert.Null(Index.Find(""));
        Assert.Null(Index.Find(null));
    }

    [Theory]
    [InlineData("^UP_PULSE4#35271", "UP_PULSE4")]
    [InlineData("^HYPERDRIVE", "HYPERDRIVE")]
    [InlineData("HYPERDRIVE", "HYPERDRIVE")]
    public void NormaliseStripsCaretAndVariant(string input, string expected)
        => Assert.Equal(expected, TechIndex.Normalise(input));

    [Fact]
    public void EmptyIndexResolvesNothingRatherThanThrowing()
    {
        Assert.Equal(0, TechIndex.Empty.Count);
        Assert.Null(TechIndex.Empty.Find("^HYPERDRIVE"));
    }

    // --- Round trip through the on-disk shape --------------------------

    [Fact]
    public void IndexRoundTripsThroughTechJson()
    {
        var entries = new JsonArray();
        var entry = new JsonObject();
        entry.Set("Id", "HYPERDRIVE");
        entry.Set("Name", "Hyperdrive");
        entry.Set("Subtitle", "Lightspeed Warp Drive");
        entry.Set("Description", "FTL propulsion.");
        entry.Set("Icon", "HYPERDRIVE.webp");
        entry.Set("Category", "Ship");
        entries.Add(entry);

        var root = new JsonObject();
        root.Set("SchemaVersion", 1);
        root.Set("Technologies", entries);

        var loaded = TechIndex.FromBytes(Encoding.Latin1.GetBytes(root.ToExportString()));

        var found = loaded.Find("^HYPERDRIVE");
        Assert.NotNull(found);
        Assert.Equal("Hyperdrive", found!.Name);
        Assert.Equal("Lightspeed Warp Drive", found.Subtitle);
        Assert.Equal("HYPERDRIVE.webp", found.Icon);
    }

    [Fact]
    public void EntriesWithoutAnIdAreSkippedRatherThanThrowing()
    {
        var entries = new JsonArray();
        entries.Add(new JsonObject());
        var root = new JsonObject();
        root.Set("Technologies", entries);

        Assert.Equal(0, TechIndex.FromBytes(Encoding.Latin1.GetBytes(root.ToExportString())).Count);
    }

    // --- Against the real extracted data, when it is present ------------

    [Fact]
    public void ExtractedTechResolvesEveryIdInTheCorpus()
    {
        // Skips cleanly when extract-tech has not been run, so a fresh clone still passes.
        string techPath = Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..",
            "src", "NmsVault.Web", "wwwroot", "gallery", "tech.json");
        if (!File.Exists(techPath)) return;

        var index = TechIndex.FromBytes(File.ReadAllBytes(techPath));
        Assert.True(index.Count > 1000, $"expected a full extraction, got {index.Count}");

        string fixtures = Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");
        var unresolved = new List<string>();

        foreach (var path in Directory.EnumerateFiles(fixtures, "*", SearchOption.AllDirectories))
        {
            var root = JsonObject.FromBytes(File.ReadAllBytes(path));
            var payload = root.GetObject("Ship") ?? root;

            foreach (var key in (string[])["Inventory", "Inventory_TechOnly", "Store"])
            {
                var grid = TechGrids.Build(payload.GetObject(key));
                foreach (var id in grid?.InstalledBaseIds ?? [])
                    if (index.Find(id) is null) unresolved.Add(id);
            }
        }

        Assert.Empty(unresolved.Distinct());
    }
}
