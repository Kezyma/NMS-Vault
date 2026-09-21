using NmsVault.Core;
using NmsVault.Core.Adapters;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// Companion export and re-import, across all four formats.
/// </summary>
/// <remarks>
/// A companion had never been exported by any test. That is how the re-import bug survived:
/// a stored document fed back in came out with itself as its own payload, and the only way
/// to see it was to run the round trip.
/// </remarks>
public class CompanionExportTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");
    private static readonly JsonNameMapper Mapper = JsonNameMapper.LoadEmbedded();

    private const string Pet = "[EXP-23-R] Diplodocus.nmspet";

    private static VaultItem Load()
        => NmseImporter.Read(
            File.ReadAllBytes(Path.Combine(FixtureRoot, "companions", Pet)),
            new VaultMetadata { Id = "diplodocus", DisplayName = "Diplodocus" },
            ".nmspet");

    private static IEnumerable<IExportAdapter> Adapters() =>
    [
        new NmseExportAdapter(),
        new GoatfungusExportAdapter(),
        new CompanionExportAdapter(Mapper),
        new NomNomExportAdapter(Mapper),
    ];

    [Fact]
    public void ARawExportGivesThePetAloneAsThePayload()
    {
        var item = Load();

        Assert.Equal(EntityKind.Companion, item.Kind);
        Assert.Equal("^DIPLO_PET", item.Payload.GetString("CreatureID"));

        // The slots are a sidecar, not part of the creature.
        Assert.Null(item.Payload.GetArray("PetAccessoryCustomisation"));
        Assert.Equal(3, item.AccessorySlots?.Length);
    }

    [Fact]
    public void ReImportingAStoredDocumentDoesNotNestItInsideItself()
    {
        // What `convert --file gallery/items/<pet>.json` does. The stored document is
        // { Pet, PetAccessoryCustomisation, Vault }; feeding it back in used to produce
        // { Pet: { Pet: {...}, Vault: {...} } } - a nested wrapper and a metadata block
        // bound for somebody's real save.
        var stored = JsonObject.FromBytes(Load().ToBytes());

        var again = NmseImporter.Read(
            stored, new VaultMetadata { Id = "diplodocus", DisplayName = "Diplodocus" }, ".nmspet");

        Assert.Equal("^DIPLO_PET", again.Payload.GetString("CreatureID"));
        Assert.Null(again.Payload.GetObject("Pet"));
        Assert.Null(again.Payload.GetObject(VaultItem.VaultKey));
        Assert.Equal(3, again.AccessorySlots?.Length);
    }

    [Fact]
    public void ReImportingIsIdempotent()
    {
        // Twice round must equal once round, or every conversion drifts.
        var once = Load();
        var twice = NmseImporter.Read(
            JsonObject.FromBytes(once.ToBytes()),
            new VaultMetadata { Id = "diplodocus", DisplayName = "Diplodocus" }, ".nmspet");

        Assert.Equal(once.ToBytes(), twice.ToBytes());
    }

    [Theory]
    [InlineData(EditorId.Nmse)]
    [InlineData(EditorId.Goatfungus)]
    [InlineData(EditorId.Companion)]
    [InlineData(EditorId.NomNom)]
    public void EveryAdapterExportsACompanionWithoutThrowing(EditorId editor)
    {
        var adapter = Adapters().First(a => a.Editor == editor);
        var item = Load();

        Assert.True(adapter.Extension(EntityKind.Companion).HasValue,
            $"{adapter.DisplayName} reports it cannot write a companion at all");

        var result = adapter.Export(item);

        Assert.NotEmpty(result.Content);

        // Whatever it wrote has to be readable JSON; a half-written document is worse than
        // a refusal, because it reaches a save before anyone notices.
        var document = JsonObject.FromBytes(result.Content);
        Assert.NotEmpty(document.Names());
    }

    /// <summary>Builds a ship whose customisation is one non-default thing and nothing else.</summary>
    private static VaultItem ShipCustomisedOnlyBy(string preset, params string[] boneScales)
    {
        var custom = new JsonObject();
        custom.Set("PaletteID", "^");
        custom.Set("DescriptorGroups", new JsonArray());
        custom.Set("TextureOptions", new JsonArray());
        custom.Set("Colours", new JsonArray());

        var bones = new JsonArray();
        foreach (string b in boneScales) bones.Add(b);
        custom.Set("BoneScales", bones);

        var ccd = new JsonObject();
        ccd.Set("SelectedPreset", preset);
        ccd.Set("CustomData", custom);

        return VaultItem.Create(
            EntityKind.Starship, new JsonObject(),
            new VaultMetadata { Id = "t", DisplayName = "Test Ship" },
            characterCustomisationData: ccd);
    }

    [Theory]
    [InlineData(EditorId.Companion)]
    [InlineData(EditorId.NomNom)]
    public void APresetIsReportedAsLostRatherThanSilentlyDropped(EditorId editor)
    {
        // IsDefault counts a preset, so this ship is "customised" and the format carries only
        // the colours array - but neither adapter used to mention the preset, so LossesFor
        // came back empty and the dialog said nothing would be lost.
        var adapter = Adapters().First(a => a.Editor == editor);
        var losses = adapter.LossesFor(ShipCustomisedOnlyBy("^SOMEPRESET"));

        Assert.NotEmpty(losses);
        Assert.Contains(losses, l => l.Contains("preset", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(EditorId.Companion)]
    [InlineData(EditorId.NomNom)]
    public void AdjustedProportionsAreReportedAsLost(EditorId editor)
    {
        var adapter = Adapters().First(a => a.Editor == editor);
        var losses = adapter.LossesFor(ShipCustomisedOnlyBy("^", "1.5"));

        Assert.NotEmpty(losses);
        Assert.Contains(losses, l => l.Contains("proportions", StringComparison.OrdinalIgnoreCase));
    }

    [Theory]
    [InlineData(EditorId.Companion)]
    [InlineData(EditorId.NomNom)]
    public void ADefaultCustomisationReportsNoLoss(EditorId editor)
    {
        // The other side of the same coin: a blank block must not invent a warning.
        var adapter = Adapters().First(a => a.Editor == editor);

        Assert.Empty(adapter.LossesFor(ShipCustomisedOnlyBy("^")));
    }

    [Fact]
    public void KaiiObfuscatesTheAccessorySlotsAsWellAsTheCreature()
    {
        // Every other value in a Kaii document is obfuscated. Writing the slots readable
        // produces the half-obfuscated file KeyObfuscator's own docs call the failure mode -
        // and our importer tolerates it, so nothing else would catch this.
        var document = JsonObject.FromBytes(new CompanionExportAdapter(Mapper).Export(Load()).Content);
        var accessories = document.GetObject("AccessoryCustomisation") ?? document;

        string text = accessories.ToExportString();

        Assert.DoesNotContain("SelectedPreset", text);
        Assert.DoesNotContain("DescriptorGroups", text);
    }
}
