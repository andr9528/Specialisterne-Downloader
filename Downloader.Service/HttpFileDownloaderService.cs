using Downloader.Abstraction.Interfaces.Services;
using System.Diagnostics;
using System.Net.Mime;

namespace Downloader.Service
{
    /// FEEDBACK
    /// S:
    /// O:
    /// L:
    /// I:
    /// D:
    /// Naming: Could be TryDownloadOnceAsync. It seems to be very an attempt at something
    /// Readability: sw and ms could be full names
    /// Organisation:
    /// Comments:
    /// Error Handling: Hard to see how the HttpRequestExceptions are handled. Could log the exception and rethrow to make more obvious
    /// Logging:
    /// Test Ideas:
    /// Other:
    public class HttpFileDownloaderService : IHttpFileDownloaderService
    {
        private readonly HttpClient httpClient;

        public HttpFileDownloaderService(HttpClient httpClient)
        {
            this.httpClient = httpClient;
        }

        public async Task<(Stream Stream, TimeSpan Elapsed)> DownloadOnce(string link, CancellationToken ct = default)
        {
            var sw = Stopwatch.StartNew();

            using HttpResponseMessage response =
                await httpClient.GetAsync(link, HttpCompletionOption.ResponseHeadersRead, ct);
            response.EnsureSuccessStatusCode();

            var contentType = response.Content.Headers.ContentType?.MediaType;

            if (string.IsNullOrWhiteSpace(contentType))
                throw new InvalidOperationException($"No Content-Type returned for '{link}'.");

            if (contentType.StartsWith("text/html", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException(
                    $"Link '{link}' returned HTML content instead of a downloadable file.");

            // Buffer fully so the timing reflects the full download of the attempt.
            byte[] bytes = await response.Content.ReadAsByteArrayAsync(ct);

            sw.Stop();

            var ms = new MemoryStream(bytes, false);
            ms.Position = 0;

            return (ms, sw.Elapsed);
        }
    }
}