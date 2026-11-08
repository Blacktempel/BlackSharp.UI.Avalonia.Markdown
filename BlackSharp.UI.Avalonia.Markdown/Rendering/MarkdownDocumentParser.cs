// SPDX-License-Identifier: MPL-2.0

using HtmlAgilityPack;
using Markdig;
using Markdig.Extensions.AutoIdentifiers;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;

namespace BlackSharp.UI.Avalonia.Markdown.Rendering
{
    /// <summary>
    /// Converts the selected Markdown dialect to an inert tree of document elements.
    /// </summary>
    internal static class MarkdownDocumentParser
    {
        #region Fields

        /// <summary>
        /// Parses CommonMark and treats raw HTML as literal text.
        /// </summary>
        private static readonly MarkdownPipeline s_commonMark = new MarkdownPipelineBuilder()
            .DisableHtml()
            .Build();

        /// <summary>
        /// Parses GitHub Markdown extensions and preserves HTML for the native renderer to filter.
        /// </summary>
        private static readonly MarkdownPipeline s_gitHub = new MarkdownPipelineBuilder()
            .UsePipeTables(new PipeTableOptions { UseGfmRules = true })
            .UseTaskLists()
            .UseAutoLinks()
            .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
            .UseAutoIdentifiers(AutoIdentifierOptions.GitHub)
            .UseAlertBlocks()
            .UseEmojiAndSmiley(enableSmileys: false)
            .UseFootnotes()
            .Build();

        #endregion

        #region Internal

        /// <summary>
        /// Parses Markdown using the requested dialect.
        /// </summary>
        /// <param name="text">The source text.</param>
        /// <param name="useGitHubSyntax">Whether to enable GitHub extensions and raw HTML input.</param>
        /// <returns>The root of the parsed document.</returns>
        internal static HtmlNode Parse(string text, bool useGitHubSyntax)
        {
            var document = new HtmlDocument { OptionMaxNestedChildNodes = 96 };

            document.LoadHtml(
                Markdig.Markdown.ToHtml(text.TrimStart('\uFEFF'), useGitHubSyntax ? s_gitHub : s_commonMark));

            return document.DocumentNode;
        }

        #endregion
    }
}
