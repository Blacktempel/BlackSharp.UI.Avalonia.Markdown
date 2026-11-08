// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Data.Converters;
using Avalonia.Media;
using HtmlAgilityPack;
using System.Globalization;
using System.Text.RegularExpressions;

namespace BlackSharp.UI.Avalonia.Markdown.Rendering
{
    internal sealed partial class MarkdownRenderer
    {
        #region Fields

        /// <summary>
        /// Caps inline controls to the preview width with room for the scrollbar and document insets.
        /// </summary>
        private static readonly FuncValueConverter<Rect, double> s_inlineWidthConverter = new(bounds =>
            bounds.Width > 0 ? Math.Clamp(bounds.Width - 32, 1, 560) : 560);

        #endregion

        #region Private

        /// <summary>
        /// Creates a selectable, wrapping text block from inline elements.
        /// </summary>
        /// <param name="nodes">The inline nodes in document order.</param>
        /// <param name="alignment">The horizontal text alignment.</param>
        /// <returns>The text block containing the rendered inlines.</returns>
        private SelectableTextBlock CreateText(IEnumerable<HtmlNode> nodes, TextAlignment alignment = TextAlignment.Left)
        {
            var block = new SelectableTextBlock
            {
                TextWrapping  = TextWrapping.Wrap,
                TextAlignment = alignment
            };

            AddInlines(block.Inlines!, nodes, true);
            return block;
        }

        /// <summary>
        /// Adds the native inline representation of each node to the supplied collection.
        /// </summary>
        /// <param name="target">The collection that receives the inlines.</param>
        /// <param name="nodes">The inline nodes in document order.</param>
        /// <param name="trimEdges">Whether to trim collapsible spaces at the first and last text node.</param>
        private void AddInlines(InlineCollection target, IEnumerable<HtmlNode> nodes, bool trimEdges = false)
        {
            var children = nodes.Where(node => node.NodeType != HtmlNodeType.Comment).ToArray();

            // Convert each text or element node to its inline representation.
            for (var index = 0; index < children.Length; ++index)
            {
                var node = children[index];

                if (node is HtmlTextNode text)
                {
                    var value = WhitespacePattern().Replace(Decode(text.Text), " ");

                    if (trimEdges && index == 0)
                    {
                        value = value.TrimStart(' ');
                    }

                    if (trimEdges && index == children.Length - 1)
                    {
                        value = value.TrimEnd(' ');
                    }

                    if (value.Length != 0)
                    {
                        target.Add(new Run(value));
                    }
                }
                else if (RenderInline(node) is { } inline)
                {
                    target.Add(inline);
                }
            }
        }

        /// <summary>
        /// Creates an inline from the supported element names. Other elements are omitted.
        /// </summary>
        /// <param name="node">The HTML element.</param>
        /// <returns>The rendered inline, or null if the element is unsupported.</returns>
        private Inline? RenderInline(HtmlNode node)
        {
            switch (node.Name)
            {
                case "a":
                    return RenderLink(node);
                case "br":
                    return new LineBreak();
                case "img":
                    return RenderImage(node);
                case "picture":
                    return RenderPicture(node);
                case "code":
                case "kbd":
                case "samp":
                    return new Run(Decode(node.InnerText))
                    {
                        FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                        Background = SurfaceBrush,
                    };
            }

            Span? span = node.Name switch
            {
                "b"   or "strong"           => new Bold(),
                "em"  or "i"                => new Italic(),
                "del" or "s" or "strike"    => new Span { TextDecorations = TextDecorations.Strikethrough },
                "ins" or "u"                => new Underline(),
                "sub"                       => new Span { BaselineAlignment = BaselineAlignment.Subscript   },
                "sup"                       => new Span { BaselineAlignment = BaselineAlignment.Superscript },
                "span" or "small" or "abbr"
                       or "mark"  or "q"
                       or "cite"            => new Span(),
                _ => null,
            };

            if (span != null)
            {
                AddInlines(span.Inlines, node.ChildNodes);
            }

            return span;
        }

        /// <summary>
        /// Creates a link, a named anchor or inert content when the destination is rejected.
        /// </summary>
        /// <param name="node">The anchor element.</param>
        /// <returns>The link, anchor or inert label.</returns>
        private Inline RenderLink(HtmlNode node)
        {
            var destination = Attribute(node, "href")?.Trim();

            var fragment = destination?.StartsWith('#') == true
                ? Uri.UnescapeDataString(destination[1..])
                : null;

            var uri = fragment == null
                ? MarkdownUriResolver.Resolve(destination, _baseUri, false)
                : null;

            if (uri == null && fragment == null)
            {
                var span = new Span();

                if (Attribute(node, "name") != null || Attribute(node, "id") != null)
                {
                    var marker = new Border { Width = 0, Height = 1 };
                    RegisterAnchor(node, marker);

                    span.Inlines.Add(Embed(marker));
                }

                AddInlines(span.Inlines, node.ChildNodes);
                return span;
            }

            var label = new TextBlock { TextWrapping = TextWrapping.Wrap, IsHitTestVisible = false };
            AddInlines(label.Inlines!, node.ChildNodes, true);

            // The transparent border gives the entire label a hit surface for the button.
            var button = new HyperlinkButton
            {
                Content   = new Border { Background = Brushes.Transparent, Child = label },
                Padding   = new Thickness(0),
                MinHeight = 0,
                MinWidth  = 0,
            };

            ConstrainWidth(button);
            button.Classes.Add("markdown-link");

            RegisterAnchor(node, button);

            ToolTip.SetTip(button, Attribute(node, "title") ?? uri?.AbsoluteUri ?? destination);

            button.Click += (_, _) =>
            {
                if (fragment != null && _anchors.TryGetValue(fragment, out var anchor))
                {
                    anchor.BringIntoView();
                }
                else if (uri != null)
                {
                    _openLink(uri);
                }
            };

            return Embed(button);
        }

        /// <summary>
        /// Chooses a color-scheme-specific source and uses the nested image as the fallback.
        /// </summary>
        /// <param name="picture">The picture element.</param>
        /// <returns>The selected image, or null if there is no fallback image element.</returns>
        private Inline? RenderPicture(HtmlNode picture)
        {
            var fallback = picture.ChildNodes.FirstOrDefault(child => child.Name == "img");

            if (fallback == null)
            {
                return null;
            }

            var media = _dark ? "(prefers-color-scheme: dark)" : "(prefers-color-scheme: light)";

            var source = picture.ChildNodes.FirstOrDefault(child => child.Name == "source"
                && string.Equals(Attribute(child, "media")?.Trim(), media, StringComparison.OrdinalIgnoreCase));

            var sourceSet = source == null ? null : Attribute(source, "srcset");

            var selected = sourceSet?.Split(new[] { ' ', ',', '\r', '\n', '\t' },
                StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();

            return RenderImage(fallback, selected);
        }

        /// <summary>
        /// Creates an alt-text fallback and starts image loading for a permitted URL.
        /// </summary>
        /// <param name="node">The image element.</param>
        /// <param name="overrideSource">The optional source selected from a picture element.</param>
        /// <returns>The pending image or its alt text.</returns>
        private Inline RenderImage(HtmlNode node, string? overrideSource = null)
        {
            var alt = Attribute(node, "alt") ?? string.Empty;

            var uri = MarkdownUriResolver.Resolve(overrideSource        , _imageBaseUri, true)
                   ?? MarkdownUriResolver.Resolve(Attribute(node, "src"), _imageBaseUri, true);

            if (!_allowImages || uri == null || (_imageUris.Count >= MaximumImages && !_imageUris.Contains(uri)))
            {
                return new Run(alt);
            }

            _imageUris.Add(uri);

            var fallback = new TextBlock
            {
                Text         = alt,
                TextWrapping = TextWrapping.Wrap
            };

            ConstrainWidth(fallback);

            var container = new ContentControl { Content = fallback };

            var image = new Image
            {
                Stretch   = Stretch.Uniform,
                MaxHeight = 480,
                Width     = GetDimension(node, "width" , 560),
                Height    = GetDimension(node, "height", 480),
            };

            ConstrainWidth(image);
            image.Classes.Add("markdown-image");

            AutomationProperties.SetName(image, alt);
            ToolTip.SetTip(image, Attribute(node, "title"));

            _ = ApplyImageAsync(container, image, uri);

            return Embed(container);
        }

        /// <summary>
        /// Replaces the fallback with the loaded image if the preview is still current.
        /// </summary>
        /// <param name="container">The host displaying the fallback.</param>
        /// <param name="image">The image control that receives the source.</param>
        /// <param name="uri">The resolved HTTPS URL.</param>
        private async Task ApplyImageAsync(ContentControl container, Image image, Uri uri)
        {
            var source = await _loadImage(uri);

            if (source != null && !_lifetime.IsCancellationRequested)
            {
                image    .Source  = source;
                container.Content = image;
            }
        }

        /// <summary>
        /// Binds the maximum width of an inline control to the preview bounds.
        /// </summary>
        /// <param name="control">The control to constrain.</param>
        private void ConstrainWidth(Control control)
        {
            control.Bind(Control.MaxWidthProperty, new Binding(nameof(Control.Bounds))
            {
                Source    = _viewport,
                Converter = s_inlineWidthConverter,
            });
        }

        /// <summary>
        /// Reads a positive pixel size and clamps it to the available image limit.
        /// </summary>
        private static double GetDimension(HtmlNode node, string name, double maximum) =>
            int.TryParse(Attribute(node, name), NumberStyles.None, CultureInfo.InvariantCulture, out var size) && size > 0
                ? Math.Min(size, maximum)
                : double.NaN;

        /// <summary>
        /// Embeds an Avalonia control in a line of text.
        /// </summary>
        private static InlineUIContainer Embed(Control child) => new() { Child = child };

        /// <summary>
        /// Matches collapsible HTML whitespace.
        /// </summary>
        [GeneratedRegex(@"[ \t\r\n\f]+", RegexOptions.CultureInvariant)]
        private static partial Regex WhitespacePattern();

        #endregion
    }
}
