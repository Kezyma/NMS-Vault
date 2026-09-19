namespace NmsVault.Ingest;

/// <summary>
/// Minimal --key value argument parsing. Supports --flag, --key value and --key=value.
/// </summary>
internal sealed class Args
{
    private readonly Dictionary<string, string?> _values = new(StringComparer.OrdinalIgnoreCase);

    internal static Args Parse(IEnumerable<string> args)
    {
        var result = new Args();
        string? pending = null;

        foreach (var arg in args)
        {
            if (arg.StartsWith("--", StringComparison.Ordinal))
            {
                if (pending is not null) result._values[pending] = null;   // a bare flag

                string body = arg[2..];
                int eq = body.IndexOf('=');
                if (eq >= 0)
                {
                    result._values[body[..eq]] = body[(eq + 1)..];
                    pending = null;
                }
                else
                {
                    pending = body;
                }
            }
            else if (pending is not null)
            {
                result._values[pending] = arg;
                pending = null;
            }
        }

        if (pending is not null) result._values[pending] = null;
        return result;
    }

    internal bool Has(string key) => _values.ContainsKey(key);

    internal string? Get(string key) => _values.TryGetValue(key, out var v) ? v : null;

    internal string Require(string key) => Get(key)
        ?? throw new ArgumentException($"--{key} is required.");

    /// <summary>A comma-separated value as a list, trimmed, with blanks dropped.</summary>
    internal IReadOnlyList<string> GetList(string key) => Get(key) is { } raw
        ? raw.Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)
        : [];
}
