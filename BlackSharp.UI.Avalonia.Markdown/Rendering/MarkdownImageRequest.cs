// SPDX-License-Identifier: MPL-2.0

using Avalonia.Media;

namespace BlackSharp.UI.Avalonia.Markdown.Rendering
{
    /// <summary>
    /// Holds one image task and its cancellation and disposal policy.
    /// </summary>
    internal sealed class MarkdownImageRequest
    {
        #region Constructor

        /// <summary>
        /// Starts the supplied image loader and records who owns the returned image.
        /// </summary>
        /// <param name="load">The image loader.</param>
        /// <param name="ownsImage">Whether to dispose the image when this request is released.</param>
        internal MarkdownImageRequest(Func<CancellationToken, Task<IImage?>> load, bool ownsImage)
        {
            _ownsImage = ownsImage;

            Image = load(_cancellation.Token);
        }

        #endregion

        #region Fields

        /// <summary>
        /// Signals that the preview no longer uses this request.
        /// </summary>
        private readonly CancellationTokenSource _cancellation = new();

        /// <summary>
        /// Indicates that this request owns the loaded image.
        /// </summary>
        private readonly bool _ownsImage;

        #endregion

        #region Properties

        /// <summary>
        /// Gets the pending or completed image result.
        /// </summary>
        internal Task<IImage?> Image { get; }

        #endregion

        #region Internal

        /// <summary>
        /// Cancels the load and disposes the result if the request owns it.
        /// </summary>
        internal async Task ReleaseAsync()
        {
            try
            {
                await _cancellation.CancelAsync();

                if (await Image is IDisposable image && _ownsImage)
                {
                    image.Dispose();
                }
            }
            finally
            {
                _cancellation.Dispose();
            }
        }

        #endregion
    }
}
