using NmsVault.Core.Adapters;
using NmsVault.Core.Derived;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// Tests for the display attributes the gallery filters, sorts and renders on.
/// Every case runs against the real fixture corpus rather than synthetic input.
/// </summary>
public class DerivedTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");

    private static VaultMetadata Meta => new() { Id = "t", DisplayName = "Test" };

    private static VaultItem Ship(string name) => NmseImporter.Read(
        File.ReadAllBytes(Path.Combine(FixtureRoot, "starships", name)), Meta, ".nmsship");

    private static VaultItem Tool(string name) => NmseImporter.Read(
        File.ReadAllBytes(Path.Combine(FixtureRoot, "multitools", name)), Meta, ".nmstool");

    // --- Ship type ----------------------------------------------------

    [Theory]
    [InlineData("[EXP-13-R] Iron Vulture.nmsship", "Hauler", ShipFamily.Ship)]
    [InlineData("[EXP-09-R] Utopia Speeder.nmsship", "Utopia Speeder", ShipFamily.Ship)]
    [InlineData("[EXP-01-R] Golden Vector.nmsship", "Golden Vector", ShipFamily.Ship)]
    [InlineData("[DLC] Starborn Phoenix.nmsship", "Starborn Phoenix", ShipFamily.Ship)]
    [InlineData("[EXP-12-R] Starborn Runner.nmsship", "Starborn Runner", ShipFamily.Ship)]
    [InlineData("[EXP-16-R] Boundary Herald.nmsship", "Boundary Herald", ShipFamily.Ship)]
    [InlineData("[EXP-23-R] Golden Rasamama S36.nmsship", "Golden Rasamama S36", ShipFamily.Ship)]
    [InlineData("[PRE-SW] Horizon Vector NX.nmsship", "Horizon Vector NX (Switch)", ShipFamily.Ship)]
    [InlineData("[START] Rasamama S36.nmsship", "Fighter", ShipFamily.Ship)]
    [InlineData("[PRE-PC] Horizon Omega (New).nmsship", "Fighter", ShipFamily.Ship)]
    // The two that are not ordinary ships - their family decides which tech fits.
    [InlineData("[EXP-17-R] The Wraith.nmsship", "The Wraith", ShipFamily.AlienShip)]
    [InlineData("[EXP-23-R] Vintage Interceptor.nmsship", "Vintage Interceptor", ShipFamily.RobotShip)]
    public void ShipType_IsResolvedFromTheModelPath(string fixture, string expectedType, ShipFamily expectedFamily)
    {
        var result = ShipTypes.FromShip(Ship(fixture).Payload);

        Assert.Equal(expectedType, result.Type);
        Assert.Equal(expectedFamily, result.Family);
        Assert.False(result.IsModified);
    }

    [Fact]
    public void EveryShipFixtureResolvesToAKnownType()
    {
        foreach (var path in Directory.EnumerateFiles(Path.Combine(FixtureRoot, "starships")))
        {
            var result = ShipTypes.FromShip(
                NmseImporter.Read(File.ReadAllBytes(path), Meta, ".nmsship").Payload);

            Assert.NotEqual("Unknown", result.Type);
            Assert.False(result.IsModified, $"{Path.GetFileName(path)} resolved only by keyword");
        }
    }

    [Fact]
    public void TableOrderDecidesTheKeywordFallback()
    {
        // A path that is not an exact match but contains FIGHTERCLASSICGOLD also contains
        // FIGHTER, which is tested first. NMSE resolves this to Fighter, and so do we - the
        // behaviour depends on table order, which is why the table is an array.
        var result = ShipTypes.FromFilename("MODELS/MODDED/FIGHTERCLASSICGOLD_CUSTOM.SCENE.MBIN");

        Assert.Equal("Fighter", result.Type);
        Assert.True(result.IsModified);
        Assert.Equal("Fighter (Modified)", result.Display);
    }

    [Fact]
    public void WracerKeywordDoesNotSwallowWracerse()
    {
        // WRACER.SCENE carries the .SCENE precisely so it cannot match WRACERSE.SCENE.MBIN.
        Assert.Equal("Starborn Runner",
            ShipTypes.FromFilename("MODELS/COMMON/SPACECRAFT/FIGHTERS/WRACER.SCENE.MBIN").Type);
        Assert.Equal("Starborn Phoenix",
            ShipTypes.FromFilename("MODELS/COMMON/SPACECRAFT/FIGHTERS/WRACERSE.SCENE.MBIN").Type);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("MODELS/SOMETHING/ENTIRELY/UNRELATED.SCENE.MBIN")]
    public void UnrecognisedPathsAreUnknown(string? filename)
    {
        Assert.Equal("Unknown", ShipTypes.FromFilename(filename).Type);
    }

    [Fact]
    public void CorvettesAreRecognised()
    {
        Assert.True(ShipTypes.IsCorvette("MODELS/COMMON/SPACECRAFT/BIGGS/BIGGS.SCENE.MBIN"));
        Assert.Equal(ShipFamily.Corvette,
            ShipTypes.FromFilename("MODELS/COMMON/SPACECRAFT/BIGGS/BIGGS.SCENE.MBIN").Family);
        Assert.False(ShipTypes.IsCorvette("MODELS/COMMON/SPACECRAFT/FIGHTERS/FIGHTER_PROC.SCENE.MBIN"));
    }

    // --- Stats --------------------------------------------------------

    [Fact]
    public void ShipStats_AreReadInOrder()
    {
        var stats = ItemStats.ForShip(Ship("[EXP-13-R] Iron Vulture.nmsship").Payload);

        Assert.Equal(4, stats.Count);
        Assert.Equal(["Damage", "Shield", "Hyperdrive", "Manoeuvrability"], stats.Select(s => s.Label));
        Assert.Equal(77.06755828857422, stats[0].Value, 4);
        Assert.Equal(115.912841796875, stats[1].Value, 4);
    }

    [Fact]
    public void MultitoolStats_AreRead()
    {
        var stats = ItemStats.ForMultitool(Tool("[EXP-12-R] Atlas Sceptre.nmstool").Payload);

        Assert.Equal(["Damage", "Mining", "Scan"], stats.Select(s => s.Label));
        Assert.True(stats[0].Value > 0);
    }

    [Fact]
    public void MissingStatsReadAsZeroRatherThanThrowing()
    {
        var empty = JsonObject.Parse("""{ "Inventory": {} }""");

        Assert.Equal(0.0, ItemStats.Read(empty.GetObject("Inventory"), "^SHIP_DAMAGE"));
        Assert.Equal(0.0, ItemStats.Read(null, "^SHIP_DAMAGE"));
    }

    [Theory]
    [InlineData("[EXP-17-R] The Wraith.nmsship", "^ALIEN_SHIP")]
    [InlineData("[EXP-23-R] Vintage Interceptor.nmsship", "^ROBOT_SHIP")]
    public void FamilyMarkersHideInTheStatsArray(string fixture, string expected)
    {
        // These sit alongside real stats but describe what the ship is. They corroborate the
        // model path independently, and must not be shown or sorted on as stats.
        var inventory = Ship(fixture).Payload.GetObject("Inventory");

        Assert.Equal(expected, ItemStats.FamilyMarker(inventory));
        Assert.DoesNotContain(ItemStats.ForShip(Ship(fixture).Payload), s => s.Id == expected);
    }

    [Fact]
    public void OrdinaryShipsCarryNoFamilyMarker()
    {
        Assert.Null(ItemStats.FamilyMarker(Ship("[START] Rasamama S36.nmsship").Payload.GetObject("Inventory")));
    }

    // --- Tech grid ----------------------------------------------------

    [Fact]
    public void TechGrid_MatchesTheDeclaredDimensions()
    {
        var grid = TechGrids.Build(Ship("[EXP-13-R] Iron Vulture.nmsship").Payload.GetObject("Inventory_TechOnly"));

        Assert.NotNull(grid);
        Assert.Equal(10, grid!.Width);
        Assert.Equal(3, grid.Height);
        Assert.Equal(30, grid.Slots.Count);
        Assert.Empty(grid.OutOfBounds);
    }

    [Fact]
    public void TechGrid_PlacesItemsByIndexNotArrayOrder()
    {
        var grid = TechGrids.Build(Ship("[EXP-13-R] Iron Vulture.nmsship").Payload.GetObject("Inventory_TechOnly"))!;

        // The first array entry declares Index (0,0); assert it landed there rather than
        // being placed by its position in the array.
        Assert.Equal("^UP_PULSE4#35271", grid.At(0, 0)!.Value.ItemId);
        Assert.Equal(SlotState.Filled, grid.At(0, 0)!.Value.State);

        Assert.Equal(20, grid.Slots.Count(s => s.State == SlotState.Filled));
    }

    [Fact]
    public void TechGrid_StripsProceduralVariantSuffixes()
    {
        var grid = TechGrids.Build(Ship("[EXP-13-R] Iron Vulture.nmsship").Payload.GetObject("Inventory_TechOnly"))!;

        Assert.Equal("^UP_PULSE4", grid.At(0, 0)!.Value.BaseItemId);
        Assert.DoesNotContain(grid.InstalledBaseIds, id => id.Contains('#'));
    }

    [Theory]
    [InlineData("^UP_PULSE4#35271", "^UP_PULSE4")]
    [InlineData("^SHIPJUMP1", "^SHIPJUMP1")]
    [InlineData("^T_BOBBLE_ATLAS", "^T_BOBBLE_ATLAS")]
    [InlineData("^ODD#notdigits", "^ODD#notdigits")]
    [InlineData("^TRAILING#", "^TRAILING#")]
    public void VariantStripping_OnlyRemovesNumericSuffixes(string input, string expected)
    {
        Assert.Equal(expected, TechSlot.StripVariant(input));
    }

    [Fact]
    public void TechGrid_MarksSuperchargedSlotsIncludingEmptyOnes()
    {
        // Supercharged lives in the sibling SpecialSlots array, not on the slot, so it
        // applies to empty cells too.
        var grid = TechGrids.Build(Ship("[DLC] Starborn Phoenix.nmsship").Payload.GetObject("Inventory_TechOnly"))!;

        var supercharged = grid.Slots.Where(s => s.IsSupercharged).ToList();
        Assert.NotEmpty(supercharged);
        Assert.All(supercharged, s => Assert.True(s.Y <= 3, "supercharged slots live in rows 0-3"));
    }

    [Fact]
    public void TechGrid_ClassifiesLockedAndEmptyCells()
    {
        var grid = TechGrids.Build(Ship("[START] Rasamama S36.nmsship").Payload.GetObject("Inventory_TechOnly"))!;

        // A starter ship has few unlocked slots, so all three states should be present.
        Assert.Contains(grid.Slots, s => s.State == SlotState.Filled);
        Assert.Contains(grid.Slots, s => s.State == SlotState.Locked);
        Assert.Equal(grid.Width * grid.Height, grid.Slots.Count);
    }

    [Fact]
    public void EmptyValidSlotIndicesMeansEverythingIsUnlocked()
    {
        // Not "nothing is unlocked" - this matches NMSE, and exported cargo inventories
        // routinely carry no ValidSlotIndices at all.
        var inventory = JsonObject.Parse("""
            { "Width": 2, "Height": 2, "Slots": [], "ValidSlotIndices": [] }
            """);

        var grid = TechGrids.Build(inventory)!;

        Assert.All(grid.Slots, s => Assert.Equal(SlotState.Empty, s.State));
    }

    [Fact]
    public void OutOfBoundsItemsAreSurfacedRatherThanSilentlyDropped()
    {
        // NMSE drops these invisibly. Reporting them means a malformed item can be spotted.
        var inventory = JsonObject.Parse("""
            {
              "Width": 2, "Height": 2,
              "ValidSlotIndices": [],
              "Slots": [
                { "Id": "^INSIDE",  "Index": { "X": 0, "Y": 0 } },
                { "Id": "^OUTSIDE", "Index": { "X": 9, "Y": 9 } }
              ]
            }
            """);

        var grid = TechGrids.Build(inventory)!;

        Assert.Equal(["^OUTSIDE"], grid.OutOfBounds);
        Assert.Equal("^INSIDE", grid.At(0, 0)!.Value.ItemId);
    }

    [Fact]
    public void SentinelIdsCountAsEmptyNotFilled()
    {
        var inventory = JsonObject.Parse("""
            {
              "Width": 3, "Height": 1, "ValidSlotIndices": [],
              "Slots": [
                { "Id": "^",              "Index": { "X": 0, "Y": 0 } },
                { "Id": "^YOURSLOTITEM",  "Index": { "X": 1, "Y": 0 } },
                { "Id": "^REAL",          "Index": { "X": 2, "Y": 0 } }
              ]
            }
            """);

        var grid = TechGrids.Build(inventory)!;

        Assert.Equal(SlotState.Empty, grid.At(0, 0)!.Value.State);
        Assert.Equal(SlotState.Empty, grid.At(1, 0)!.Value.State);
        Assert.Equal(SlotState.Filled, grid.At(2, 0)!.Value.State);
    }

    [Fact]
    public void MissingDimensionsAreDerivedFromTheFurthestOccupiedCell()
    {
        var inventory = JsonObject.Parse("""
            {
              "ValidSlotIndices": [ { "X": 3, "Y": 1 } ],
              "Slots": [ { "Id": "^X", "Index": { "X": 1, "Y": 0 } } ]
            }
            """);

        var grid = TechGrids.Build(inventory)!;

        Assert.Equal(4, grid.Width);
        Assert.Equal(2, grid.Height);
    }

    [Fact]
    public void BuildReturnsNullForNoInventory() => Assert.Null(TechGrids.Build(null));

    [Fact]
    public void InstalledBaseIds_AreDistinctAndSorted()
    {
        var grid = TechGrids.Build(Ship("[DLC] Starborn Phoenix.nmsship").Payload.GetObject("Inventory_TechOnly"))!;
        var ids = grid.InstalledBaseIds;

        Assert.Equal(ids.Distinct(StringComparer.Ordinal).Count(), ids.Count);
        Assert.Equal(ids.Order(StringComparer.Ordinal), ids);
    }
}
