using NmsVault.Core;
using NmsVault.Core.Adapters;
using NmsVault.Json;

namespace NmsVault.Web.Services;

/// <summary>
/// One editor on an item's download menu, with everything the entry needs to draw itself.
/// </summary>
/// <remarks>
/// Built per item rather than once, because every field but the name depends on the item:
/// goatfungus has no frigate format, and what a format loses depends on what the item
/// actually carries - a ship with no custom colours loses nothing to a format that cannot
/// store them.
/// </remarks>
public sealed record DownloadOption
{
    /// <summary>Which editor.</summary>
    public required EditorId Editor { get; init; }

    /// <summary>The editor's name, as its authors write it.</summary>
    public required string Label { get; init; }

    /// <summary>Where to get the editor.</summary>
    public required string HomepageUrl { get; init; }

    /// <summary>The file extension, or null when this editor has no format for the kind.</summary>
    public string? Extension { get; init; }

    /// <summary>Why the editor cannot take this kind at all, or null when it can.</summary>
    public string? Unsupported { get; init; }

    /// <summary>What this format would drop, as sentences. Empty when nothing is lost.</summary>
    public IReadOnlyList<string> Losses { get; init; } = [];

    /// <summary>
    /// Whether output has been confirmed against the real editor. Carried but not currently
    /// drawn: the menu shows the formats and what each one would lose, and a standing caveat
    /// about the editors under every list is noise in front of the thing being chosen.
    /// </summary>
    public bool IsVerified { get; init; }

    /// <summary>Whether the entry can be chosen.</summary>
    public bool IsAvailable => Unsupported is null;

    /// <summary>
    /// Whether choosing this should stop and ask first. Only loss stops anyone: it is about
    /// this item, it is specific, and it is not visible any other way.
    /// </summary>
    public bool NeedsConfirming => IsAvailable && Losses.Count > 0;
}

/// <summary>
/// Turns a gallery item into a file for whichever editor someone uses.
/// </summary>
/// <remarks>
/// <para>
/// The conversion runs in the browser, on the item document fetched on demand. Nothing is
/// pre-rendered per format at ingest: four formats across five kinds is twenty files per
/// item to keep in step, and the adapters are the same code either way.
/// </para>
/// <para>
/// Documents are cached per item, so opening the menu, reading the warning and then
/// downloading does not fetch the same ship three times.
/// </para>
/// </remarks>
public sealed class ExportService(HttpClient http)
{
    private readonly HttpClient _http = http;
    private readonly Dictionary<string, VaultItem> _items = new(StringComparer.Ordinal);
    private readonly Dictionary<string, byte[]> _images = new(StringComparer.Ordinal);
    private readonly SemaphoreSlim _gate = new(1, 1);
    private IReadOnlyList<IExportAdapter>? _adapters;

    /// <summary>
    /// The adapters, in the order the menu offers them - NMSE first, as the only one whose
    /// output is verified and the only one that can currently load a 7.03 save.
    /// </summary>
    private IReadOnlyList<IExportAdapter> Adapters
    {
        get
        {
            // The mapper reads an embedded table of some thousands of entries, so it is built
            // once and shared. It holds no per-conversion state.
            if (_adapters is not null) return _adapters;

            var mapper = JsonNameMapper.LoadEmbedded();

            return _adapters =
            [
                new NmseExportAdapter(),
                new NomNomExportAdapter(mapper),
                new GoatfungusExportAdapter(),
                new CompanionExportAdapter(mapper),
            ];
        }
    }

    /// <summary>What the download menu should show for one item.</summary>
    /// <param name="id">The item's id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>One entry per editor, including the ones that cannot take this kind.</returns>
    public async Task<IReadOnlyList<DownloadOption>> OptionsAsync(
        string id, CancellationToken cancellationToken = default)
    {
        var item = await ItemAsync(id, cancellationToken).ConfigureAwait(false);

        var options = new List<DownloadOption>(Adapters.Count);

        foreach (var adapter in Adapters)
        {
            var extension = adapter.Extension(item.Kind);

            options.Add(new DownloadOption
            {
                Editor = adapter.Editor,
                Label = adapter.DisplayName,
                HomepageUrl = adapter.HomepageUrl,
                Extension = extension.HasValue ? extension.Value : null,
                Unsupported = extension.HasValue ? null : extension.Alternative.Reason,
                Losses = extension.HasValue ? adapter.LossesFor(item) : [],
                IsVerified = adapter.IsVerified,
            });
        }

        return options;
    }

    /// <summary>Produces one item's file for one editor.</summary>
    /// <param name="id">The item's id.</param>
    /// <param name="editor">Which editor's format.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The file name and bytes.</returns>
    /// <exception cref="NotSupportedException">If that editor has no format for the kind.</exception>
    public async Task<ExportResult> ExportAsync(
        string id, EditorId editor, CancellationToken cancellationToken = default)
    {
        var item = await ItemAsync(id, cancellationToken).ConfigureAwait(false);
        var adapter = Adapters.First(a => a.Editor == editor);

        return adapter.Export(item, new ExportOptions(
            await ImagesAsync(item, cancellationToken).ConfigureAwait(false)));
    }

    /// <summary>Fetches an item document, or returns the one already fetched.</summary>
    /// <param name="id">The item's id.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The item.</returns>
    public async Task<VaultItem> ItemAsync(string id, CancellationToken cancellationToken = default)
    {
        if (_items.TryGetValue(id, out var cached)) return cached;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_items.TryGetValue(id, out cached)) return cached;

            // Bytes rather than a stream: in a browser the response stream crosses a bridge
            // into JavaScript, and pulling a document through it a chunk at a time stalls.
            byte[] bytes = await _http.GetByteArrayAsync($"gallery/items/{id}.json", cancellationToken)
                .ConfigureAwait(false);

            return _items[id] = VaultItem.FromBytes(bytes, id);
        }
        finally
        {
            _gate.Release();
        }
    }

    /// <summary>
    /// The item's images as bytes, for the two formats that embed them. Fetched only when
    /// one of those formats is actually chosen, and only as many as they can hold.
    /// </summary>
    private async Task<IReadOnlyList<byte[]>> ImagesAsync(VaultItem item, CancellationToken cancellationToken)
    {
        const int Most = 6;

        var images = new List<byte[]>(Math.Min(Most, item.Meta.Images.Count));

        foreach (string path in item.Meta.Images.Take(Most))
        {
            if (_images.TryGetValue(path, out byte[]? bytes))
            {
                images.Add(bytes);
                continue;
            }

            try
            {
                bytes = await _http.GetByteArrayAsync($"gallery/{path}", cancellationToken)
                    .ConfigureAwait(false);
            }
            catch (HttpRequestException)
            {
                // A missing picture is not a reason to refuse the file someone asked for.
                continue;
            }

            _images[path] = bytes;
            images.Add(bytes);
        }

        return images;
    }
}
