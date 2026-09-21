namespace NmsVault.Core.Derived;

/// <summary>
/// Turning the game's identifiers into something readable.
/// </summary>
/// <remarks>
/// The game writes a good deal of its data as run-together words - <c>SuperRare</c>,
/// <c>LandJellyfish</c>, <c>MiniRobo</c> - because they are enum names rather than prose.
/// Printed raw they read as code; split, they read as English.
/// </remarks>
public static class Words
{
    /// <summary>
    /// Splits a run-together identifier into words.
    /// </summary>
    /// <remarks>
    /// Only on a capital following a lower-case letter, so an acronym stays whole: SuperRare
    /// becomes "Super Rare" while NX stays "NX". A value that is already one word comes back
    /// unchanged, which is most of them.
    /// </remarks>
    /// <param name="value">The identifier.</param>
    /// <returns>The same text with spaces at the word boundaries.</returns>
    public static string Spaced(string? value)
    {
        if (value is not { Length: > 1 }) return value ?? "";

        var built = new System.Text.StringBuilder(value.Length + 4);
        built.Append(value[0]);

        for (int i = 1; i < value.Length; i++)
        {
            if (char.IsUpper(value[i]) && char.IsLower(value[i - 1])) built.Append(' ');
            built.Append(value[i]);
        }

        return built.ToString();
    }

    /// <summary>
    /// How a creature gets about, as a value under a label rather than as a verb.
    /// </summary>
    /// <param name="moveArea">The species table's <c>MoveArea</c> - Ground, Water, Air, Space.</param>
    /// <returns>The same thing in English, or the raw value if it is not one of the four.</returns>
    public static string Movement(string? moveArea) => moveArea switch
    {
        "Ground" => "Walking",
        "Water" => "Swimming",
        "Air" => "Flying",
        "Space" => "Drifting",
        _ => moveArea ?? "",
    };
}
