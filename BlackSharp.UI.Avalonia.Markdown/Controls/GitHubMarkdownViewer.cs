// SPDX-License-Identifier: MPL-2.0

namespace BlackSharp.UI.Avalonia.Markdown.Controls
{
    /// <summary>
    /// Displays and edits GitHub-style Markdown, including tables, tasks, alerts and safe README HTML.
    /// </summary>
    public sealed class GitHubMarkdownViewer : MarkdownViewer
    {
        #region Properties

        /// <inheritdoc/>
        protected override bool UseGitHubSyntax => true;

        #endregion
    }
}
