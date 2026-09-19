using NmsVault.Json;

namespace NmsVault.Core.Adapters;

/// <summary>
/// Rewrites human-readable JSON keys back to the game's obfuscated three-character form,
/// which is what NMS Companion and NomNom payloads use.
/// </summary>
/// <remarks>
/// <para>
/// Done as an explicit tree rewrite rather than by setting <c>JsonObject.NameMapper</c> and
/// letting the serialiser reverse-map, because those formats need a <em>mixed</em>
/// document: readable envelope keys (<c>Description</c>, <c>FileVersion</c>,
/// <c>Thumbnail</c>) wrapped around an obfuscated payload. Handing the whole graph to the
/// serialiser's mapper would also rewrite envelope keys that happen to exist in the table -
/// <c>Colours</c> is one, so Kaii's readable <c>Colours</c> key would silently become
/// <c>Aak</c> and the file would be unreadable.
/// </para>
/// <para>
/// Unmapped names pass through unchanged, matching <c>JsonNameMapper.ToKey</c>. That is
/// also the failure mode to be aware of: a stale mapping table does not error, it quietly
/// emits a half-obfuscated document. <see cref="CountUnmapped"/> exists so callers can
/// detect that rather than discover it downstream.
/// </para>
/// </remarks>
public static class KeyObfuscator
{
    /// <summary>
    /// Returns a deep copy of <paramref name="source"/> with every key obfuscated.
    /// The original is left untouched.
    /// </summary>
    public static JsonObject Obfuscate(JsonObject source, JsonNameMapper mapper)
    {
        var result = new JsonObject();
        foreach (var name in source.Names())
            result.Set(mapper.ToKey(name), ObfuscateValue(source.Get(name), mapper));
        return result;
    }

    /// <summary>Returns a deep copy of <paramref name="source"/> with element keys obfuscated.</summary>
    public static JsonArray Obfuscate(JsonArray source, JsonNameMapper mapper)
    {
        var result = new JsonArray();
        for (int i = 0; i < source.Length; i++)
            result.Add(ObfuscateValue(source.Get(i), mapper));
        return result;
    }

    /// <summary>
    /// The inverse: returns a deep copy with every obfuscated key translated back to its
    /// readable name. Used on ingestion, after detection has established that a document's
    /// payload is obfuscated.
    /// </summary>
    public static JsonObject Deobfuscate(JsonObject source, JsonNameMapper mapper)
    {
        var result = new JsonObject();
        foreach (var name in source.Names())
            result.Set(mapper.ToName(name), DeobfuscateValue(source.Get(name), mapper));
        return result;
    }

    /// <inheritdoc cref="Deobfuscate(JsonObject, JsonNameMapper)"/>
    public static JsonArray Deobfuscate(JsonArray source, JsonNameMapper mapper)
    {
        var result = new JsonArray();
        for (int i = 0; i < source.Length; i++)
            result.Add(DeobfuscateValue(source.Get(i), mapper));
        return result;
    }

    private static object? DeobfuscateValue(object? value, JsonNameMapper mapper) => value switch
    {
        JsonObject obj => Deobfuscate(obj, mapper),
        JsonArray arr => Deobfuscate(arr, mapper),
        _ => value,
    };

    private static object? ObfuscateValue(object? value, JsonNameMapper mapper) => value switch
    {
        JsonObject obj => Obfuscate(obj, mapper),
        JsonArray arr => Obfuscate(arr, mapper),
        // Scalars, RawDouble and BinaryData pass through by reference: they are immutable
        // values, so sharing them with the source is safe and avoids needless copying.
        _ => value,
    };

    /// <summary>
    /// Counts keys the mapper has no obfuscated form for, recursively.
    /// <para>
    /// A non-zero count means the mapping table is older than the data - the exact trap
    /// that made libNOM unusable here, since its table maps for NMS 5.5 and would leave
    /// every 6.x and 7.x key readable inside an otherwise obfuscated document.
    /// </para>
    /// </summary>
    public static int CountUnmapped(JsonObject source, JsonNameMapper mapper)
    {
        int count = 0;
        foreach (var name in source.Names())
        {
            if (mapper.ToKey(name) == name && !LooksAlreadyObfuscated(name, mapper))
                count++;
            count += CountUnmappedValue(source.Get(name), mapper);
        }
        return count;
    }

    private static int CountUnmappedValue(object? value, JsonNameMapper mapper) => value switch
    {
        JsonObject obj => CountUnmapped(obj, mapper),
        JsonArray arr => CountUnmappedInArray(arr, mapper),
        _ => 0,
    };

    private static int CountUnmappedInArray(JsonArray arr, JsonNameMapper mapper)
    {
        int count = 0;
        for (int i = 0; i < arr.Length; i++)
            count += CountUnmappedValue(arr.Get(i), mapper);
        return count;
    }

    /// <summary>
    /// Whether a name is already an obfuscated key, so leaving it alone is correct rather
    /// than a miss. Keeps <see cref="CountUnmapped"/> from flagging a document that was
    /// obfuscated to begin with.
    /// </summary>
    private static bool LooksAlreadyObfuscated(string name, JsonNameMapper mapper)
        => mapper.IsObfuscatedKey(name);
}
