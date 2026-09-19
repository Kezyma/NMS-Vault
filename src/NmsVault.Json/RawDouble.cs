// Ported from NMSE (No Man's Save Editor) — https://github.com/vectorcmdr/NMSE
// Original: Models/RawDouble.cs
// Copyright (C) the NMSE authors. Licensed under the GNU Affero General Public License v3.
// Modified 2026-09-18 for NMS-Vault: added the XML docs the build now requires; namespace changed
// This file has been changed from the original.

namespace NmsVault.Json;

/// <summary>
/// A double value that preserves its original JSON text representation.
/// When a save file is parsed, floating-point numbers are stored as <see cref="RawDouble"/>
/// so that serialization can reproduce the exact original text rather than applying
/// <see cref="double.ToString(string)"/> which may produce a different (but numerically
/// equivalent) representation.
///
/// For example, the game may write <c>0.30000001192092898</c> but .NET's "G17" format
/// for the same IEEE 754 double produces <c>0.30000001192092896</c>. Both parse to the
/// same bits, but the text difference causes unnecessary diffs.
/// </summary>
public readonly struct RawDouble
{
    /// <summary>The parsed IEEE 754 double value.</summary>
    public readonly double Value;

    /// <summary>The original JSON text (e.g., "0.30000001192092898").</summary>
    public readonly string Text;

    /// <summary>Pairs a parsed value with the text it was parsed from.</summary>
    /// <param name="value">The numeric value.</param>
    /// <param name="text">The original JSON text, preserved verbatim for round-tripping.</param>
    public RawDouble(double value, string text)
    {
        Value = value;
        Text = text;
    }

    /// <summary>Implicit conversion to <see cref="double"/> for arithmetic and comparisons.</summary>
    public static implicit operator double(RawDouble rd) => rd.Value;

    /// <summary>Returns the original JSON text, not a reformatted number.</summary>
    /// <returns>The verbatim source text.</returns>
    public override string ToString() => Text;
}
