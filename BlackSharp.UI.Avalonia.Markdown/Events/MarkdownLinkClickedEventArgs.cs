// SPDX-License-Identifier: MPL-2.0

namespace BlackSharp.UI.Avalonia.Markdown.Events
{
    /// <summary>
    /// Carries the resolved destination of an activated external Markdown link.
    /// </summary>
    public sealed class MarkdownLinkClickedEventArgs : EventArgs
    {
        #region Constructor

        /// <summary>
        /// Creates an event containing the supplied link destination.
        /// </summary>
        /// <param name="uri">The destination of the link.</param>
        public MarkdownLinkClickedEventArgs(Uri uri)
        {
            Uri = uri;
        }

        #endregion

        #region Properties

        /// <summary>
        /// Gets the destination of the activated link.
        /// </summary>
        public Uri Uri { get; }

        #endregion
    }
}
