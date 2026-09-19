namespace NmsVault.Web.Services;

/// <summary>
/// Holds what has been searched for and ticked, and decides whether a row survives it.
/// </summary>
/// <remarks>
/// <para>
/// The matching rule is <b>any within a heading, all across headings</b>. Ticking Fighter
/// and Hauler shows both; ticking Fighter and class S shows only S-class fighters. That is
/// what people expect of a filter list and it is worth stating, because the alternative -
/// all within a heading - reads identically in the UI and returns nothing.
/// </para>
/// <para>
/// One of these is handed to the search box, the filter bar and the list, and all three
/// mutate the same instance. Handing each of them a copy is how those three stop agreeing
/// about what is being shown.
/// </para>
/// </remarks>
/// <typeparam name="TRow">The row type being filtered.</typeparam>
/// <param name="facets">The headings this sifter knows about.</param>
/// <param name="searchText">Reads the text a search should match against.</param>
public sealed class Sifter<TRow>(IReadOnlyList<Facet<TRow>> facets, Func<TRow, string> searchText)
{
    private readonly IReadOnlyList<Facet<TRow>> _facets = facets;
    private readonly Func<TRow, string> _searchText = searchText;
    private readonly Dictionary<string, HashSet<string>> _ticked = new(StringComparer.Ordinal);

    /// <summary>What has been typed into the search box.</summary>
    public string Search { get; set; } = "";

    /// <summary>Whether anything is narrowing the list at all.</summary>
    public bool Any => Search.Length > 0 || _ticked.Values.Any(t => t.Count > 0);

    /// <summary>The values ticked under one heading. Mutated in place by the controls.</summary>
    /// <param name="facetKey">The heading.</param>
    /// <returns>Its ticked values.</returns>
    public HashSet<string> Ticked(string facetKey)
        => _ticked.TryGetValue(facetKey, out var set) ? set : _ticked[facetKey] = new(StringComparer.Ordinal);

    /// <summary>How many values are ticked under one heading.</summary>
    /// <param name="facetKey">The heading.</param>
    /// <returns>The count.</returns>
    public int TickedCount(string facetKey) => Ticked(facetKey).Count;

    /// <summary>Clears everything.</summary>
    public void Clear()
    {
        Search = "";
        foreach (var set in _ticked.Values) set.Clear();
    }

    /// <summary>Whether a row survives the current search and ticks.</summary>
    /// <param name="row">The row.</param>
    /// <returns>True when it should be shown.</returns>
    public bool Matches(TRow row)
    {
        // Matched anywhere in the text rather than as a prefix: someone looking for
        // "vulture" should find "Iron Vulture" without knowing what it is called first.
        if (Search.Length > 0
            && !_searchText(row).Contains(Search, StringComparison.OrdinalIgnoreCase))
            return false;

        foreach (var facet in _facets)
        {
            var ticked = Ticked(facet.Key);
            if (ticked.Count == 0) continue;

            if (!facet.ValuesOf(row).Any(v => ticked.Contains(v.Key)))
                return false;
        }

        return true;
    }
}
