using NmsVault.Core.Adapters;
using NmsVault.Core.Derived;

namespace NmsVault.Core.Tests;

/// <summary>
/// Tests for which inventories an item offers, and in what order.
/// </summary>
/// <remarks>
/// The grid itself is covered elsewhere; what is checked here is the selection - that a ship
/// leads with its technology, that a multitool has exactly one, and that a kind with no grid
/// at all offers nothing rather than an empty tab.
/// </remarks>
public class ItemInventoryTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");

    private static VaultMetadata Meta => new() { Id = "t", DisplayName = "Test" };

    private static VaultItem Ship(string name) => NmseImporter.Read(
        File.ReadAllBytes(Path.Combine(FixtureRoot, "starships", name)), Meta, ".nmsship");

    private static VaultItem Tool(string name) => NmseImporter.Read(
        File.ReadAllBytes(Path.Combine(FixtureRoot, "multitools", name)), Meta, ".nmstool");

    [Fact]
    public void AShipLeadsWithItsTechnology()
    {
        // Technology first because that is what anyone looking at a ship in a gallery came to
        // see; the general inventory and the cargo hold follow, as the game orders its tabs.
        var found = ItemInventories.For(Ship("[EXP-13-R] Iron Vulture.nmsship"));

        Assert.Equal(["tech", "general", "cargo"], found.Select(i => i.Key));
        Assert.Equal(["Technology", "General", "Cargo"], found.Select(i => i.Label));
    }

    [Fact]
    public void AShipsTechnologyGridHoldsWhatTheIndexCounted()
    {
        // The same number the card shows, arrived at two different ways: the card counts
        // distinct base ids from the facts, this counts filled cells in the grid. They are
        // allowed to differ only when a ship has two of the same technology installed.
        var item = Ship("[EXP-13-R] Iron Vulture.nmsship");

        var tech = ItemInventories.For(item).Single(i => i.Key == "tech");
        int filled = tech.Grid.Slots.Count(s => s.State == SlotState.Filled);

        Assert.Equal(ItemFacts.For(item).InstalledTech.Count, tech.Grid.InstalledBaseIds.Count);
        Assert.True(filled >= tech.Grid.InstalledBaseIds.Count);
    }

    [Fact]
    public void AMultitoolHasOneInventoryCalledTechnology()
    {
        // The game gives a multitool's single grid no tab at all, so it takes the only
        // heading a reader has already seen over a grid of this shape.
        var found = ItemInventories.For(Tool("[EXP-12-R] Atlas Sceptre.nmstool"));

        Assert.Single(found);
        Assert.Equal("Technology", found[0].Label);
    }

    [Fact]
    public void EveryCellOfEveryGridIsAccountedFor()
    {
        // Slots is sparse and unordered in the save; the grid must be neither. Width x Height
        // cells, each one reachable at its own position.
        foreach (var item in new[] { Ship("[EXP-13-R] Iron Vulture.nmsship"),
                                     Ship("[PRE-PS] Alpha Vector (Original).nmsship"),
                                     Tool("[EXP-12-R] Atlas Sceptre.nmstool") })
        {
            foreach (var inventory in ItemInventories.For(item))
            {
                var grid = inventory.Grid;
                Assert.Equal(grid.Width * grid.Height, grid.Slots.Count);

                for (int y = 0; y < grid.Height; y++)
                    for (int x = 0; x < grid.Width; x++)
                    {
                        var cell = grid.At(x, y);
                        Assert.NotNull(cell);
                        Assert.Equal((x, y), (cell!.Value.X, cell.Value.Y));
                    }
            }
        }
    }

    [Fact]
    public void NothingFallsOutsideAGridInTheRealCorpus()
    {
        // A slot whose Index is past the declared size is invisible in NMSE. The grid records
        // them so they can be said out loud; this asserts the corpus has none, so anything
        // the gallery ever reports is a real problem rather than a routine one.
        foreach (string name in Directory.GetFiles(Path.Combine(FixtureRoot, "starships")))
        {
            var item = NmseImporter.Read(File.ReadAllBytes(name), Meta, ".nmsship");

            foreach (var inventory in ItemInventories.For(item))
                Assert.Empty(inventory.Grid.OutOfBounds);
        }
    }

    [Fact]
    public void ACompanionOffersNoGrid()
    {
        var pet = VaultItem.Create(
            EntityKind.Companion,
            Json.JsonObject.Parse("""{ "CreatureID": "x" }"""),
            Meta);

        Assert.Empty(ItemInventories.For(pet));
    }

    [Fact]
    public void AnInventoryWithNoCellsIsNotOffered()
    {
        // An inventory can exist in the payload and have nothing to draw. A tab reading
        // "Cargo 0" over an empty rectangle is worse than no tab.
        var ship = VaultItem.Create(
            EntityKind.Starship,
            Json.JsonObject.Parse("""
            {
                "Resource": { "Filename": "FIGHTER_PROC.SCENE.MBIN" },
                "Inventory_TechOnly": { "Width": 2, "Height": 2, "Slots": [], "ValidSlotIndices": [] },
                "Inventory_Cargo": { "Width": 0, "Height": 0, "Slots": [], "ValidSlotIndices": [] }
            }
            """),
            Meta);

        Assert.Equal(["tech"], ItemInventories.For(ship).Select(i => i.Key));
    }
}
