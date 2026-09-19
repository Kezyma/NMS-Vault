using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>What a cell in an inventory grid is.</summary>
public enum SlotState
{
    /// <summary>Not owned. Rendered as a locked cell.</summary>
    Locked,

    /// <summary>Owned but nothing installed.</summary>
    Empty,

    /// <summary>Has an item in it.</summary>
    Filled,
}

/// <summary>One cell of an inventory grid.</summary>
/// <param name="X">Column.</param>
/// <param name="Y">Row.</param>
/// <param name="State">Locked, empty or filled.</param>
/// <param name="ItemId">
/// The installed item's id including any procedural suffix, e.g. <c>^UP_PULSE4#35271</c>.
/// Null unless <see cref="State"/> is <see cref="SlotState.Filled"/>.
/// </param>
/// <param name="IsSupercharged">
/// Whether this cell is a supercharged (TechBonus) slot. Applies to empty cells too.
/// </param>
/// <param name="IsDamaged">Whether this cell is blocked by broken technology.</param>
public readonly record struct TechSlot(
    int X,
    int Y,
    SlotState State,
    string? ItemId,
    bool IsSupercharged,
    bool IsDamaged)
{
    /// <summary>
    /// The item id with any procedural suffix stripped, for looking up a name and icon.
    /// <c>^UP_PULSE4#35271</c> becomes <c>^UP_PULSE4</c>.
    /// </summary>
    public string? BaseItemId => ItemId is null ? null : StripVariant(ItemId);

    /// <summary>Removes a trailing <c>#NNNNN</c> procedural variant suffix.</summary>
    public static string StripVariant(string id)
    {
        int hash = id.LastIndexOf('#');
        if (hash <= 0 || hash == id.Length - 1) return id;

        for (int i = hash + 1; i < id.Length; i++)
            if (!char.IsAsciiDigit(id[i])) return id;

        return id[..hash];
    }
}

/// <summary>An inventory laid out as the grid the game draws.</summary>
/// <param name="Width">Columns.</param>
/// <param name="Height">Rows.</param>
/// <param name="Slots">Every cell, row-major.</param>
/// <param name="OutOfBounds">
/// Items whose stored position falls outside <paramref name="Width"/> × <paramref name="Height"/>.
/// NMSE drops these silently; they are surfaced here so a malformed item can be spotted
/// rather than quietly losing technology in the display.
/// </param>
public sealed record TechGrid(
    int Width,
    int Height,
    IReadOnlyList<TechSlot> Slots,
    IReadOnlyList<string> OutOfBounds)
{
    /// <summary>The cell at a position, or null if outside the grid.</summary>
    public TechSlot? At(int x, int y)
        => x < 0 || y < 0 || x >= Width || y >= Height ? null : Slots[(y * Width) + x];

    /// <summary>Distinct base item ids installed, for icon preloading and tech filters.</summary>
    public IReadOnlyList<string> InstalledBaseIds =>
        [.. Slots.Where(s => s.State == SlotState.Filled)
                 .Select(s => s.BaseItemId!)
                 .Distinct(StringComparer.Ordinal)
                 .Order(StringComparer.Ordinal)];
}

/// <summary>
/// Builds a <see cref="TechGrid"/> from an inventory object.
/// </summary>
/// <remarks>
/// Everything needed is in the payload; no database is involved. The only thing the grid
/// cannot supply is what each installed id <em>means</em> - that needs the tech lookup.
/// </remarks>
public static class TechGrids
{
    /// <summary>Ids the game uses for a slot that exists but holds nothing.</summary>
    private static readonly HashSet<string> EmptySentinels =
        new(StringComparer.Ordinal) { "", "^", "^YOURSLOTITEM" };

    /// <summary>Builds the grid for an inventory, or null when there is no inventory.</summary>
    public static TechGrid? Build(JsonObject? inventory)
    {
        if (inventory is null) return null;

        var slotsArray = inventory.GetArray("Slots");
        var validArray = inventory.GetArray("ValidSlotIndices");
        var specialArray = inventory.GetArray("SpecialSlots");

        var filled = new Dictionary<(int X, int Y), string>();
        var outOfBoundsCandidates = new List<((int X, int Y) Pos, string Id)>();

        for (int i = 0; i < (slotsArray?.Length ?? 0); i++)
        {
            var slot = slotsArray!.GetObject(i);
            if (slot is null) continue;

            string? id = ReadItemId(slot);
            if (id is null || EmptySentinels.Contains(id)) continue;

            var pos = ReadIndex(slot.GetObject("Index"));
            if (pos is null) continue;

            // Slots is sparse and unordered - position comes from Index, never array order.
            filled[pos.Value] = id;
            outOfBoundsCandidates.Add((pos.Value, id));
        }

        var valid = ReadPositions(validArray, indexKey: null);
        var supercharged = ReadSpecialSlots(specialArray, "TechBonus");
        var damaged = ReadSpecialSlots(specialArray, "BlockedByBrokenTech");

        var (width, height) = ReadDimensions(inventory, filled.Keys, valid);

        var cells = new List<TechSlot>(width * height);
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                var pos = (x, y);
                bool hasItem = filled.TryGetValue(pos, out string? itemId);

                // An empty ValidSlotIndices means every cell is unlocked, not none - this
                // matches NMSE, and exported cargo inventories routinely have none.
                SlotState state = hasItem ? SlotState.Filled
                    : valid.Count == 0 || valid.Contains(pos) ? SlotState.Empty
                    : SlotState.Locked;

                cells.Add(new TechSlot(x, y, state, hasItem ? itemId : null,
                    supercharged.Contains(pos), damaged.Contains(pos)));
            }
        }

        var stray = outOfBoundsCandidates
            .Where(c => c.Pos.X >= width || c.Pos.Y >= height)
            .Select(c => c.Id)
            .ToList();

        return new TechGrid(width, height, cells, stray);
    }

    private static string? ReadItemId(JsonObject slot)
    {
        // Id is usually a string but can be a nested object, and can be BinaryData.
        var nested = slot.GetObject("Id");
        object? raw = nested is not null ? nested.Get("Id") : slot.Get("Id");
        return raw?.ToString();
    }

    private static (int X, int Y)? ReadIndex(JsonObject? index)
    {
        if (index is null) return null;
        if (index.Get("X") is not { } x || index.Get("Y") is not { } y) return null;
        return (ToInt(x), ToInt(y));
    }

    private static HashSet<(int X, int Y)> ReadPositions(JsonArray? array, string? indexKey)
    {
        var result = new HashSet<(int, int)>();
        for (int i = 0; i < (array?.Length ?? 0); i++)
        {
            var entry = array!.GetObject(i);
            if (entry is null) continue;
            var pos = ReadIndex(indexKey is null ? entry : entry.GetObject(indexKey));
            if (pos is not null) result.Add(pos.Value);
        }
        return result;
    }

    /// <summary>
    /// Supercharged is not a property of a slot: it lives in the sibling SpecialSlots array
    /// and applies to empty cells as well as filled ones.
    /// </summary>
    private static HashSet<(int X, int Y)> ReadSpecialSlots(JsonArray? array, string type)
    {
        var result = new HashSet<(int, int)>();
        for (int i = 0; i < (array?.Length ?? 0); i++)
        {
            var entry = array!.GetObject(i);
            if (entry is null) continue;
            if (!string.Equals(entry.GetObject("Type")?.GetString("InventorySpecialSlotType"),
                    type, StringComparison.Ordinal))
                continue;

            var pos = ReadIndex(entry.GetObject("Index"));
            if (pos is not null) result.Add(pos.Value);
        }
        return result;
    }

    /// <summary>
    /// Grid size, falling back the way NMSE does when Width/Height are absent or zero:
    /// derive from the furthest occupied position.
    /// </summary>
    private static (int Width, int Height) ReadDimensions(
        JsonObject inventory, IEnumerable<(int X, int Y)> filled, IEnumerable<(int X, int Y)> valid)
    {
        int width = ToInt(inventory.Get("Width"));
        int height = ToInt(inventory.Get("Height"));
        if (width > 0 && height > 0) return (width, height);

        var all = filled.Concat(valid).ToList();
        if (all.Count == 0) return (Math.Max(width, 1), Math.Max(height, 1));

        return (Math.Max(width, all.Max(p => p.X) + 1), Math.Max(height, all.Max(p => p.Y) + 1));
    }

    private static int ToInt(object? value) => value switch
    {
        int i => i,
        long l => (int)l,
        double d => (int)d,
        RawDouble r => (int)r.Value,
        string s when int.TryParse(s, out int parsed) => parsed,
        _ => 0,
    };
}
