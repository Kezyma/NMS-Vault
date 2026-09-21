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

    // A creature captured twice: as the egg, and as what hatched out of it.
    private const string Pet = "[EXP-13-R] Gnawing Scuttler.nmspet";
    private const string Egg = "[EXP-13-R] Gnawing Scuttler_egg.nmspet";

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

    /// <summary>
    /// Answers everything with the site's own shell, which is what a static host does with a
    /// path that is not on disk.
    /// </summary>
    private sealed class SinglePageFallback : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(
                    "<!DOCTYPE html><html><head><title>NMS-Vault</title></head><body></body></html>",
                    Encoding.UTF8, "text/html"),
            });
    }

    [Fact]
    public async Task AMissingDocumentFailsAsAMissingDocument()
    {
        // The site is a single-page app on a static host, so a document that is not there is
        // answered with index.html and a 200 - nothing throws until the parser meets <!DOCTYPE
        // and rejects it, three layers below the fetch. A caller guarding against a failed
        // request is not watching for a parse error there, so it escaped the component and
        // took the whole page down with it instead of the one sheet that asked.
        //
        // Easy to reach, too: republishing rewrites every item document, and a page left open
        // across a rebuild asks for one in the window where it does not exist.
        var service = new ExportService(
            new HttpClient(new SinglePageFallback()) { BaseAddress = new Uri("https://example.test/") });

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => service.ItemAsync("anything"));

        Assert.Contains("not there", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnEmptyBodyFailsAsAMissingDocumentToo()
    {
        // The dev server's version of the same thing, and the one actually reported: it answers
        // a path that is not on disk with a 200, no content type and nothing in the body. The
        // parser then rejects an empty document at line 1, column 1, which is the error that
        // took the page down.
        var service = new ExportService(
            new HttpClient(new Empty200()) { BaseAddress = new Uri("https://example.test/") });

        var thrown = await Assert.ThrowsAsync<HttpRequestException>(() => service.ItemAsync("anything"));

        Assert.Contains("not there", thrown.Message, StringComparison.Ordinal);
    }

    /// <summary>Answers everything with a 200, no content type and no body.</summary>
    private sealed class Empty200 : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
            => Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new ByteArrayContent([]),
            });
    }

    [Fact]
    public async Task AnEggIsDownloadedInsteadWhenItIsTheOneChosen()
    {
        // A creature and the egg it hatched from are one item with two payloads, so the choice
        // is made at download time rather than by having two entries for one animal.
        var (service, _) = WithEgg();

        var creature = await service.ExportAsync("item", EditorId.Nmse, CompanionForm.Companion);
        var egg = await service.ExportAsync("item", EditorId.Nmse, CompanionForm.Egg);

        Assert.NotEqual(creature.Content, egg.Content);

        // The egg is the creature before it hatched, so what it is - species and genus - has to
        // match, and what it does not carry is the accessories.
        var hatched = JsonObject.FromBytes(creature.Content);
        var unhatched = JsonObject.FromBytes(egg.Content);

        Assert.Equal(hatched.GetString("SpeciesSeed"), unhatched.GetString("SpeciesSeed"));
        Assert.Equal(hatched.GetString("GenusSeed"), unhatched.GetString("GenusSeed"));
        Assert.Null(unhatched.GetArray("PetAccessoryCustomisation"));
    }

    [Fact]
    public async Task TheTwoFormsDoNotLandOnTopOfEachOther()
    {
        // Downloading both would otherwise put two files of the same name in one folder, and
        // the second would quietly replace the first.
        var (service, _) = WithEgg();

        var creature = await service.ExportAsync("item", EditorId.Nmse, CompanionForm.Companion);
        var egg = await service.ExportAsync("item", EditorId.Nmse, CompanionForm.Egg);

        Assert.Equal("Test Item.nmspet", creature.FileName);
        Assert.Equal("Test Item (Egg).nmspet", egg.FileName);
    }

    [Fact]
    public async Task WhatAFormatLosesIsAskedOfTheFormBeingDownloaded()
    {
        // goatfungus drops companion accessories, which is worth stopping someone for when
        // they are downloading a creature wearing three and is simply untrue of its egg.
        var (service, _) = WithEgg();

        var forCreature = await service.OptionsAsync("item", CompanionForm.Companion);
        var forEgg = await service.OptionsAsync("item", CompanionForm.Egg);

        Assert.NotEmpty(forCreature.First(o => o.Editor == EditorId.Goatfungus).Losses);
        Assert.Empty(forEgg.First(o => o.Editor == EditorId.Goatfungus).Losses);
    }

    [Fact]
    public async Task AnItemWithNoEggIgnoresTheChoiceRatherThanFailing()
    {
        // The menu only offers the choice where there is one, but a stale selection must not
        // be able to turn into a failed download on the next item.
        var (service, _) = ServiceFor("companions", Pet, ".nmspet");

        Assert.False(await service.HasEggAsync("item"));

        var asked = await service.ExportAsync("item", EditorId.Nmse, CompanionForm.Egg);
        var plain = await service.ExportAsync("item", EditorId.Nmse);

        Assert.Equal(plain.Content, asked.Content);
        Assert.Equal(plain.FileName, asked.FileName);
    }

    /// <summary>A companion carrying the egg it hatched from, as the gallery stores one.</summary>
    private static (ExportService Service, OneItem Transport) WithEgg()
    {
        var stored = Import(Pet).WithEgg(Import(Egg).Payload);
        var transport = new OneItem("item", stored.ToBytes());

        return (new ExportService(new HttpClient(transport) { BaseAddress = new Uri("https://example.test/") }),
                transport);
    }

    private static VaultItem Import(string file)
        => NmseImporter.Read(
            File.ReadAllBytes(Path.Combine(FixtureRoot, "companions", file)),
            new VaultMetadata { Id = "item", DisplayName = "Test Item" },
            ".nmspet");

    [Fact]
    public async Task A404IsStillA404()
    {
        // The host that answers honestly must keep failing the same way it always did.
        var service = new ExportService(
            new HttpClient(new OneItem("present", [])) { BaseAddress = new Uri("https://example.test/") });

        await Assert.ThrowsAsync<HttpRequestException>(() => service.ItemAsync("absent"));
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
