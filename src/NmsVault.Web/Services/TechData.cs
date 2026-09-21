using NmsVault.Core.Derived;
using NmsVault.Json;

namespace NmsVault.Web.Services;

/// <summary>
/// Loads the technology lookup, once, so installed ids can be shown as names and icons.
/// </summary>
/// <remarks>
/// Missing data is not an error. A gallery published before <c>extract-tech</c> has run simply
/// shows raw ids, which is worse but not broken - so a failed fetch leaves an empty index rather
/// than taking the page down.
/// </remarks>
public sealed class TechData(HttpClient http)
{
    private readonly HttpClient _http = http;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private TechIndex? _index;

    /// <summary>The technology lookup, loaded on first call and cached after.</summary>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The index, empty when the data is not published.</returns>
    public async Task<TechIndex> IndexAsync(CancellationToken cancellationToken = default)
    {
        if (_index is not null) return _index;

        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            if (_index is not null) return _index;

            try
            {
                byte[] bytes = await GalleryHttp
                    .DocumentAsync(_http, "gallery/tech.json", cancellationToken)
                    .ConfigureAwait(false);

                return _index = TechIndex.FromBytes(bytes);
            }
            catch (Exception ex) when (ex is HttpRequestException or JsonException)
            {
                return _index = TechIndex.Empty;
            }
        }
        finally
        {
            _gate.Release();
        }
    }
}
