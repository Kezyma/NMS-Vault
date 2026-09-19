using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>One of an item's inventories, ready to draw.</summary>
/// <param name="Key">Stable identifier, used to remember which tab is open.</param>
/// <param name="Label">The tab's heading, as the game names it.</param>
/// <param name="Grid">The laid-out cells.</param>
public readonly record struct NamedInventory(string Key, string Label, TechGrid Grid);

/// <summary>
/// The inventories an item carries, in the order the game shows its tabs.
/// </summary>
/// <remarks>
/// <para>
/// The names are the game's own, and the order is the game's own: technology first, because
/// that is what anyone looking at a ship in a gallery came to see, then the general
/// inventory, then cargo.
/// </para>
/// <para>
/// A multitool has one inventory and the game gives it no tab at all, so it is labelled
/// Technology to match the only heading a reader has seen for a grid of this shape.
/// </para>
/// </remarks>
public static class ItemInventories
{
    /// <summary>Every inventory an item has, skipping ones that are absent or empty.</summary>
    /// <param name="item">The item.</param>
    /// <returns>Its inventories, best first. Empty where the kind has none.</returns>
    public static IReadOnlyList<NamedInventory> For(VaultItem item)
    {
        var payload = item.Payload;

        var sources = item.Kind switch
        {
            EntityKind.Starship or EntityKind.Freighter =>
                (("tech", "Technology", payload.GetObject("Inventory_TechOnly")),
                 ("general", "General", payload.GetObject("Inventory")),
                 ("cargo", "Cargo", payload.GetObject("Inventory_Cargo"))),

            EntityKind.Multitool =>
                (("tech", "Technology", payload.GetObject("Store")),
                 ("", "", null),
                 ("", "", null)),

            // Companions and frigates carry no grid the game ever draws.
            _ => (("", "", null), ("", "", null), ("", "", null)),
        };

        var found = new List<NamedInventory>(3);
        Add(found, sources.Item1);
        Add(found, sources.Item2);
        Add(found, sources.Item3);
        return found;
    }

    private static void Add(List<NamedInventory> into, (string Key, string Label, JsonObject? Source) source)
    {
        if (source.Key.Length == 0 || source.Source is not { } inventory) return;

        // A declared size is what says the inventory is really there. Every inventory in the
        // fixture corpus has one; an object carrying zeroes is a placeholder the game left
        // behind, and TechGrids will happily derive a one-cell grid from it - which would be
        // a tab over a single empty square saying nothing.
        if (Size(inventory, "Width") <= 0 || Size(inventory, "Height") <= 0) return;

        if (TechGrids.Build(inventory) is not { } grid || grid.Slots.Count == 0) return;

        into.Add(new NamedInventory(source.Key, source.Label, grid));
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
