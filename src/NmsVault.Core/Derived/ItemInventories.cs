using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>One of an item's inventories, ready to draw.</summary>
/// <param name="Key">Stable identifier.</param>
/// <param name="Label">Its heading, as the game names it.</param>
/// <param name="Grid">The laid-out cells.</param>
public readonly record struct NamedInventory(string Key, string Label, TechGrid Grid);

/// <summary>
/// The inventories an item carries.
/// </summary>
/// <remarks>
/// <para>
/// A starship's payload has three - <c>Inventory_TechOnly</c>, <c>Inventory</c> and
/// <c>Inventory_Cargo</c> - but only the first two are real: a starship has no cargo hold in
/// game, and the third is a leftover the save carries anyway.
/// </para>
/// <para>
/// Of the two that are real, only the technology grid is worth drawing. What is in a ship's
/// general inventory is whatever its last owner happened to be carrying, which says nothing
/// about the ship; how much room it has is worth knowing, and that is a number rather than a
/// picture - see <see cref="SlotCounts"/>, which is where it goes.
/// </para>
/// </remarks>
public static class ItemInventories
{
    /// <summary>The grid to draw for an item, or null where the kind has none.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Its technology grid.</returns>
    public static NamedInventory? Shown(VaultItem item)
    {
        var source = item.Kind switch
        {
            EntityKind.Starship or EntityKind.Freighter => item.Payload.GetObject("Inventory_TechOnly"),

            // A multitool's single inventory is all technology; the game gives it no tab, so
            // it takes the heading a reader has already seen over a grid of this shape.
            EntityKind.Multitool => item.Payload.GetObject("Store"),

            // Companions and frigates carry no grid the game ever draws.
            _ => null,
        };

        return Read(source) is { } grid ? new NamedInventory("tech", "Technology", grid) : null;
    }

    /// <summary>
    /// Builds a grid from an inventory object, or null when the object is not a real
    /// inventory.
    /// </summary>
    /// <remarks>
    /// A declared size is what says the inventory is really there. Every inventory in the
    /// fixture corpus has one; an object carrying zeroes is a placeholder, and
    /// <see cref="TechGrids"/> will happily derive a one-cell grid from it.
    /// </remarks>
    internal static TechGrid? Read(JsonObject? inventory)
    {
        if (inventory is null) return null;
        if (Size(inventory, "Width") <= 0 || Size(inventory, "Height") <= 0) return null;

        return TechGrids.Build(inventory) is { Slots.Count: > 0 } grid ? grid : null;
    }

    private static int Size(JsonObject inventory, string name) => inventory.Get(name) switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        RawDouble r => (int)r.Value,
        _ => 0,
    };
}

/// <summary>
/// How much room an item has, and how much of it is in use.
/// </summary>
/// <remarks>
/// These sit alongside the base stats on a card because that is what they are: numbers a
/// reader compares two ships by. A ship with forty-eight technology slots is a different
/// proposition from one with twenty, and neither the picture nor the grid says so at a glance.
/// <para>
/// Unlocked rather than total, because a cell nobody owns is not room - it is a cell the next
/// owner would have to buy.
/// </para>
/// </remarks>
public static class SlotCounts
{
    /// <summary>
    /// Ids for the derived stats.
    /// </summary>
    /// <remarks>
    /// A hash rather than the caret the game uses, so a derived stat can never be mistaken
    /// for one the save carries, nor collide with one added in a later game version.
    /// </remarks>
    public const string TechSlotsId = "#TECH_SLOTS";

    /// <inheritdoc cref="TechSlotsId"/>
    public const string StorageId = "#STORAGE";

    /// <inheritdoc cref="TechSlotsId"/>
    public const string TechInstalledId = "#TECH_INSTALLED";

    /// <summary>How the derived stats read on a card and in a column heading.</summary>
    public const string TechSlotsLabel = "Tech Slots";

    /// <inheritdoc cref="TechSlotsLabel"/>
    public const string StorageLabel = "Storage";

    /// <inheritdoc cref="TechSlotsLabel"/>
    public const string TechInstalledLabel = "Tech Installed";

    /// <summary>Cells the owner has, whether or not anything is in them.</summary>
    /// <param name="grid">The grid, or null for none.</param>
    /// <returns>How many cells are not locked.</returns>
    public static int Unlocked(TechGrid? grid)
        => grid?.Slots.Count(s => s.State != SlotState.Locked) ?? 0;

    /// <summary>Cells with something in them.</summary>
    /// <param name="grid">The grid, or null for none.</param>
    /// <returns>How many cells are filled.</returns>
    public static int Filled(TechGrid? grid)
        => grid?.Slots.Count(s => s.State == SlotState.Filled) ?? 0;

    /// <summary>The derived stats for a starship.</summary>
    /// <param name="ship">The ship payload.</param>
    /// <returns>Tech slots, storage and how much technology is installed.</returns>
    public static IReadOnlyList<ItemStat> ForShip(JsonObject ship)
    {
        var tech = ItemInventories.Read(ship.GetObject("Inventory_TechOnly"));
        var general = ItemInventories.Read(ship.GetObject("Inventory"));

        return
        [
            new ItemStat(TechSlotsId, TechSlotsLabel, Unlocked(tech)),
            new ItemStat(StorageId, StorageLabel, Unlocked(general)),
            new ItemStat(TechInstalledId, TechInstalledLabel, Filled(tech)),
        ];
    }

    /// <summary>The derived stats for a multitool.</summary>
    /// <param name="multitool">The multitool payload.</param>
    /// <returns>
    /// Tech slots and how much is installed. No storage: a multitool has one inventory and it
    /// is all technology, so a second number would be the same number.
    /// </returns>
    public static IReadOnlyList<ItemStat> ForMultitool(JsonObject multitool)
    {
        var store = ItemInventories.Read(multitool.GetObject("Store"));

        return
        [
            new ItemStat(TechSlotsId, TechSlotsLabel, Unlocked(store)),
            new ItemStat(TechInstalledId, TechInstalledLabel, Filled(store)),
        ];
    }
}
