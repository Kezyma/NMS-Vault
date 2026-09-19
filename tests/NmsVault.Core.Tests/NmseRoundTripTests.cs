using System.Text;
using NmsVault.Core;
using NmsVault.Core.Adapters;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// NMSE export -> vault item -> NMSE export, over the real fixture corpus.
/// <para>
/// This is the load-bearing test for the whole project. If an item cannot survive a round
/// trip through the vault format unchanged, nothing downstream can be trusted - and unlike
/// the other three editors, NMSE supports the current game version, so this one can
/// actually be checked against production files.
/// </para>
/// </summary>
public class NmseRoundTripTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");

    private static readonly NmseExportAdapter Adapter = new();

    private static VaultMetadata Meta(string id) => new() { Id = id, DisplayName = id };

    public static TheoryData<string> Ships() => Fixtures("starships");
    public static TheoryData<string> Multitools() => Fixtures("multitools");

    private static TheoryData<string> Fixtures(string folder)
    {
        var data = new TheoryData<string>();
        foreach (var path in Directory.EnumerateFiles(Path.Combine(FixtureRoot, folder)))
            data.Add(Path.GetFileName(path));
        return data;
    }

    private static byte[] Read(string folder, string name)
        => File.ReadAllBytes(Path.Combine(FixtureRoot, folder, name));

    /// <summary>
    /// NMSE wrote the corpus on Windows via Environment.NewLine, so it is CRLF; this port
    /// emits LF deliberately. Normalise the NMSE side only - everything else still has to
    /// match byte for byte.
    /// </summary>
    private static byte[] NormaliseLineEndings(byte[] bytes)
    {
        var result = new List<byte>(bytes.Length);
        for (int i = 0; i < bytes.Length; i++)
        {
            if (bytes[i] == (byte)'\r' && i + 1 < bytes.Length && bytes[i + 1] == (byte)'\n') continue;
            result.Add(bytes[i]);
        }
        return [.. result];
    }

    [Theory]
    [MemberData(nameof(Ships))]
    public void Starship_RoundTripsThroughTheVaultFormat(string name)
    {
        byte[] original = NormaliseLineEndings(Read("starships", name));

        var item = NmseImporter.Read(original, Meta("x"), ".nmsship");
        var exported = Adapter.Export(item);

        Assert.Equal(original, exported.Content);
    }

    [Theory]
    [MemberData(nameof(Multitools))]
    public void Multitool_RoundTripsThroughTheVaultFormat(string name)
    {
        byte[] original = NormaliseLineEndings(Read("multitools", name));

        var item = NmseImporter.Read(original, Meta("x"), ".nmstool");
        var exported = Adapter.Export(item);

        Assert.Equal(original, exported.Content);
    }

    // --- The sidecar the vault exists to protect ----------------------

    [Theory]
    [InlineData("[PRE-PC] Horizon Omega (Original).nmsship", true)]
    [InlineData("[PRE-PC] Horizon Omega (New).nmsship", false)]
    [InlineData("[PRE-PS] Alpha Vector (Original).nmsship", true)]
    [InlineData("[PRE-PS] Alpha Vector (New).nmsship", false)]
    public void LegacyColourFlag_SurvivesImport(string name, bool expected)
    {
        var item = NmseImporter.Read(Read("starships", name), Meta("x"), ".nmsship");

        Assert.Equal(expected, item.UsesLegacyColours);
    }

    [Fact]
    public void AbsentLegacyColourFlag_StaysNullRatherThanBecomingFalse()
    {
        // Files exported before the flag was added omit it entirely. Null and false are
        // different answers: null means "this file does not say", and an importer must
        // leave the destination slot alone rather than forcing modern colours.
        var stripped = JsonObject.FromBytes(Read("starships", "[START] Rasamama S36.nmsship"));
        Assert.True(stripped.Get("UsesLegacyColours") is bool);  // guard: the fixture has it
        stripped.Remove("UsesLegacyColours");

        var item = NmseImporter.Read(stripped, Meta("x"), ".nmsship");

        Assert.Null(item.UsesLegacyColours);
    }

    [Fact]
    public void AbsentLegacyColourFlag_IsNotInventedOnExport()
    {
        var stripped = JsonObject.FromBytes(Read("starships", "[START] Rasamama S36.nmsship"));
        stripped.Remove("UsesLegacyColours");

        var item = NmseImporter.Read(stripped, Meta("x"), ".nmsship");
        var text = Encoding.Latin1.GetString(Adapter.Export(item).Content);

        Assert.DoesNotContain("UsesLegacyColours", text);
    }

    // --- Vault metadata --------------------------------------------------

    [Fact]
    public void VaultBlock_IsStrippedOnExportButSurvivesStorage()
    {
        var meta = new VaultMetadata
        {
            Id = "golden-vector",
            DisplayName = "Golden Vector",
            AlternativeNames = ["Gold Fighter"],
            Description = "An expedition reward.",
            Images = ["img/golden-vector-1.png", "img/golden-vector-2.png"],
            Tags = ["fighter", "s-class", "gold"],
        };
        var item = NmseImporter.Read(Read("starships", "[EXP-01-R] Golden Vector.nmsship"), meta, ".nmsship");

        // Stored in the vault...
        var stored = VaultItem.FromBytes(item.ToBytes());
        Assert.Equal("Golden Vector", stored.Meta.DisplayName);
        Assert.Equal(["fighter", "s-class", "gold"], stored.Meta.Tags);
        Assert.Equal(EntityKind.Starship, stored.Kind);

        // ...but never leaks into a file handed to an editor.
        string exported = Encoding.Latin1.GetString(Adapter.Export(stored).Content);
        Assert.DoesNotContain("\"Vault\"", exported);
        Assert.DoesNotContain("golden-vector", exported);
    }

    [Fact]
    public void EveryMetadataFieldSurvivesBeingStoredAndReadBack()
    {
        // One field being forgotten in ToJson is a silent loss - nothing fails, the value is
        // simply gone next time the item is read. Source went in that way: the property and
        // the reader were both added and the writer was not, and it took a re-import of the
        // whole gallery to notice.
        var meta = new VaultMetadata
        {
            Id = "rezosu-z65",
            DisplayName = "Rezosu Z65",
            AlternativeNames = ["PS4 tool"],
            Summary = "The PlayStation pre-order multi-tool.",
            Description = "Two lines.\nThe second one.",
            Images = ["img/rezosu-z65.webp"],
            Tags = ["pre-order", "pistol"],
            Source = "[PRE-PS] Rezosu Z65.nmstool",
            Author = "Someone",
            DateAdded = new DateTimeOffset(2026, 9, 19, 12, 0, 0, TimeSpan.Zero),
            GameVersion = "7.03",
        };

        var written = meta.ToJson();
        var read = VaultMetadata.FromJson(written);

        // Every property, found by reflection rather than listed here, so a field added later
        // is covered the day it is added rather than the day someone remembers this test.
        var names = written.Names().ToHashSet(StringComparer.Ordinal);

        foreach (var property in typeof(VaultMetadata).GetProperties())
            Assert.Contains(property.Name, names);

        Assert.Equal(meta.Id, read.Id);
        Assert.Equal(meta.DisplayName, read.DisplayName);
        Assert.Equal(meta.AlternativeNames, read.AlternativeNames);
        Assert.Equal(meta.Summary, read.Summary);
        Assert.Equal(meta.Description, read.Description);
        Assert.Equal(meta.Images, read.Images);
        Assert.Equal(meta.Tags, read.Tags);
        Assert.Equal(meta.Source, read.Source);
        Assert.Equal(meta.Author, read.Author);
        Assert.Equal(meta.DateAdded, read.DateAdded);
        Assert.Equal(meta.GameVersion, read.GameVersion);
        Assert.Equal(meta.SchemaVersion, read.SchemaVersion);
    }

    [Fact]
    public void TheSourceFileIsRememberedSoAnItemCanBeReadAgain()
    {
        // What makes `reimport --from` possible. Two exports can slug to the same id - there
        // are two ships called Rasamama S36 - so the stored item has to say which file it came
        // from rather than the name being worked out again.
        var meta = new VaultMetadata
        {
            Id = "rasamama-s36-start",
            DisplayName = "Rasamama S36",
            Source = "[START] Rasamama S36.nmsship",
        };

        var stored = VaultItem.FromBytes(
            NmseImporter.Read(Read("starships", "[START] Rasamama S36.nmsship"), meta, ".nmsship").ToBytes());

        string source = Assert.IsType<string>(stored.Meta.Source);
        Assert.Equal("[START] Rasamama S36.nmsship", source);

        // The name only. Where the file sat is nobody else's business and would be wrong by
        // the next day anyway.
        Assert.DoesNotContain(Path.DirectorySeparatorChar, source);
        Assert.DoesNotContain('/', source);
    }

    [Fact]
    public void StoredVaultItem_StillRoundTripsToTheOriginalBytes()
    {
        // Metadata must be additive: attaching it and storing the item must not perturb
        // the payload, or the gallery would hand out files subtly different from the source.
        byte[] original = NormaliseLineEndings(Read("starships", "[EXP-13-R] Iron Vulture.nmsship"));

        var item = NmseImporter.Read(original, Meta("iron-vulture"), ".nmsship");
        var stored = VaultItem.FromBytes(item.ToBytes());

        Assert.Equal(original, Adapter.Export(stored).Content);
    }

    // --- Kind inference ---------------------------------------------------

    [Fact]
    public void BareMultitool_IsRecognisedWithoutAnExtensionHint()
    {
        var doc = JsonObject.FromBytes(Read("multitools", "[EXP-12-R] Atlas Sceptre.nmstool"));

        Assert.Equal(EntityKind.Multitool, NmseImporter.InferBareKind(doc, extensionHint: null));
    }

    [Fact]
    public void UnrecognisableBareObject_FailsLoudly()
    {
        // Better a clear error at ingestion than a mislabelled item in the gallery.
        var doc = JsonObject.Parse("""{ "Something": 1 }""");

        var ex = Assert.Throws<InvalidOperationException>(
            () => NmseImporter.InferBareKind(doc, extensionHint: null));
        Assert.Contains("Could not determine", ex.Message);
    }

    // --- Unsupported combinations -----------------------------------------

    [Fact]
    public void Freighter_ReportsWhyNmseCannotExpressIt()
    {
        var result = Adapter.Extension(EntityKind.Freighter);

        Assert.False(result.HasValue);
        Assert.Contains("base interior", result.Alternative.Reason);
    }

    [Theory]
    [InlineData(EntityKind.Starship, ".nmsship")]
    [InlineData(EntityKind.Multitool, ".nmstool")]
    [InlineData(EntityKind.Companion, ".nmspet")]
    [InlineData(EntityKind.Frigate, ".nmsfrig")]
    public void SupportedKinds_ReportTheirExtension(EntityKind kind, string expected)
    {
        var result = Adapter.Extension(kind);

        Assert.True(result.HasValue);
        Assert.Equal(expected, result.Value);
    }

    [Fact]
    public void ExportedFileName_UsesTheDisplayNameAndIsFilesystemSafe()
    {
        var meta = new VaultMetadata { Id = "x", DisplayName = "Rasamama S36: \"Gold\"/Mk2" };
        var item = NmseImporter.Read(Read("starships", "[START] Rasamama S36.nmsship"), meta, ".nmsship");

        string fileName = Adapter.Export(item).FileName;

        Assert.EndsWith(".nmsship", fileName);
        Assert.DoesNotContain(':', fileName);
        Assert.DoesNotContain('"', fileName);
        Assert.DoesNotContain('/', fileName);
    }
}
