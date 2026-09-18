// Ported from NMSE (No Man's Save Editor) — https://github.com/vectorcmdr/NMSE
// Original: Models/JsonReader.cs
// Copyright (C) the NMSE authors. Licensed under the GNU Affero General Public License v3.
// Modified 2026-09-18 for NMS-Vault: namespace changed
// This file has been changed from the original.

namespace NmsVault.Json;

/// <summary>
/// High-performance JSON reader that operates directly on a string source.
/// Line/column tracking is deferred (computed lazily on error) to eliminate
/// per-character branching overhead in the hot reading path.
/// </summary>
internal sealed class JsonReader : IDisposable
{
    private readonly string _source;
    private int _pos;

    /// <summary>
    /// Lazily computed line number at the current position.
    /// Only computed when an error occurs (via ComputeLineColumn).
    /// </summary>
    public int Line => ComputeLine();

    /// <summary>
    /// Lazily computed column number at the current position.
    /// Only computed when an error occurs (via ComputeLineColumn).
    /// </summary>
    public int Column => ComputeColumn();

    public JsonReader(string source) => _source = source;

    /// <summary>
    /// Read the next character and advance position.
    /// No line/column tracking overhead - just bounds check and index.
    /// </summary>
    public int Read()
    {
        if (_pos >= _source.Length) return -1;
        return _source[_pos++];
    }

    /// <summary>
    /// Peek at the current character without consuming it.
    /// </summary>
    public int Peek() => _pos < _source.Length ? _source[_pos] : -1;

    /// <summary>
    /// Get direct access to the source string and current position for fast scanning.
    /// </summary>
    public string Source => _source;
    /// <summary>Current read position in the source string.</summary>
    public int Position => _pos;

    /// <summary>
    /// Advance position directly (for use after fast scanning).
    /// No character-by-character iteration - just sets the position.
    /// </summary>
    public void Advance(int newPos) => _pos = newPos;

    /// <summary>
    /// Read next non-whitespace character.
    /// Also skips null bytes (0x00) since NMS save files use null terminators after JSON data.
    /// All whitespace chars (space=0x20, tab=0x09, CR=0x0D, LF=0x0A) and null (0x00) are &lt;= 0x20,
    /// so a single comparison handles all of them.
    /// </summary>
    public int ReadSkipWhitespace()
    {
        while (_pos < _source.Length)
        {
            char c = _source[_pos++];
            if (c > ' ') return c;
        }
        return -1;
    }

    /// <summary>
    /// Read a digit character (0-9). Returns the char value, or -1 if not a digit (position unchanged).
    /// Inlined to avoid delegate overhead from Func-based ReadIf.
    /// </summary>
    public int ReadDigit()
    {
        if (_pos < _source.Length)
        {
            char c = _source[_pos];
            if (c >= '0' && c <= '9') { _pos++; return c; }
        }
        return -1;
    }

    /// <summary>
    /// Read a character if it matches the expected value. Returns the char, or -1 (position unchanged).
    /// </summary>
    public int ReadIf(char expected)
    {
        if (_pos < _source.Length && _source[_pos] == expected) { _pos++; return expected; }
        return -1;
    }

    /// <summary>
    /// Read a character if it matches either of two expected values. Returns the char, or -1 (position unchanged).
    /// </summary>
    public int ReadIfEither(char a, char b)
    {
        if (_pos < _source.Length)
        {
            char c = _source[_pos];
            if (c == a || c == b) { _pos++; return c; }
        }
        return -1;
    }

    /// <summary>
    /// Read a character if it matches a digit or +/-. Returns the char, or -1 (position unchanged).
    /// Used for exponent sign parsing.
    /// </summary>
    public int ReadDigitOrSign()
    {
        if (_pos < _source.Length)
        {
            char c = _source[_pos];
            if ((c >= '0' && c <= '9') || c == '+' || c == '-') { _pos++; return c; }
        }
        return -1;
    }

    // Conditional read - reads if predicate matches, otherwise unreads (legacy compatibility)

    /// <summary>
    /// Reads the next character if it matches the given predicate, otherwise leaves position unchanged.
    /// </summary>
    /// <param name="reader">The reader to read from.</param>
    /// <param name="predicate">A function that returns <c>true</c> if the character should be consumed.</param>
    /// <returns>The character read, or -1 if the predicate did not match or end-of-input was reached.</returns>
    public static int ReadIf(JsonReader reader, Func<int, bool> predicate)
    {
        if (reader._pos >= reader._source.Length) return -1;
        char c = reader._source[reader._pos];
        if (predicate(c)) { reader._pos++; return c; }
        return -1;
    }

    /// <summary>
    /// Moves the position back by one character, effectively "unreading" the last consumed character.
    /// </summary>
    internal void Unread()
    {
        if (_pos > 0) _pos--;
    }

    /// <summary>
    /// Compute line number at current position by scanning from the start.
    /// Only called on error paths, so performance is not critical.
    /// </summary>
    private int ComputeLine()
    {
        int line = 1;
        int scanTo = Math.Min(_pos, _source.Length);
        for (int i = 0; i < scanTo; i++)
            if (_source[i] == '\n') line++;
        return line;
    }

    /// <summary>
    /// Compute column number at current position by scanning from the last newline.
    /// Only called on error paths, so performance is not critical.
    /// </summary>
    private int ComputeColumn()
    {
        int scanTo = Math.Min(_pos, _source.Length);
        int lastNewline = -1;
        for (int i = scanTo - 1; i >= 0; i--)
        {
            if (_source[i] == '\n') { lastNewline = i; break; }
        }
        return scanTo - lastNewline;
    }

    /// <inheritdoc />
    public void Dispose() { /* No resources to dispose */ }
}
