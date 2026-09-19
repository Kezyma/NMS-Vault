using System.Text;
using NmsVault.Core;
using NmsVault.Core.Adapters;
using NmsVault.Core.Derived;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// Tests for the goatfungus, NMS Companion and NomNom adapters.
/// <para>
/// None of those editors can load a 7.03 Cosmos save, so none of this output has been
/// confirmed by importing it. These tests assert what we can actually check: that the
/// documented shape is produced, that obfuscation covers the data, and that what each
/// format cannot carry is reported rather than silently dropped.
/// </para>
/// </summary>
public class AdapterTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");
    private static readonly JsonNameMapper Mapper = JsonNameMapper.LoadEmbedded();

    private static VaultItem LoadShip(string name, string display = "Test Ship")
        => NmseImporter.Read(
            File.ReadAllBytes(Path.Combine(FixtureRoot, "starships", name)),
            new VaultMetadata { Id = "t", DisplayName = display, Description = "A test ship." },
            ".nmsship");

    private static VaultItem LoadTool(string name)
        => NmseImporter.Read(
            File.ReadAllBytes(Path.Combine(FixtureRoot, "multitools", name)),
            new VaultMetadata { Id = "t", DisplayName = "Test Tool" },
            ".nmstool");

    private static JsonObject Parse(ExportResult r) => JsonObject.FromBytes(r.Content);

    private const string CustomisedShip = "[EXP-13-R] Iron Vulture.nmsship";
    private const string PlainShip = "[START] Rasamama S36.nmsship";

    // --- Every adapter declares its verification status ----------------

    [Fact]
    public void OnlyNmseClaimsToBeVerified()
    {
        // The others cannot be verified against a real editor yet, and the UI depends on
        // this flag to say so honestly.
        Assert.True(new NmseExportAdapter().IsVerified);
        Assert.False(new GoatfungusExportAdapter().IsVerified);
        Assert.False(new CompanionExportAdapter(Mapper).IsVerified);
        Assert.False(new NomNomExportAdapter(Mapper).IsVerified);
    }

    // --- goatfungus: the bare object ----------------------------------

    [Fact]
    public void Goatfungus_WritesTheBareObjectWithReadableKeys()
    {
        var result = new GoatfungusExportAdapter().Export(LoadShip(PlainShip));
        var root = Parse(result);

        Assert.EndsWith(".sh0", result.FileName);
        // No wrapper: the ship's own keys are at root.
        Assert.True(root.Contains("Resource"));
        Assert.True(root.Contains("Inventory"));
        Assert.False(root.Contains("Ship"));
        // Readable, not obfuscated.
        Assert.False(root.Contains("@Cs"));
    }

    [Fact]
    public void Goatfungus_ReportsEverythingItCannotCarry()
    {
        var losses = new GoatfungusExportAdapter().LossesFor(LoadShip(CustomisedShip));

        Assert.Contains(losses, l => l.Contains("legacy colours"));
        Assert.Contains(losses, l => l.Contains("no customisation at all"));

        // Whole sentences saying what will happen in-game, not field names. A warning reading
        // "DescriptorGroups" tells nobody anything.
        Assert.All(losses, l => Assert.EndsWith(".", l, StringComparison.Ordinal));
    }

    [Fact]
    public void Goatfungus_HasNoFrigateFormat()
    {
        var result = new GoatfungusExportAdapter().Extension(EntityKind.Frigate);

        Assert.False(result.HasValue);
        Assert.Contains("no frigate format", result.Alternative.Reason);
    }

    // --- NMS Companion (Kaii) -----------------------------------------

    [Fact]
    public void Companion_NestsTheShipUnderObfuscatedKeys()
    {
        var result = new CompanionExportAdapter(Mapper).Export(LoadShip(PlainShip));
        var root = Parse(result);

        Assert.EndsWith(".shp", result.FileName);

        // Readable envelope...
        Assert.True(root.Contains("Ship"));
        Assert.True(root.Contains("Colours"));
        Assert.Equal(1, root.Get("FileVersion"));

        // ...wrapping the hardcoded obfuscated pair libNOM writes.
        var ship = root.GetObject("Ship");
        Assert.NotNull(ship);
        Assert.True(ship!.Contains("@Cs"), "ship should be nested under @Cs (ShipOwnership)");
        Assert.True(ship.Contains("4hl"), "legacy colour flag should be under 4hl");
    }

    [Fact]
    public void Companion_ObfuscatesThePayload()
    {
        var result = new CompanionExportAdapter(Mapper).Export(LoadShip(PlainShip));
        var ship = Parse(result).GetObject("Ship")!.GetObject("@Cs");

        Assert.NotNull(ship);
        // Resource obfuscates to @ff in the 7.03 table; assert via the mapper rather than
        // hardcoding, so a table refresh does not break the test spuriously.
        Assert.True(ship!.Contains(Mapper.ToKey("Resource")));
        Assert.False(ship.Contains("Resource"));
    }

    [Fact]
    public void Companion_CarriesTheLegacyColourFlag()
    {
        // The flag is the whole reason this project started; it must survive to every
        // format that has somewhere to put it.
        var legacy = Parse(new CompanionExportAdapter(Mapper)
            .Export(LoadShip("[PRE-PC] Horizon Omega (Original).nmsship")));
        var modern = Parse(new CompanionExportAdapter(Mapper)
            .Export(LoadShip("[PRE-PC] Horizon Omega (New).nmsship")));

        Assert.Equal(true, legacy.GetObject("Ship")!.Get("4hl"));
        Assert.Equal(false, modern.GetObject("Ship")!.Get("4hl"));
    }

    [Fact]
    public void Companion_WritesSixThumbnailSlotsWithNoThumbnail1()
    {
        byte[] png = [0x89, 0x50, 0x4E, 0x47];
        var result = new CompanionExportAdapter(Mapper)
            .Export(LoadShip(PlainShip), new ExportOptions([png]));
        var root = Parse(result);

        Assert.Equal(Convert.ToBase64String(png), root.GetString("Thumbnail"));
        // libNOM writes Thumbnail then Thumbnail2 - there is deliberately no Thumbnail1.
        Assert.False(root.Contains("Thumbnail1"));
        foreach (int slot in (int[])[2, 3, 4, 5, 6])
            Assert.True(root.Contains($"Thumbnail{slot}"), $"missing Thumbnail{slot}");
        Assert.Null(root.Get("Thumbnail2"));
    }

    [Fact]
    public void Companion_ReportsLostPartsForACustomisedShip()
    {
        // Iron Vulture has DescriptorGroups, TextureOptions and a named palette. This
        // format carries CustomData.Colours only, so all three are lost.
        var losses = new CompanionExportAdapter(Mapper).LossesFor(LoadShip(CustomisedShip));

        Assert.Contains(losses, l => l.Contains("custom parts"));
        Assert.Contains(losses, l => l.Contains("texture"));
        Assert.Contains(losses, l => l.Contains("SHIP_METALLIC"));
    }

    [Fact]
    public void Companion_ReportsNoLossForAnUncustomisedShip()
    {
        Assert.Empty(new CompanionExportAdapter(Mapper).LossesFor(LoadShip(PlainShip)));
    }

    [Fact]
    public void Companion_UsesCapitalTInMultiTool()
    {
        // Kaii spells it MultiTool; NomNom spells it Multitool. Mixing them up produces a
        // file the editor cannot read.
        var root = Parse(new CompanionExportAdapter(Mapper)
            .Export(LoadTool("[EXP-12-R] Atlas Sceptre.nmstool")));

        Assert.True(root.Contains("MultiTool"));
        Assert.False(root.Contains("Multitool"));
    }

    // --- NomNom (Standard) --------------------------------------------

    [Fact]
    public void NomNom_WritesTheStandardEnvelope()
    {
        var result = new NomNomExportAdapter(Mapper).Export(LoadShip(PlainShip));
        var root = Parse(result);

        Assert.EndsWith(".shp", result.FileName);
        Assert.Equal(2, root.Get("FileVersion"));
        foreach (var key in (string[])["Data", "DateCreated", "Description", "Preview", "Starred"])
            Assert.True(root.Contains(key), $"missing {key}");

        var data = root.GetObject("Data");
        Assert.NotNull(data);
        // Data sub-keys stay readable; only the values beneath are obfuscated.
        Assert.True(data!.Contains("Ship"));
        Assert.True(data.Contains("UseLegacyColours"));
        Assert.True(data.Contains("Colours"));
        Assert.True(data.Contains("PersistentPlayerBases"));
    }

    [Fact]
    public void NomNom_UsesItsOwnSpellingOfTheLegacyColourKey()
    {
        var data = Parse(new NomNomExportAdapter(Mapper)
            .Export(LoadShip("[PRE-PS] Alpha Vector (Original).nmsship"))).GetObject("Data")!;

        // UseLegacyColours, not UsesLegacyColours - the missing s is NMSE's spelling.
        Assert.Equal(true, data.Get("UseLegacyColours"));
        Assert.False(data.Contains("UsesLegacyColours"));
    }

    [Fact]
    public void NomNom_DateCreatedIsDeterministicWhenMetadataSuppliesIt()
    {
        var item = NmseImporter.Read(
            File.ReadAllBytes(Path.Combine(FixtureRoot, "starships", PlainShip)),
            new VaultMetadata
            {
                Id = "t",
                DisplayName = "T",
                DateAdded = new DateTimeOffset(2026, 1, 2, 3, 4, 5, TimeSpan.Zero),
            },
            ".nmsship");

        var root = Parse(new NomNomExportAdapter(Mapper).Export(item));

        Assert.StartsWith("2026-01-02T03:04:05", root.GetString("DateCreated"));
    }

    [Fact]
    public void NomNom_DerivesMultitoolTypeInItsOwnVocabulary()
    {
        // No NMSE fixture carries a Type field, so it is derived - and then translated, because
        // NomNom's enum spells things differently. Atlas Sceptre uses STAFFMULTITOOLATLAS,
        // which the gallery shows as "Voltaic Staff" and NomNom calls StaffAtlas.
        var root = Parse(new NomNomExportAdapter(Mapper)
            .Export(LoadTool("[EXP-12-R] Atlas Sceptre.nmstool")));

        Assert.Equal("StaffAtlas", root.GetObject("Data")!.GetString("Type"));
    }

    [Fact]
    public void UnrolledToolsLandInRifleC()
    {
        // Both shared-model fixtures are scripted tools with every stat written as an explicit
        // 0.0 - not absent, actually zero. That is inside Rifle C (damage 0-5, mining 0, scan
        // 0-5) and inside nothing else, so the table answers Rifle for them.
        //
        // This is a property of the table rather than of these two files: any C-class tool that
        // has rolled nothing reads as a rifle, because zero is a legal rifle roll and is not a
        // legal pistol one. Pinned here so the consequence stays visible.
        foreach (var name in (string[])["[START] Waveform Focuser N56-P.nmstool",
                                        "[EXP-23-S] Iselovke-risho v0.27.nmstool"])
        {
            var tool = LoadTool(name);

            Assert.Equal(0.0, ItemStats.Read(tool.Payload.GetObject("Store"), "^WEAPON_DAMAGE"));
            Assert.Equal(0.0, ItemStats.Read(tool.Payload.GetObject("Store"), "^WEAPON_MINING"));
            Assert.Equal(0.0, ItemStats.Read(tool.Payload.GetObject("Store"), "^WEAPON_SCAN"));

            Assert.Equal("Rifle", MultitoolTypes.FromMultitool(tool.Payload));
            Assert.Equal("Rifle", Parse(new NomNomExportAdapter(Mapper).Export(tool))
                .GetObject("Data")!.GetString("Type"));
        }
    }

    [Fact]
    public void NomNom_IsTheOnlyThirdPartyFormatThatKeepsACorvetteBase()
    {
        // No corvette fixture exists yet, so synthesise one to exercise the path.
        var item = VaultItem.Create(
            EntityKind.Starship,
            JsonObject.Parse("""{ "Name": "Corvette", "Resource": { "Filename": "BIGGS.SCENE.MBIN" } }"""),
            new VaultMetadata { Id = "c", DisplayName = "Corvette" },
            shipBase: JsonObject.Parse("""{ "BaseType": { "PersistentBaseTypes": "PlayerShipBase" }, "Objects": [] }"""));

        Assert.NotNull(Parse(new NomNomExportAdapter(Mapper).Export(item))
            .GetObject("Data")!.GetObject("PersistentPlayerBases"));

        Assert.Contains(new CompanionExportAdapter(Mapper).LossesFor(item),
            l => l.Contains("corvette"));
        Assert.Contains(new GoatfungusExportAdapter().LossesFor(item),
            l => l.Contains("corvette"));
    }

    // --- The staleness trap that killed the libNOM dependency ---------

    [Theory]
    [InlineData("starships", PlainShip)]
    [InlineData("starships", CustomisedShip)]
    [InlineData("starships", "[EXP-23-R] Vintage Interceptor.nmsship")]
    [InlineData("starships", "[DLC] Starborn Phoenix.nmsship")]
    [InlineData("multitools", "[EXP-12-R] Atlas Sceptre.nmstool")]
    public void OurMappingTableCoversRealCosmosData(string folder, string name)
    {
        // The reason libNOM was dropped: its table maps for NMS 5.5, and unmapped keys pass
        // through readable rather than erroring, so it would emit half-obfuscated files.
        // This asserts our table has no such gap for real 7.03 payloads.
        //
        // Checks the payload and its sidecars, not the whole document: the wrapper keys are
        // the export format's own invention, not game keys, and are deliberately absent from
        // the table - see WrapperKeysAreNotGameKeys below.
        var item = folder == "starships"
            ? LoadShip(name)
            : LoadTool(name);

        Assert.Equal(0, KeyObfuscator.CountUnmapped(item.Payload, Mapper));

        if (item.CharacterCustomisationData is { } ccd)
            Assert.Equal(0, KeyObfuscator.CountUnmapped(ccd, Mapper));
    }

    [Fact]
    public void WrapperKeysAreNotGameKeys()
    {
        // Ship, Base and Vault are the export format's own scaffolding, so the mapping
        // table has no entry for them - correctly, since obfuscating them would be
        // meaningless. Documented as a test so nobody "fixes" the table by adding them.
        foreach (var key in (string[])["Ship", "Base", VaultItem.VaultKey])
        {
            Assert.Equal(key, Mapper.ToKey(key));
            Assert.False(Mapper.IsObfuscatedKey(key));
        }

        // CharacterCustomisationData and UsesLegacyColours, by contrast, ARE real game keys
        // that happen to sit at wrapper level.
        Assert.NotEqual("CharacterCustomisationData", Mapper.ToKey("CharacterCustomisationData"));
        Assert.NotEqual("ShipUsesLegacyColours", Mapper.ToKey("ShipUsesLegacyColours"));
    }

    [Fact]
    public void CountUnmapped_ActuallyDetectsAGap()
    {
        // Guard against the test above passing because the counter is broken rather than
        // because the table is complete.
        var withJunk = JsonObject.Parse("""{ "Resource": { "NotARealGameKey": 1 } }""");

        Assert.Equal(1, KeyObfuscator.CountUnmapped(withJunk, Mapper));
    }

    // --- Obfuscation round trip ---------------------------------------

    [Fact]
    public void ObfuscatedPayload_DeobfuscatesBackToTheOriginal()
    {
        var original = LoadShip(PlainShip).Payload;

        var obfuscated = KeyObfuscator.Obfuscate(original, Mapper);
        // Re-parse with auto-detect, which is how a reader would encounter the file.
        var recovered = JsonObject.FromBytes(
            Encoding.Latin1.GetBytes(obfuscated.ToExportString()), Mapper);

        Assert.Equal(original.ToExportString(), recovered.ToExportString());
    }

    [Fact]
    public void Obfuscate_DoesNotMutateTheSource()
    {
        var item = LoadShip(PlainShip);
        string before = item.Payload.ToExportString();

        KeyObfuscator.Obfuscate(item.Payload, Mapper);

        Assert.Equal(before, item.Payload.ToExportString());
    }
}
