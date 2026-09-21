using System.Net.Http;

namespace NmsVault.Web.Services;

/// <summary>
/// Fetches one of the gallery's JSON documents.
/// </summary>
/// <remarks>
/// <para>
/// Exists because a missing document does not arrive looking missing. Both hosts the gallery
/// runs on answer one with a 200: the dev server sends an empty body and no content type, and
/// a static host with a single-page fallback sends <c>index.html</c>. Either way
/// <see cref="HttpClient"/> raises nothing, and the first sign of trouble is the parser
/// refusing whatever arrived - a <c>JsonException</c> thrown three layers below the fetch,
/// where a caller guarding against a failed request is not looking for it. Unhandled in a
/// Blazor component, that takes the whole page down rather than the one sheet that asked.
/// </para>
/// <para>
/// It is genuinely easy to hit: republishing rewrites every item document, and a page held
/// open across a rebuild asks for one in the window where it does not exist.
/// </para>
/// <para>
/// So the masquerade is undone here, at the point where the cause is still legible, and a
/// document that is not there fails as the request failure it actually is.
/// </para>
/// </remarks>
internal static class GalleryHttp
{
    /// <summary>
    /// Fetches a gallery document as bytes.
    /// </summary>
    /// <param name="http">The client.</param>
    /// <param name="path">Path under the site root, e.g. <c>gallery/index.json</c>.</param>
    /// <param name="cancellationToken">Cancellation.</param>
    /// <returns>The document bytes.</returns>
    /// <exception cref="HttpRequestException">
    /// The request failed, or the host answered with something that is not the document.
    /// </exception>
    public static async Task<byte[]> DocumentAsync(
        HttpClient http, string path, CancellationToken cancellationToken = default)
    {
        using var response = await http.GetAsync(path, cancellationToken).ConfigureAwait(false);

        response.EnsureSuccessStatusCode();

        // The static host's answer: the site's own shell. Rejected by content type rather than
        // by sniffing the body, because a host that serves JSON as text/plain or omits the type
        // is unhelpful rather than wrong and the parser is the better judge of those.
        if (response.Content.Headers.ContentType?.MediaType is "text/html") throw Missing(path, "a page");

        // Bytes rather than a stream: in a browser the response stream crosses a bridge into
        // JavaScript, and pulling a document through it a chunk at a time stalls.
        byte[] bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken).ConfigureAwait(false);

        // The dev server's answer, which carries no content type to go on: a 200 and nothing
        // at all. No document is empty, so this is never a real one.
        return bytes.Length > 0 ? bytes : throw Missing(path, "nothing");
    }

    /// <summary>The host answered, but not with the document.</summary>
    private static HttpRequestException Missing(string path, string what)
        => new($"'{path}' came back as {what} rather than a document, which is what this site " +
               "serves when the file is not there.");
}
