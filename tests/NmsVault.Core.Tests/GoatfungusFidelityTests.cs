using System.Text;
using NmsVault.Core.Adapters;
using NmsVault.Core.Detection;
using NmsVault.Json;

namespace NmsVault.Core.Tests;

/// <summary>
/// Checks our goatfungus output against files the real editor wrote.
/// </summary>
/// <remarks>
/// <para>
/// The fixtures in <c>tests/fixtures/goatfungus</c> came out of NMSSaveEditor itself. They
/// are not exports of the same entities as the NMSE fixtures, so the two sets cannot be
/// compared directly - what can be compared is a round trip: read a real export, and write
/// it back out. Anything our reader drops or our writer spells differently shows up as a
/// byte that does not match.
/// </para>
/// <para>
/// This is the strongest check available without the editor being able to open a 7.03 save.
/// It proves the file shape, the key order, the number formatting and the whitespace; it
/// cannot prove the editor accepts the result, which is why <see cref="IExportAdapter"/>
/// still reports this format unverified.
/// </para>
/// </remarks>
public class GoatfungusFidelityTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "goatfungus");

    private static readonly VaultMetadata Meta = new() { Id = "x", DisplayName = "x" };

    public static TheoryData<string> Exports =>
        ["Rasamama_S36.sh0", "Prime_Vector.sh0", "Ship.sh0", "Iselovke-risho_v027.wp0", "Weapon.wp0"];

    [Theory]
    [MemberData(nameof(Exports))]
    public void ARealExportSurvivesBeingReadAndWrittenBack(string name)
    {
        byte[] theirs = File.ReadAllBytes(Path.Combine(FixtureRoot, name));

        var item = new VaultImporter(JsonNameMapper.LoadEmbedded()).Import(theirs, Meta, name);
        byte[] ours = new GoatfungusExportAdapter().Export(item).Content;

        Assert.Equal(
            Encoding.Latin1.GetString(theirs),
            Encoding.Latin1.GetString(ours));
    }

    [Theory]
    [MemberData(nameof(Exports))]
    public void ARealExportIsOnOneLine(string name)
    {
        // The reason the writer minifies. goatfungus exports with Newtonsoft's default
        // formatting, which is none at all; NMSE indents its own files and this editor does
        // not, so the two adapters cannot share a writer.
        string text = Encoding.Latin1.GetString(File.ReadAllBytes(Path.Combine(FixtureRoot, name)));

        Assert.DoesNotContain('\n', text);
        Assert.DoesNotContain("\": ", text, StringComparison.Ordinal);
    }

    [Theory]
    [MemberData(nameof(Exports))]
    public void ARealExportIsDetectedAsGoatfungus(string name)
    {
        var detected = FormatDetector.Detect(
            File.ReadAllBytes(Path.Combine(FixtureRoot, name)), JsonNameMapper.LoadEmbedded(), name);

        Assert.Equal(SourceFormat.Goatfungus, detected.Format);
    }

    [Fact]
    public void TheKeysARealExportCarriesAreTheOnesWeWrite()
    {
        // Named separately from the round trip so a change to either side says which side
        // moved. The order is part of it: goatfungus writes the save's own order.
        var ship = JsonObject.Parse(Encoding.Latin1.GetString(
            File.ReadAllBytes(Path.Combine(FixtureRoot, "Rasamama_S36.sh0"))));

        Assert.Equal(
            ["Name", "Resource", "Inventory", "Inventory_Cargo", "Inventory_TechOnly",
             "InventoryLayout", "Location", "Position", "Direction", "VehicleCargo"],
            ship.Names());

        var tool = JsonObject.Parse(Encoding.Latin1.GetString(
            File.ReadAllBytes(Path.Combine(FixtureRoot, "Iselovke-risho_v027.wp0"))));

        Assert.Equal(
            ["Layout", "Store", "ScreenData", "Seed", "CustomisationData", "Name", "IsLarge",
             "PrimaryMode", "SecondaryMode", "UseLegacyColours", "Resource"],
            tool.Names());
    }
}
