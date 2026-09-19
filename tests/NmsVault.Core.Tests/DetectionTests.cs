using System.Text;
using NmsVault.Core;
using NmsVault.Core.Adapters;
using NmsVault.Core.Detection;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// Tests for format detection and the universal importer.
/// <para>
/// The NMSE cases run against real files. The NMS Companion and NomNom cases round-trip
/// through our own adapters, which proves adapter and importer are inverses but cannot prove
/// either matches what those editors actually write - they can't load a Cosmos save.
/// </para>
/// </summary>
public class DetectionTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");
    private static readonly JsonNameMapper Mapper = JsonNameMapper.LoadEmbedded();
    private static readonly VaultImporter Importer = new(Mapper);

    private static VaultMetadata Meta => new() { Id = "t", DisplayName = "Test" };

    public static TheoryData<string, string> AllFixtures()
    {
        var data = new TheoryData<string, string>();
        foreach (var folder in (string[])["starships", "multitools"])
            foreach (var path in Directory.EnumerateFiles(Path.Combine(FixtureRoot, folder)))
                data.Add(folder, Path.GetFileName(path));
        return data;
    }

    private static byte[] Read(string folder, string name)
        => File.ReadAllBytes(Path.Combine(FixtureRoot, folder, name));

    // --- Real files ----------------------------------------------------

    [Theory]
    [MemberData(nameof(AllFixtures))]
    public void EveryFixtureIsDetectedAsNmseWithTheRightKind(string folder, string name)
    {
        var result = FormatDetector.Detect(Read(folder, name), Mapper, name);

        Assert.Equal(SourceFormat.Nmse, result.Format);
        Assert.Equal(folder == "starships" ? EntityKind.Starship : EntityKind.Multitool, result.Kind);
        Assert.Equal(KeySpace.Readable, result.Keys);
    }

    [Fact]
    public void ShipWrapperIsDetectedWithCertaintyEvenWithoutAFileName()
    {
        // Content-first: the wrapper plus UsesLegacyColours is unambiguous on its own.
        var result = FormatDetector.Detect(
            Read("starships", "[START] Rasamama S36.nmsship"), Mapper, fileName: null);

        Assert.Equal(SourceFormat.Nmse, result.Format);
        Assert.Equal(Certainty.Certain, result.Certainty);
    }

    [Fact]
    public void BareMultitoolWithoutAFileNameIsOnlyAGuessAtTheEditor()
    {
        // NMSE .nmstool and goatfungus .wp0 are byte-identical for multitools, so with no
        // extension the file genuinely cannot say which wrote it. Reported honestly rather
        // than picked arbitrarily.
        var result = FormatDetector.Detect(
            Read("multitools", "[EXP-12-R] Atlas Sceptre.nmstool"), Mapper, fileName: null);

        Assert.Equal(SourceFormat.NmseOrGoatfungus, result.Format);
        Assert.Equal(EntityKind.Multitool, result.Kind);
        Assert.Equal(Certainty.Guess, result.Certainty);
    }

    [Fact]
    public void GoatfungusExtensionNamesGoatfungus()
    {
        var result = FormatDetector.Detect(
            Read("multitools", "[EXP-12-R] Atlas Sceptre.nmstool"), Mapper, "whatever.wp0");

        Assert.Equal(SourceFormat.Goatfungus, result.Format);
    }

    // --- Non-JSON and out-of-scope input ------------------------------

    [Fact]
    public void ZipMagicIsRecognisedAsTheModelIoTool()
    {
        // NMSE's .nmsship and the Model IO Tool's ZIP share an extension; only the PK header
        // separates them.
        byte[] zip = [0x50, 0x4B, 0x03, 0x04, 0x00, 0x00];

        var result = FormatDetector.Detect(zip, Mapper, "ship.nmsship");

        Assert.Equal(SourceFormat.ModelIoToolZip, result.Format);
        Assert.Equal(Certainty.Certain, result.Certainty);
    }

    [Fact]
    public void ModelIoToolZipImportFailsWithAUsefulMessage()
    {
        byte[] zip = [0x50, 0x4B, 0x03, 0x04];

        var ex = Assert.Throws<ImportException>(() => Importer.Import(zip, Meta, "ship.nmsship"));

        Assert.Contains("Model IO Tool", ex.Message);
        Assert.Contains("NMSE", ex.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("not json at all")]
    [InlineData("{ \"unclosed\": ")]
    [InlineData("[1, 2, 3]")]
    public void GarbageIsUnrecognisedRatherThanThrowing(string text)
    {
        var result = FormatDetector.Detect(Encoding.UTF8.GetBytes(text), Mapper, "x.json");

        Assert.Equal(SourceFormat.Unknown, result.Format);
    }

    [Fact]
    public void ABaseEntryIsRejectedWithAnExplanation()
    {
        // A PersistentPlayerBases entry is a legitimate NMSE export but not a gallery item,
        // and freighter interiors are explicitly out of scope.
        var baseEntry = JsonObject.Parse("""
            { "Objects": [], "BaseType": { "PersistentBaseTypes": "FreighterBase" } }
            """);

        var result = FormatDetector.Detect(
            Encoding.Latin1.GetBytes(baseEntry.ToExportString()), Mapper, "x.nmsfreight");

        Assert.Equal(SourceFormat.Unknown, result.Format);
        Assert.Contains("FreighterBase", result.Reason);
        Assert.Contains("out of scope", result.Reason);
    }

    [Fact]
    public void UnknownFileVersionIsRejected()
    {
        var odd = JsonObject.Parse("""{ "FileVersion": 99, "Data": { "Ship": {} } }""");

        var result = FormatDetector.Detect(
            Encoding.Latin1.GetBytes(odd.ToExportString()), Mapper, "x.shp");

        Assert.Equal(SourceFormat.Unknown, result.Format);
        Assert.Contains("FileVersion 99", result.Reason);
    }

    // --- The stale-table trap -----------------------------------------

    [Fact]
    public void AMixedKeySpaceIsDetectedAndRefused()
    {
        // This is what a writer with an out-of-date mapping table produces: some keys
        // obfuscated, some left readable. Storing it would propagate the corruption into
        // every format converted from it, so ingestion refuses it.
        var mixed = new JsonObject();
        var data = new JsonObject();
        var ship = new JsonObject();
        ship.Set(Mapper.ToKey("Resource"), new JsonObject());   // obfuscated
        ship.Set("Inventory", new JsonObject());                // left readable
        data.Set("Ship", ship);
        mixed.Set("Data", data);
        mixed.Set("FileVersion", 2);

        byte[] bytes = Encoding.Latin1.GetBytes(mixed.ToExportString());

        Assert.Equal(KeySpace.Mixed, FormatDetector.Detect(bytes, Mapper, "x.shp").Keys);

        var ex = Assert.Throws<ImportException>(() => Importer.Import(bytes, Meta, "x.shp"));
        Assert.Contains("mixed key space", ex.Message);
        Assert.Contains("out-of-date", ex.Message);
    }

    // --- Adapter and importer are inverses ----------------------------

    [Theory]
    [InlineData("[START] Rasamama S36.nmsship")]
    [InlineData("[EXP-13-R] Iron Vulture.nmsship")]
    public void NomNomExport_IsDetectedAndImportedBack(string name)
    {
        var original = NmseImporter.Read(Read("starships", name), Meta, ".nmsship");

        var exported = new NomNomExportAdapter(Mapper).Export(original);
        var detected = FormatDetector.Detect(exported.Content, Mapper, exported.FileName);

        Assert.Equal(SourceFormat.NomNom, detected.Format);
        Assert.Equal(EntityKind.Starship, detected.Kind);
        Assert.Equal(KeySpace.Obfuscated, detected.Keys);

        var reimported = Importer.Import(exported.Content, Meta, exported.FileName);

        // The payload survives exactly; the flag survives too.
        Assert.Equal(original.Payload.ToExportString(), reimported.Payload.ToExportString());
        Assert.Equal(original.UsesLegacyColours, reimported.UsesLegacyColours);
    }

    [Theory]
    [InlineData("[START] Rasamama S36.nmsship")]
    [InlineData("[PRE-PC] Horizon Omega (Original).nmsship")]
    public void CompanionFormatExport_IsDetectedAndImportedBack(string name)
    {
        var original = NmseImporter.Read(Read("starships", name), Meta, ".nmsship");

        var exported = new CompanionExportAdapter(Mapper).Export(original);
        var detected = FormatDetector.Detect(exported.Content, Mapper, exported.FileName);

        Assert.Equal(SourceFormat.Companion, detected.Format);
        Assert.Equal(EntityKind.Starship, detected.Kind);

        var reimported = Importer.Import(exported.Content, Meta, exported.FileName);

        Assert.Equal(original.Payload.ToExportString(), reimported.Payload.ToExportString());
        Assert.Equal(original.UsesLegacyColours, reimported.UsesLegacyColours);
    }

    [Fact]
    public void MultitoolSurvivesEveryFormatRoundTrip()
    {
        var original = NmseImporter.Read(
            Read("multitools", "[EXP-12-R] Atlas Sceptre.nmstool"), Meta, ".nmstool");

        foreach (IExportAdapter adapter in (IExportAdapter[])[
            new NmseExportAdapter(), new GoatfungusExportAdapter(),
            new CompanionExportAdapter(Mapper), new NomNomExportAdapter(Mapper)])
        {
            var exported = adapter.Export(original);
            var reimported = Importer.Import(exported.Content, Meta, exported.FileName);

            Assert.Equal(EntityKind.Multitool, reimported.Kind);
            Assert.Equal(original.Payload.ToExportString(), reimported.Payload.ToExportString());
        }
    }

    [Fact]
    public void RoundTrippingThroughALossyFormatLosesExactlyWhatWasAdvertised()
    {
        // Iron Vulture has parts, a texture option and a named palette. Going out to NomNom
        // and back must lose those and keep the colours - matching what LossesFor promised,
        // so the warning and the behaviour cannot drift apart.
        var original = NmseImporter.Read(
            Read("starships", "[EXP-13-R] Iron Vulture.nmsship"), Meta, ".nmsship");

        var exported = new NomNomExportAdapter(Mapper).Export(original);
        var reimported = Importer.Import(exported.Content, Meta, exported.FileName);

        var before = original.CharacterCustomisationData!.GetObject("CustomData")!;
        var after = reimported.CharacterCustomisationData!.GetObject("CustomData")!;

        // Colours kept, byte for byte.
        Assert.Equal(before.GetArray("Colours")!.ToString(), after.GetArray("Colours")!.ToString());

        // Parts, texture and palette gone - exactly the three losses reported.
        Assert.Equal(3, before.GetArray("DescriptorGroups")!.Length);
        Assert.Equal(0, after.GetArray("DescriptorGroups")!.Length);
        Assert.Equal(1, before.GetArray("TextureOptions")!.Length);
        Assert.Equal(0, after.GetArray("TextureOptions")!.Length);
        Assert.Equal("^SHIP_METALLIC", before.GetString("PaletteID"));
        Assert.Equal("^", after.GetString("PaletteID"));
    }

    // --- Slugs are permalinks -----------------------------------------

    [Theory]
    [InlineData("Iron Vulture", "iron-vulture")]
    [InlineData("[EXP-13-R] Iron Vulture", "exp-13-r-iron-vulture")]
    [InlineData("Rasamama S36: \"Gold\"/Mk2", "rasamama-s36-gold-mk2")]
    [InlineData("  spaced  out  ", "spaced-out")]
    [InlineData("---", "item")]
    [InlineData("", "item")]
    [InlineData("Nöstromo", "n-stromo")]
    public void SlugsAreStableAndUrlSafe(string input, string expected)
    {
        // Ids are permalinks, so this must be total and must not change its mind between
        // versions - hence deliberately dumb ASCII folding rather than transliteration.
        Assert.Equal(expected, Slug.From(input));
    }
}
