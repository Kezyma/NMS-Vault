using NmsVault.Core.Adapters;
using NmsVault.Core.Derived;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// Tests for the grid an item shows, and for the slot counts derived beside it.
/// </summary>
public class ItemInventoryTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");

    private static VaultMetadata Meta => new() { Id = "t", DisplayName = "Test" };

    private static VaultItem Ship(string name) => NmseImporter.Read(
        File.ReadAllBytes(Path.Combine(FixtureRoot, "starships", name)), Meta, ".nmsship");

    private static VaultItem Tool(string name) => NmseImporter.Read(
        File.ReadAllBytes(Path.Combine(FixtureRoot, "multitools", name)), Meta, ".nmstool");

    private const string Vulture = "[EXP-13-R] Iron Vulture.nmsship";
    private const string Sceptre = "[EXP-12-R] Atlas Sceptre.nmstool";

    [Fact]
    public void AShipShowsItsTechnologyAndNothingElse()
    {
        // The general inventory holds whatever its last owner was carrying, which says nothing
        // about the ship; the cargo hold is not a thing a starship has at all. How much room
        // the general inventory has is worth knowing, and that is a number - see the storage
        // stat below - rather than a picture.
        var shown = ItemInventories.Shown(Ship(Vulture));

        Assert.NotNull(shown);
        Assert.Equal("tech", shown!.Value.Key);
        Assert.Equal("Technology", shown.Value.Label);
    }

    [Fact]
    public void AMultitoolsOneGridIsCalledTechnology()
    {
        var shown = ItemInventories.Shown(Tool(Sceptre));

        Assert.NotNull(shown);
        Assert.Equal("Technology", shown!.Value.Label);
    }

    [Fact]
    public void ACompanionShowsNoGrid()
    {
        var pet = VaultItem.Create(EntityKind.Companion, JsonObject.Parse("""{ "CreatureID": "x" }"""), Meta);

        Assert.Null(ItemInventories.Shown(pet));
    }

    [Fact]
    public void AnInventoryWithNoDeclaredSizeIsNotAnInventory()
    {
        // An object carrying zeroes is a placeholder, and TechGrids will happily derive a
        // one-cell grid from it - which would draw a tab over a single empty square.
        var ship = VaultItem.Create(
            EntityKind.Starship,
            JsonObject.Parse("""
            {
                "Resource": { "Filename": "FIGHTER_PROC.SCENE.MBIN" },
                "Inventory_TechOnly": { "Width": 0, "Height": 0, "Slots": [], "ValidSlotIndices": [] }
            }
            """),
            Meta);

        Assert.Null(ItemInventories.Shown(ship));
    }

    [Fact]
    public void EveryCellOfTheGridIsAccountedFor()
    {
        // Slots is sparse and unordered in the save; the grid must be neither. Width x Height
        // cells, each one reachable at its own position.
        foreach (var item in new[] { Ship(Vulture), Ship("[PRE-PS] Alpha Vector (Original).nmsship"), Tool(Sceptre) })
        {
            var grid = ItemInventories.Shown(item)!.Value.Grid;

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

    [Fact]
    public void NothingFallsOutsideAGridInTheRealCorpus()
    {
        // A slot whose Index is past the declared size is invisible in NMSE. The grid records
        // them so they can be said out loud; this asserts the corpus has none, so anything the
        // gallery ever reports is a real problem rather than a routine one.
        foreach (string name in Directory.GetFiles(Path.Combine(FixtureRoot, "starships")))
        {
            var item = NmseImporter.Read(File.ReadAllBytes(name), Meta, ".nmsship");
            Assert.Empty(ItemInventories.Shown(item)!.Value.Grid.OutOfBounds);
        }
    }

    // --- the derived counts -------------------------------------------

    [Fact]
    public void AShipCountsItsTechnologySlotsItsStorageAndWhatIsInstalled()
    {
        var stats = SlotCounts.ForShip(Ship(Vulture).Payload);

        Assert.Equal(
            [SlotCounts.TechSlotsLabel, SlotCounts.StorageLabel, SlotCounts.TechInstalledLabel],
            stats.Select(s => s.Label));

        Assert.All(stats, s => Assert.True(s.Value > 0));
    }

    [Fact]
    public void SlotsAreCountedUnlockedRatherThanTotal()
    {
        // A cell nobody owns is not room - it is a cell the next owner would have to buy.
        var grid = ItemInventories.Shown(Ship(Vulture))!.Value.Grid;

        int locked = grid.Slots.Count(s => s.State == SlotState.Locked);
        Assert.True(locked > 0, "the fixture should have locked cells for this to mean anything");

        Assert.Equal(grid.Slots.Count - locked, SlotCounts.Unlocked(grid));
    }

    [Fact]
    public void WhatIsInstalledNeverExceedsTheSlotsToPutItIn()
    {
        foreach (string name in Directory.GetFiles(Path.Combine(FixtureRoot, "starships")))
        {
            var stats = SlotCounts.ForShip(NmseImporter.Read(File.ReadAllBytes(name), Meta, ".nmsship").Payload);

            double slots = stats.Single(s => s.Id == SlotCounts.TechSlotsId).Value;
            double installed = stats.Single(s => s.Id == SlotCounts.TechInstalledId).Value;

            Assert.True(installed <= slots, $"{Path.GetFileName(name)}: {installed} in {slots} slots");
        }
    }

    [Fact]
    public void AMultitoolHasNoSeparateStorage()
    {
        // One inventory, and it is all technology, so a storage number would be the same
        // number said twice.
        var stats = SlotCounts.ForMultitool(Tool(Sceptre).Payload);

        Assert.Equal([SlotCounts.TechSlotsLabel, SlotCounts.TechInstalledLabel], stats.Select(s => s.Label));
    }

    [Fact]
    public void TheDerivedStatsJoinTheGamesOwnOnTheCard()
    {
        // One list, because a reader comparing two ships does not care which numbers the save
        // carried and which were counted.
        var stats = ItemFacts.For(Ship(Vulture)).Stats;

        Assert.Equal(
            ["Damage", "Shield", "Hyperdrive", "Manoeuvrability",
             SlotCounts.TechSlotsLabel, SlotCounts.StorageLabel, SlotCounts.TechInstalledLabel],
            stats.Select(s => s.Label));
    }

    [Fact]
    public void ADerivedStatIdCannotBeMistakenForAGameOne()
    {
        // The game writes a caret; these write a hash. Asserted rather than left to a comment,
        // because the ids reach index.json and an accidental caret would look like a stat the
        // save carries.
        foreach (string id in (string[])[SlotCounts.TechSlotsId, SlotCounts.StorageId, SlotCounts.TechInstalledId])
            Assert.StartsWith("#", id, StringComparison.Ordinal);

        Assert.All(ItemStats.ShipStats, s => Assert.StartsWith("^", s.Id, StringComparison.Ordinal));
    }
}
