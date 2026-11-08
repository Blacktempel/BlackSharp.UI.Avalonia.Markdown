// SPDX-License-Identifier: MPL-2.0

namespace BlackSharp.UI.Avalonia.Markdown.Rendering
{
    /// <summary>
    /// Resolves Markdown destinations and restricts their schemes.
    /// </summary>
    internal static class MarkdownUriResolver
    {
        #region Internal

        /// <summary>
        /// Resolves a destination against an optional document base.
        /// </summary>
        /// <param name="value">The decoded destination, or null when absent.</param>
        /// <param name="baseUri">An optional absolute base for relative destinations.</param>
        /// <param name="image">Whether the destination must be HTTPS.</param>
        /// <returns>A permitted URI, or null if the destination cannot be used.</returns>
        internal static Uri? Resolve(string? value, Uri? baseUri, bool image)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return null;
            }

            value = value.Trim();
            if (value.Contains('\\') || value.Any(char.IsControl))
            {
                return null;
            }

            Uri? uri;
            if (baseUri?.IsAbsoluteUri == true)
            {
                Uri.TryCreate(baseUri, value, out uri);
            }
            else
            {
                Uri.TryCreate(value, UriKind.Absolute, out uri);
            }

            if (uri == null || !string.IsNullOrEmpty(uri.UserInfo))
            {
                return null;
            }

            return uri.Scheme == Uri.UriSchemeHttps
                || (!image && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeMailto))
                ? uri
                : null;
        }

        #endregion
    }
}
