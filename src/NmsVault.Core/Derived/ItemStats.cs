using NmsVault.Json;

namespace NmsVault.Core.Derived;

/// <summary>One base stat, as the gallery shows and sorts on it.</summary>
/// <param name="Id">The game's stat id, e.g. <c>^SHIP_DAMAGE</c>.</param>
/// <param name="Label">Display label, e.g. "Damage".</param>
/// <param name="Value">The raw value.</param>
public readonly record struct ItemStat(string Id, string Label, double Value);

/// <summary>
/// Reads base stats out of an inventory's <c>BaseStatValues</c> array.
/// </summary>
/// <remarks>
/// <para>
/// Values are reported raw, with no normalisation, because there is nothing honest to
/// normalise against: NMSE's <c>BaseStatLimits</c> ships with every maximum set to
/// <c>int.MaxValue</c> and its <c>LeveledStatDatabase</c> has no data file. Sorting on raw
/// values is exact; a bar would need a hardcoded maximum table that goes stale with each
/// game update and would be quietly wrong in the meantime.
/// </para>
/// <para>
/// An absent stat and a stat of zero are indistinguishable here, matching NMSE. Both read as
/// zero, which is the right answer for display in either case.
/// </para>
/// </remarks>
public static class ItemStats
{
    /// <summary>Ship stats, in the order the game and NMSE present them.</summary>
    public static readonly (string Id, string Label)[] ShipStats =
    [
        ("^SHIP_DAMAGE", "Damage"),
        ("^SHIP_SHIELD", "Shield"),
        ("^SHIP_HYPERDRIVE", "Hyperdrive"),
        ("^SHIP_AGILE", "Manoeuvrability"),
    ];

    /// <summary>Multitool stats.</summary>
    public static readonly (string Id, string Label)[] MultitoolStats =
    [
        ("^WEAPON_DAMAGE", "Damage"),
        ("^WEAPON_MINING", "Mining"),
        ("^WEAPON_SCAN", "Scan"),
    ];

    /// <summary>
    /// Stat ids that are not really stats: the game uses them as family markers, and they
    /// carry a value of 1 on ships that have them. Excluded from display and sorting.
    /// </summary>
    private static readonly HashSet<string> FamilyMarkers =
        new(StringComparer.Ordinal) { "^ALIEN_SHIP", "^ROBOT_SHIP" };

    /// <summary>Reads one stat's value, or zero when it is absent.</summary>
    public static double Read(JsonObject? inventory, string statId)
    {
        var values = inventory?.GetArray("BaseStatValues");
        if (values is null) return 0.0;

        for (int i = 0; i < values.Length; i++)
        {
            var entry = values.GetObject(i);
            if (entry is null) continue;
            if (string.Equals(entry.GetString("BaseStatID"), statId, StringComparison.Ordinal))
                return ToDouble(entry.Get("Value"));
        }
        return 0.0;
    }

    /// <summary>Reads a ship's four stats from its cargo inventory.</summary>
    public static IReadOnlyList<ItemStat> ForShip(JsonObject ship)
        => Read(ship.GetObject("Inventory"), ShipStats);

    /// <summary>Reads a multitool's three stats from its store.</summary>
    public static IReadOnlyList<ItemStat> ForMultitool(JsonObject multitool)
        => Read(multitool.GetObject("Store"), MultitoolStats);

    private static IReadOnlyList<ItemStat> Read(JsonObject? inventory, (string Id, string Label)[] wanted)
        => [.. wanted.Select(w => new ItemStat(w.Id, w.Label, Read(inventory, w.Id)))];

    /// <summary>
    /// Whether an inventory carries a family marker such as <c>^ALIEN_SHIP</c>. These sit in
    /// <c>BaseStatValues</c> alongside real stats but describe what the ship <em>is</em> - a
    /// second, independent signal to the model path that <see cref="ShipTypes"/> reads.
    /// </summary>
    public static string? FamilyMarker(JsonObject? inventory)
    {
        var values = inventory?.GetArray("BaseStatValues");
        if (values is null) return null;

        for (int i = 0; i < values.Length; i++)
        {
            string? id = values.GetObject(i)?.GetString("BaseStatID");
            if (id is not null && FamilyMarkers.Contains(id)) return id;
        }
        return null;
    }

    private static double ToDouble(object? value) => value switch
    {
        double d => d,
        RawDouble r => r.Value,
        int i => i,
        long l => l,
        float f => f,
        _ => 0.0,
    };
}
