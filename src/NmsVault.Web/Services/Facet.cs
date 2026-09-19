namespace NmsVault.Web.Services;

/// <summary>One value a filter heading can offer, and the count of rows holding it.</summary>
/// <param name="Key">The value as it is matched, e.g. "Hauler".</param>
/// <param name="Label">How it reads to someone, usually the same.</param>
public readonly record struct FacetValue(string Key, string Label)
{
    /// <summary>A value whose key and label are the same.</summary>
    /// <param name="key">The value.</param>
    public FacetValue(string key) : this(key, key) { }
}

/// <summary>
/// A filter heading: a name, and how to read the values a row holds under it.
/// </summary>
/// <remarks>
/// <para>
/// Declared rather than written out, so adding a filter is one entry in a table and the
/// filter bar builds itself. A heading offers only the values actually present in the rows
/// it is given - a type nothing in the gallery has is not worth a checkbox.
/// </para>
/// <para>
/// The constructor is private and the factories name the arity, because a heading that holds
/// one value per row and one that holds several behave differently when matched, and getting
/// that wrong is silent.
/// </para>
/// </remarks>
/// <typeparam name="TRow">The row type being filtered.</typeparam>
public sealed class Facet<TRow>
{
    private readonly Func<TRow, IEnumerable<FacetValue>> _values;

    private Facet(string key, string label, Func<TRow, IEnumerable<FacetValue>> values)
    {
        Key = key;
        Label = label;
        _values = values;
    }

    /// <summary>How this heading is stored and addressed.</summary>
    public string Key { get; }

    /// <summary>How the heading reads.</summary>
    public string Label { get; }

    /// <summary>A heading where each row holds exactly one value, or none.</summary>
    /// <param name="key">Storage key.</param>
    /// <param name="label">Display label.</param>
    /// <param name="value">Reads the row's value, or null where it has none.</param>
    /// <returns>The heading.</returns>
    public static Facet<TRow> One(string key, string label, Func<TRow, string?> value)
        => new(key, label, row => value(row) is { Length: > 0 } v ? [new FacetValue(v)] : []);

    /// <summary>A heading where a row can hold several values at once.</summary>
    /// <param name="key">Storage key.</param>
    /// <param name="label">Display label.</param>
    /// <param name="values">Reads the row's values.</param>
    /// <returns>The heading.</returns>
    public static Facet<TRow> Many(string key, string label, Func<TRow, IEnumerable<string>> values)
        => new(key, label, row => values(row).Select(v => new FacetValue(v)));

    /// <summary>A heading whose values need a label different from the key.</summary>
    /// <param name="key">Storage key.</param>
    /// <param name="label">Display label.</param>
    /// <param name="values">Reads the row's labelled values.</param>
    /// <returns>The heading.</returns>
    public static Facet<TRow> Labelled(string key, string label, Func<TRow, IEnumerable<FacetValue>> values)
        => new(key, label, values);

    /// <summary>The values a row holds under this heading.</summary>
    /// <param name="row">The row.</param>
    /// <returns>Its values, possibly none.</returns>
    public IEnumerable<FacetValue> ValuesOf(TRow row) => _values(row);

    /// <summary>
    /// Every value present across the given rows, with how many rows hold each, most common
    /// first and then alphabetically so the order is stable as counts change.
    /// </summary>
    /// <param name="rows">The rows to survey.</param>
    /// <returns>The offered values and their counts.</returns>
    public IReadOnlyList<(FacetValue Value, int Count)> Offer(IEnumerable<TRow> rows)
    {
        var counts = new Dictionary<string, (FacetValue Value, int Count)>(StringComparer.Ordinal);

        foreach (var row in rows)
            foreach (var value in ValuesOf(row))
                counts[value.Key] = counts.TryGetValue(value.Key, out var seen)
                    ? (seen.Value, seen.Count + 1)
                    : (value, 1);

        return [.. counts.Values
            .OrderByDescending(c => c.Count)
            .ThenBy(c => c.Value.Label, StringComparer.OrdinalIgnoreCase)];
    }
}
