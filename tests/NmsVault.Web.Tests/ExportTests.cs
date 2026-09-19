using System.Net;
using System.Text;
using NmsVault.Core;
using NmsVault.Core.Adapters;
using NmsVault.Json;
using NmsVault.Web.Services;

namespace NmsVault.Web.Tests;

/// <summary>
/// Tests for the download menu's data: which formats an item is offered in, what each one
/// says it will lose, and that the bytes handed to the browser are the adapter's own.
/// </summary>
/// <remarks>
/// The service is what the gallery actually runs - the component around it only draws the
/// answer - so these run against real fixture documents over a stubbed transport rather than
/// against mocked options.
/// </remarks>
public class ExportTests
{
    private static string FixtureRoot => Path.Combine(AppContext.BaseDirectory, "fixtures", "nmse");

    private const string Ship = "[PRE-PS] Alpha Vector (Original).nmsship";
    private const string Customised = "[EXP-13-R] Iron Vulture.nmsship";
    private const string Tool = "[EXP-12-R] Atlas Sceptre.nmstool";

    /// <summary>
    /// Serves one vault document, and nothing else. Anything the service asks for that was
    /// not put here comes back 404, which is how the missing-image path gets exercised.
    /// </summary>
    private sealed class OneItem(string id, byte[] document) : HttpMessageHandler
    {
        /// <summary>How many times the document was actually fetched.</summary>
        public int Fetches { get; private set; }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri?.AbsolutePath.EndsWith($"/gallery/items/{id}.json", StringComparison.Ordinal) == true)
            {
                Fetches++;
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = new ByteArrayContent(document),
                });
            }

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private static (ExportService Service, OneItem Transport) ServiceFor(
        string folder, string file, string extension, string id = "item")
    {
        var item = NmseImporter.Read(
            File.ReadAllBytes(Path.Combine(FixtureRoot, folder, file)),
            new VaultMetadata { Id = id, DisplayName = "Test Item", Images = ["img/missing.webp"] },
            extension);

        var transport = new OneItem(id, Encoding.Latin1.GetBytes(item.ToJson().ToExportString()));

        return (new ExportService(new HttpClient(transport) { BaseAddress = new Uri("https://example.test/") }),
                transport);
    }

    [Fact]
    public async Task EveryEditorIsOfferedForAShip()
    {
        var (service, _) = ServiceFor("starships", Ship, ".nmsship");

        var options = await service.OptionsAsync("item");

        Assert.Equal(
            [EditorId.Nmse, EditorId.NomNom, EditorId.Goatfungus, EditorId.Companion],
            options.Select(o => o.Editor));

        Assert.All(options, o => Assert.True(o.IsAvailable));
    }

    [Fact]
    public async Task TheMenuCarriesEachFormatsVerificationStatus()
    {
        // Not shown anywhere at the moment, but it is what the adapter says and the service
        // must not quietly flatten it. NMSE and goatfungus have real files behind them; the
        // other two are written from libNOM's writers and nothing more.
        var (service, _) = ServiceFor("starships", Ship, ".nmsship");

        var options = await service.OptionsAsync("item");

        Assert.Equal(
            [EditorId.Nmse, EditorId.Goatfungus],
            options.Where(o => o.IsVerified).Select(o => o.Editor).Order());
    }

    [Fact]
    public async Task NmseNeverAsksBecauseItLosesNothing()
    {
        // The vault format is NMSE's own export shape with a metadata block added, so there is
        // nothing for the NMSE adapter to drop. If this ever fails, the vault format has grown
        // a field that NMSE cannot carry and the gallery owes the reader a warning about it.
        foreach (var (folder, file, extension) in
                 ((string, string, string)[])[("starships", Ship, ".nmsship"),
                                              ("starships", Customised, ".nmsship"),
                                              ("multitools", Tool, ".nmstool")])
        {
            var (service, _) = ServiceFor(folder, file, extension);

            var nmse = (await service.OptionsAsync("item")).Single(o => o.Editor == EditorId.Nmse);

            Assert.Empty(nmse.Losses);
            Assert.False(nmse.NeedsConfirming);
        }
    }

    [Fact]
    public async Task AFormatThatDropsSomethingSaysWhatAndAsksFirst()
    {
        // Iron Vulture carries full customisation. goatfungus keeps none of it.
        var (service, _) = ServiceFor("starships", Customised, ".nmsship");

        var goatfungus = (await service.OptionsAsync("item")).Single(o => o.Editor == EditorId.Goatfungus);

        Assert.NotEmpty(goatfungus.Losses);
        Assert.True(goatfungus.NeedsConfirming);

        // Sentences for a reader, not field names. A warning naming "DescriptorGroups" tells
        // nobody anything.
        Assert.All(goatfungus.Losses, loss => Assert.True(loss.Length > 12));
    }

    [Fact]
    public async Task AnEditorWithNoFormatForTheKindIsDisabledWithItsReason()
    {
        // goatfungus has no frigate format at all. No frigate fixture exists yet, so the kind
        // is set directly - the point under test is the menu's handling, not the import.
        var frigate = VaultItem.Create(
            EntityKind.Frigate,
            JsonObject.Parse("""{ "Resource": { "Filename": "FRIGATE.SCENE.MBIN" } }"""),
            new VaultMetadata { Id = "f", DisplayName = "Frigate" });

        var service = new ExportService(new HttpClient(new OneItem("f", Encoding.Latin1.GetBytes(frigate.ToJson().ToExportString())))
        {
            BaseAddress = new Uri("https://example.test/"),
        });

        var option = (await service.OptionsAsync("f")).Single(o => o.Editor == EditorId.Goatfungus);

        Assert.False(option.IsAvailable);
        Assert.Null(option.Extension);
        Assert.NotNull(option.Unsupported);

        // Disabled, so there is nothing to confirm - the reason is shown on the entry itself.
        Assert.False(option.NeedsConfirming);
    }

    [Fact]
    public async Task TheFileIsTheAdaptersOwnOutput()
    {
        var (service, _) = ServiceFor("starships", Ship, ".nmsship", id: "alpha-vector");

        var file = await service.ExportAsync("alpha-vector", EditorId.Nmse);

        Assert.EndsWith(".nmsship", file.FileName, StringComparison.Ordinal);
        Assert.NotEmpty(file.Content);

        // Byte-for-byte what the adapter produces on its own. The service adds nothing.
        var expected = new NmseExportAdapter().Export(
            await service.ItemAsync("alpha-vector"), new ExportOptions([]));

        Assert.Equal(expected.Content, file.Content);
    }

    [Fact]
    public async Task AMissingPictureDoesNotStopTheDownload()
    {
        // The metadata names an image the transport will 404. A format that embeds pictures
        // should still produce a file; someone asked for a ship, not for a thumbnail.
        var (service, _) = ServiceFor("starships", Ship, ".nmsship");

        var file = await service.ExportAsync("item", EditorId.NomNom);

        Assert.NotEmpty(file.Content);
    }

    [Fact]
    public async Task TheDocumentIsFetchedOnceHoweverOftenItIsUsed()
    {
        // Opening the menu, reading a warning and then downloading is three passes over the
        // same item. It is one request.
        var (service, transport) = ServiceFor("starships", Ship, ".nmsship");

        await service.OptionsAsync("item");
        await service.OptionsAsync("item");
        await service.ExportAsync("item", EditorId.Nmse);
        await service.ExportAsync("item", EditorId.Goatfungus);

        Assert.Equal(1, transport.Fetches);
    }

    [Fact]
    public async Task AnItemThatIsNotThereFailsRatherThanReturningAnEmptyMenu()
    {
        // The component turns this into a sentence in the panel. What it must not do is show
        // an empty list, which reads as "no formats" rather than "could not read the item".
        var (service, _) = ServiceFor("starships", Ship, ".nmsship");

        await Assert.ThrowsAsync<HttpRequestException>(() => service.OptionsAsync("no-such-item"));
    }
}
