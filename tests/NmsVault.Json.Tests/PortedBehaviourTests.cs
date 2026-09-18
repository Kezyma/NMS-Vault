using System.Text;
using NmsVault.Json;

namespace NmsVault.Json.Tests;

/// <summary>
/// Tests for the behaviour that differs from NMSE's original JSON engine: the embedded
/// mapping table, byte-level entry points, explicit auto-detect, and LF line endings.
/// The inherited behaviour is covered by <see cref="JsonModelTests"/>.
/// </summary>
public class PortedBehaviourTests
{
    // --- Embedded mapping table ---------------------------------------

    [Fact]
    public void LoadEmbedded_ResolvesTheMappingResource()
    {
        var mapper = JsonNameMapper.LoadEmbedded();

        // Spot-check keys this project actually depends on.
        Assert.Equal("ShipOwnership", mapper.ToName("@Cs"));
        Assert.Equal("ShipUsesLegacyColours", mapper.ToName("4hl"));
        Assert.Equal("UsesLegacyColours", mapper.ToName("U>8"));
        Assert.Equal("Data", mapper.ToName("8?J"));
    }

    [Fact]
    public void LoadEmbedded_RoundTripsKeysBothWays()
    {
        var mapper = JsonNameMapper.LoadEmbedded();

        Assert.Equal("@Cs", mapper.ToKey(mapper.ToName("@Cs")));
        Assert.True(mapper.IsObfuscatedKey("@Cs"));
        Assert.False(mapper.IsObfuscatedKey("ShipOwnership"));
    }

    [Fact]
    public void LoadEmbedded_PassesUnknownKeysThrough()
    {
        var mapper = JsonNameMapper.LoadEmbedded();

        // Both translators pass unknown names through unchanged. This is load-bearing:
        // it is why a stray key inside a game object survives into a real save.
        Assert.Equal("__NotAGameKey", mapper.ToName("__NotAGameKey"));
        Assert.Equal("__NotAGameKey", mapper.ToKey("__NotAGameKey"));
    }

    // --- Auto-detect is explicit, not global --------------------------

    [Fact]
    public void ParseObject_WithoutAutoDetect_LeavesObfuscatedKeysAlone()
    {
        // No mapper supplied anywhere: the parser must not reach for global state.
        var obj = JsonParser.ParseObject("""{ "@Cs": [], "4hl": [true] }""");

        Assert.True(obj.Contains("@Cs"));
        Assert.False(obj.Contains("ShipOwnership"));
    }

    [Fact]
    public void ParseObject_WithAutoDetect_TranslatesObfuscatedKeys()
    {
        var mapper = JsonNameMapper.LoadEmbedded();
        var obj = JsonParser.ParseObject("""{ "@Cs": [], "4hl": [true] }""",
            mapper: null, autoDetect: mapper);

        Assert.True(obj.Contains("ShipOwnership"));
        Assert.True(obj.Contains("ShipUsesLegacyColours"));
    }

    [Fact]
    public void ParseObject_WithAutoDetect_LeavesReadableKeysAlone()
    {
        // An NMSE or goatfungus export is already human-readable; auto-detect must no-op.
        var mapper = JsonNameMapper.LoadEmbedded();
        var obj = JsonParser.ParseObject("""{ "Ship": {}, "UsesLegacyColours": false }""",
            mapper: null, autoDetect: mapper);

        Assert.True(obj.Contains("Ship"));
        Assert.True(obj.Contains("UsesLegacyColours"));
    }

    [Fact]
    public void ParseObject_AutoDetect_RemapsKeysSeenBeforeDetectionFired()
    {
        // PS4-shaped input: a readable key first, obfuscated ones after. The already-added
        // key must be retro-fixed when detection fires, not left half-translated.
        var mapper = JsonNameMapper.LoadEmbedded();
        var obj = JsonParser.ParseObject("""{ "Version": 1, "@Cs": [], "4hl": [true] }""",
            mapper: null, autoDetect: mapper);

        Assert.True(obj.Contains("Version"));
        Assert.True(obj.Contains("ShipOwnership"));
        Assert.True(obj.Contains("ShipUsesLegacyColours"));
    }

    [Fact]
    public void ParseObject_AutoDetectIsPerObject_NotPerFile()
    {
        // A NomNom envelope has readable root keys and an obfuscated payload beneath.
        // Detection must re-arm on the nested object rather than giving up at the root.
        var mapper = JsonNameMapper.LoadEmbedded();
        var obj = JsonParser.ParseObject(
            """{ "FileVersion": 2, "Data": { "@Cs": [], "4hl": [true] } }""",
            mapper: null, autoDetect: mapper);

        Assert.True(obj.Contains("FileVersion"));
        var data = obj.GetObject("Data");
        Assert.NotNull(data);
        Assert.True(data!.Contains("ShipOwnership"));
    }

    // --- Byte-level entry points --------------------------------------

    [Fact]
    public void FromBytes_ParsesLatin1TransparentBytes()
    {
        byte[] bytes = Encoding.UTF8.GetBytes("""{ "Name": "Plain" }""");
        var obj = JsonObject.FromBytes(bytes);

        Assert.Equal("Plain", obj.GetString("Name"));
    }

    [Fact]
    public void FromBytes_RoundTripsNonAsciiThroughLatin1Encoding()
    {
        // The export string holds UTF-8 bytes in Latin-1 chars, so Latin1 is the correct
        // encoding on the way out. Encoding with UTF8 instead would double-encode.
        var source = new JsonObject();
        source.Set("Name", "Nöstromo");

        string text = source.ToExportString();
        byte[] bytes = Encoding.Latin1.GetBytes(text);

        // Well-formed UTF-8 on the wire, with no \uXXXX escapes (goatfungus rejects those).
        Assert.Equal("Nöstromo", Encoding.UTF8.GetString(bytes).Split('"')[3]);
        Assert.DoesNotContain("\\u", text);

        var reparsed = JsonObject.FromBytes(bytes);
        Assert.Equal("Nöstromo", reparsed.GetString("Name"));
    }

    [Fact]
    public void FromBytes_AutoDetectsObfuscatedPayload()
    {
        var mapper = JsonNameMapper.LoadEmbedded();
        byte[] bytes = Encoding.UTF8.GetBytes("""{ "@Cs": [], "4hl": [true] }""");

        var obj = JsonObject.FromBytes(bytes, mapper);

        Assert.True(obj.Contains("ShipOwnership"));
    }

    [Fact]
    public void ToExportString_UsesHumanReadableKeys()
    {
        var mapper = JsonNameMapper.LoadEmbedded();
        var obj = JsonObject.FromBytes(Encoding.UTF8.GetBytes("""{ "@Cs": [] }"""), mapper);

        // Export files are always deobfuscated, even when the source was obfuscated.
        Assert.Contains("ShipOwnership", obj.ToExportString());
        Assert.DoesNotContain("@Cs", obj.ToExportString());
    }

    // --- Line endings --------------------------------------------------

    [Fact]
    public void FormattedOutput_UsesLfRegardlessOfPlatform()
    {
        // Indentation lands in the exported bytes, so this must not vary by OS.
        var obj = new JsonObject();
        obj.Set("A", 1);
        obj.Set("B", 2);

        string text = obj.ToExportString();

        Assert.Contains("\n", text);
        Assert.DoesNotContain("\r", text);
        Assert.Equal("\n", JsonParser.NewLine);
    }

    // --- Fidelity the vault format depends on -------------------------

    [Fact]
    public void WholeNumberDoubles_KeepTheirDecimalPoint()
    {
        // NMS distinguishes integer 1 from float 1.0; emitting the wrong one corrupts a save.
        var obj = new JsonObject();
        obj.Set("Scale", 1.0);
        obj.Set("Count", 1);

        string text = JsonParser.Serialize(obj, formatted: false, skipReverseMapping: true);

        Assert.Equal("""{"Scale":1.0,"Count":1}""", text);
    }

    [Fact]
    public void RawDouble_PreservesOriginalTextVerbatim()
    {
        var obj = JsonObject.Parse("""{ "V": 0.30000001192092898 }""");

        string text = JsonParser.Serialize(obj, formatted: false, skipReverseMapping: true);

        Assert.Contains("0.30000001192092898", text);
    }

    [Fact]
    public void BinaryData_SurvivesAByteLevelRoundTrip()
    {
        // Bytes >= 0x80 that are not valid UTF-8 must come back byte-identical rather than
        // being mangled into replacement characters.
        byte[] payload = [0x7B, 0x22, 0x44, 0x22, 0x3A, 0x22, 0xFF, 0xFE, 0x22, 0x7D]; // {"D":"\xFF\xFE"}

        var obj = JsonObject.FromBytes(payload);
        byte[] roundTripped = Encoding.Latin1.GetBytes(
            JsonParser.Serialize(obj, formatted: false, skipReverseMapping: true));

        Assert.Equal(payload, roundTripped);
    }
}
