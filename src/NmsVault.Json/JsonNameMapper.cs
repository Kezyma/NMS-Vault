// Ported from NMSE (No Man's Save Editor) — https://github.com/vectorcmdr/NMSE
// Original: Data/JsonNameMapper.cs
// Copyright (C) the NMSE authors. Licensed under the GNU Affero General Public License v3.
// Modified 2026-09-18 for NMS-Vault: removed Load(string) (filesystem I/O); namespace changed
// This file has been changed from the original.

using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace NmsVault.Json;

/// <summary>
/// Maps obfuscated 3-character NMS save file JSON key names to human-readable names and vice versa.
/// Loads Resources/db/mapping.json (JSON format).
/// Legacy tab-separated mapping files are no longer supported.
/// </summary>
public class JsonNameMapper
{
    private readonly Dictionary<string, string> _keyToName = new();
    private readonly Dictionary<string, string> _nameToKey = new();

    /// <summary>
    /// Load a mapping from a stream. Expects the JSON mapping format.
    /// </summary>
    public void Load(Stream stream)
    {
        using var reader = new StreamReader(stream, leaveOpen: true);
        string content = reader.ReadToEnd();

        var trimmed = content.TrimStart();
        if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            throw new InvalidDataException("Mapping file is not JSON. Only Resources/db/mapping.json (JSON) is supported.");

        try
        {
            using var doc = JsonDocument.Parse(content);
            if (doc.RootElement.TryGetProperty("Mapping", out JsonElement mappingArray) && mappingArray.ValueKind == JsonValueKind.Array)
            {
                foreach (var entry in mappingArray.EnumerateArray())
                {
                    if (entry.ValueKind != JsonValueKind.Object) continue;
                    if (!entry.TryGetProperty("Key", out JsonElement keyEl)) continue;
                    if (!entry.TryGetProperty("Value", out JsonElement valEl)) continue;
                    if (keyEl.ValueKind != JsonValueKind.String || valEl.ValueKind != JsonValueKind.String) continue;

                    string key = keyEl.GetString()!;
                    string name = valEl.GetString()!;

                    if (!_keyToName.ContainsKey(key) && !_nameToKey.ContainsKey(name))
                    {
                        _keyToName[key] = name;
                        _nameToKey[name] = key;
                    }
                }
            }
            else
            {
                throw new InvalidDataException("mapping.json does not contain a valid \"Mapping\" array.");
            }
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException("Failed to parse mapping.json", ex);
        }
    }

    // NMSE's Load(string filePath) lived here. It is replaced by the embedded-resource
    // loader below: WebAssembly has no filesystem, and embedding the 60 KB table removes
    // an async init step that every caller would otherwise have to await.

    /// <summary>
    /// Creates a mapper from the <c>mapping.json</c> embedded in this assembly.
    /// The table is MBINCompiler-derived and ships with the library.
    /// </summary>
    /// <returns>A loaded mapper.</returns>
    public static JsonNameMapper LoadEmbedded()
    {
        var assembly = typeof(JsonNameMapper).Assembly;
        const string resourceName = "NmsVault.Json.mapping.json";

        using var stream = assembly.GetManifestResourceStream(resourceName)
            ?? throw new InvalidDataException(
                $"Embedded resource '{resourceName}' is missing. Check the EmbeddedResource item in the csproj.");

        var mapper = new JsonNameMapper();
        mapper.Load(stream);
        return mapper;
    }

    /// <summary>
    /// Translate an obfuscated key to its human-readable name.
    /// Returns the key unchanged if no mapping exists.
    /// </summary>
    public string ToName(string key)
    {
        return _keyToName.TryGetValue(key, out string? name) ? name : key;
    }

    /// <summary>
    /// Translate a human-readable name back to its obfuscated key.
    /// Returns the name unchanged if no mapping exists.
    /// </summary>
    public string ToKey(string name)
    {
        return _nameToKey.TryGetValue(name, out string? key) ? key : name;
    }

    /// <summary>
    /// Check if a given string is a known obfuscated key.
    /// </summary>
    public bool IsObfuscatedKey(string key)
    {
        return _keyToName.ContainsKey(key);
    }

    /// <summary>Total number of key-name mappings loaded.</summary>
    public int Count => _keyToName.Count;

    /// <summary>
    /// Loads mappings from a dictionary of obfuscated-key -> human-readable-name pairs.
    /// Used by tests to create lightweight mapper instances without loading from files.
    /// </summary>
    /// <param name="keyToName">Dictionary mapping obfuscated keys (e.g. "Abc") to human-readable names (e.g. "PlayerStateData").</param>
    internal void LoadFromDictionary(Dictionary<string, string> keyToName)
    {
        foreach (var (key, name) in keyToName)
        {
            if (!_keyToName.ContainsKey(key) && !_nameToKey.ContainsKey(name))
            {
                _keyToName[key] = name;
                _nameToKey[name] = key;
            }
        }
    }
}