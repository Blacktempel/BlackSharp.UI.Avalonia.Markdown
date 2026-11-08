// SPDX-License-Identifier: MPL-2.0

using Avalonia.Media;
using Avalonia.Media.Imaging;
using BlackSharp.Core.Logging;
using SkiaSharp;

namespace BlackSharp.UI.Avalonia.Markdown.Rendering
{
    /// <summary>
    /// Caches image requests used by the current preview and releases images removed from it.
    /// </summary>
    internal sealed class MarkdownImageStore
    {
        #region Fields

        /// <summary>
        /// Limits the encoded size of one remote image.
        /// </summary>
        private const int MaximumEncodedBytes = 4 * 1024 * 1024;

        /// <summary>
        /// Limits the decoded pixel count of one remote image.
        /// </summary>
        private const int MaximumPixelCount = 2_000_000;

        /// <summary>
        /// Limits concurrent HTTP downloads shared by the viewer instances.
        /// </summary>
        private static readonly SemaphoreSlim s_downloadSlots = new(4);

        /// <summary>
        /// Requests image data without cookies or automatic redirects.
        /// </summary>
        private static readonly HttpClient s_client = new(new HttpClientHandler
        {
            AllowAutoRedirect = false,
            UseCookies = false,
        });

        /// <summary>
        /// Maps resolved image URLs to requests used by the preview.
        /// </summary>
        private readonly Dictionary<Uri, MarkdownImageRequest> _requests = new();

        #endregion

        #region Internal

        /// <summary>
        /// Returns the image request for the supplied URL, starting one if necessary.
        /// </summary>
        /// <param name="uri">The resolved HTTPS URL.</param>
        /// <param name="loader">An optional host-supplied image provider.</param>
        /// <param name="logger">An optional logger for image errors.</param>
        /// <returns>A task that returns the image, or null on failure or cancellation.</returns>
        internal Task<IImage?> GetAsync(Uri uri, Func<Uri, CancellationToken, Task<IImage?>>? loader, ILogger? logger)
        {
            if (!_requests.TryGetValue(uri, out var request))
            {
                request = new MarkdownImageRequest(
                    (token) => LoadAsync(uri, loader, logger, token),
                    loader == null);

                _requests.Add(uri, request);
            }

            return request.Image;
        }

        /// <summary>
        /// Releases requests whose URLs do not appear in the supplied set.
        /// </summary>
        /// <param name="visibleUris">URLs used by the new preview.</param>
        internal void Retain(IReadOnlySet<Uri> visibleUris)
        {
            // Cancel downloads and release owned images that are no longer referenced by the preview.
            foreach (var uri in _requests.Keys.Where(uri => !visibleUris.Contains(uri)).ToArray())
            {
                _ = _requests[uri].ReleaseAsync();

                _requests.Remove(uri);
            }
        }

        /// <summary>
        /// Releases all stored requests.
        /// </summary>
        internal void Clear()
        {
            // Cancel all pending downloads and release their owned bitmaps.
            foreach (var request in _requests.Values)
            {
                _ = request.ReleaseAsync();
            }

            _requests.Clear();
        }

        #endregion

        #region Private

        /// <summary>
        /// Calls the supplied loader or downloads a bounded raster image.
        /// </summary>
        private static async Task<IImage?> LoadAsync(
            Uri uri, Func<Uri, CancellationToken, Task<IImage?>>? loader, ILogger? logger, CancellationToken token)
        {
            try
            {
                if (loader != null)
                {
                    return await loader(uri, token);
                }

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(token);
                timeout.CancelAfter(TimeSpan.FromSeconds(12));

                await s_downloadSlots.WaitAsync(timeout.Token);

                try
                {
                    var encoded = await DownloadAsync(uri, timeout.Token);
                    if (encoded == null || !HasSupportedDimensions(encoded))
                    {
                        return null;
                    }

                    using var stream = new MemoryStream(encoded, false);
                    return new Bitmap(stream);
                }
                finally
                {
                    s_downloadSlots.Release();
                }
            }
            catch (OperationCanceledException)
            {
                return null;
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                logger?.Log(LogLevel.Warn, nameof(MarkdownImageStore), "A Markdown image could not be loaded.", exception);
                return null;
            }
        }

        /// <summary>
        /// Reads at most the permitted number of bytes from an HTTPS response.
        /// </summary>
        private static async Task<byte[]?> DownloadAsync(Uri uri, CancellationToken token)
        {
            using var response = await s_client.GetAsync(uri, HttpCompletionOption.ResponseHeadersRead, token)
                .ConfigureAwait(false);

            if (!response.IsSuccessStatusCode || response.Content.Headers.ContentLength > MaximumEncodedBytes)
            {
                return null;
            }

            using var stream = await response.Content.ReadAsStreamAsync(token).ConfigureAwait(false);
            using var buffer = new MemoryStream();
            var chunk = new byte[8192];

            // Read at most the permitted number of bytes from the response stream.
            while (true)
            {
                var read = await stream.ReadAsync(chunk, token).ConfigureAwait(false);

                if (read == 0)
                {
                    return buffer.ToArray();
                }

                if (buffer.Length + read > MaximumEncodedBytes)
                {
                    return null;
                }

                buffer.Write(chunk, 0, read);
            }
        }

        /// <summary>
        /// Checks raster dimensions.
        /// </summary>
        private static bool HasSupportedDimensions(byte[] encoded)
        {
            using var data  = SKData.CreateCopy(encoded);
            using var codec = SKCodec.Create(data);

            return codec != null
                && codec.Info.Width > 0
                && codec.Info.Height > 0
                && (long)codec.Info.Width * codec.Info.Height <= MaximumPixelCount;
        }

        #endregion
    }
}
