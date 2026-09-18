// Ported from NMSE (No Man's Save Editor) — https://github.com/vectorcmdr/NMSE
// Original: Models/JsonException.cs
// Copyright (C) the NMSE authors. Licensed under the GNU Affero General Public License v3.
// Modified 2026-09-18 for NMS-Vault: namespace changed
// This file has been changed from the original.

namespace NmsVault.Json;

/// <summary>
/// Exception thrown when JSON parsing or serialization encounters an error.
/// Includes optional line and column information for locating the error in the source.
/// </summary>
public class JsonException : Exception
{
    /// <summary>The line number where the error occurred, or 0 if unavailable.</summary>
    public int Line { get; }

    /// <summary>The column number where the error occurred, or 0 if unavailable.</summary>
    public int Column { get; }

    /// <summary>
    /// Initializes a new JSON exception with the specified message.
    /// </summary>
    /// <param name="message">A description of the parsing error.</param>
    public JsonException(string message) : base(message) { }

    /// <summary>
    /// Initializes a new JSON exception with position information.
    /// </summary>
    /// <param name="message">A description of the parsing error.</param>
    /// <param name="line">The line number in the JSON source where the error occurred.</param>
    /// <param name="column">The column number in the JSON source where the error occurred.</param>
    public JsonException(string message, int line, int column) : base($"{message} at line {line}, column {column}")
    {
        Line = line;
        Column = column;
    }

    /// <summary>
    /// Initializes a new JSON exception with position information and an inner exception.
    /// </summary>
    /// <param name="message">A description of the parsing error.</param>
    /// <param name="inner">The exception that caused this error.</param>
    /// <param name="line">The line number in the JSON source where the error occurred.</param>
    /// <param name="column">The column number in the JSON source where the error occurred.</param>
    public JsonException(string message, Exception inner, int line, int column) : base($"{message} at line {line}, column {column}", inner)
    {
        Line = line;
        Column = column;
    }
}
