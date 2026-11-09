// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Layout;
using Avalonia.Media;
using HtmlAgilityPack;
using System.Globalization;
using System.Net;
using System.Text.RegularExpressions;

namespace BlackSharp.UI.Avalonia.Markdown.Rendering
{
    /// <summary>
    /// Projects a restricted document tree into native Avalonia controls.
    /// </summary>
    internal sealed partial class MarkdownRenderer
    {
        #region Constructor

        /// <summary>
        /// Creates a renderer for one preview.
        /// </summary>
        /// <param name="viewport">The scroll viewer containing the preview.</param>
        /// <param name="baseUri">The optional base for relative links.</param>
        /// <param name="imageBaseUri">The optional base for relative image URLs.</param>
        /// <param name="allowImages">Whether to request remote images.</param>
        /// <param name="dark">Whether to use the dark palette.</param>
        /// <param name="loadImage">The callback that provides images.</param>
        /// <param name="openLink">The callback for activated external links.</param>
        /// <param name="lifetime">The cancellation token for this preview.</param>
        internal MarkdownRenderer(ScrollViewer viewport, Uri? baseUri, Uri? imageBaseUri, bool allowImages, bool dark,
            Func<Uri, Task<IImage?>> loadImage, Action<Uri> openLink, CancellationToken lifetime)
        {
            _viewport     = viewport;
            _baseUri      = baseUri;
            _imageBaseUri = imageBaseUri;
            _allowImages  = allowImages;
            _dark         = dark;
            _loadImage    = loadImage;
            _openLink     = openLink;
            _lifetime     = lifetime;
        }

        #endregion

        #region Fields

        /// <summary>
        /// The maximum number of distinct remote images in one preview.
        /// </summary>
        private const int MaximumImages = 16;

        /// <summary>
        /// The scroll viewer whose width constrains inline controls.
        /// </summary>
        private readonly ScrollViewer _viewport;

        /// <summary>
        /// The base URI for relative links.
        /// </summary>
        private readonly Uri? _baseUri;

        /// <summary>
        /// The base URI for relative images.
        /// </summary>
        private readonly Uri? _imageBaseUri;

        /// <summary>
        /// Whether image requests are allowed.
        /// </summary>
        private readonly bool _allowImages;

        /// <summary>
        /// Whether the preview uses the dark palette.
        /// </summary>
        private readonly bool _dark;

        /// <summary>
        /// Supplies images to the preview.
        /// </summary>
        private readonly Func<Uri, Task<IImage?>> _loadImage;

        /// <summary>
        /// Handles activation of an external link.
        /// </summary>
        private readonly Action<Uri> _openLink;

        /// <summary>
        /// Signals that this preview has been replaced.
        /// </summary>
        private readonly CancellationToken _lifetime;

        /// <summary>
        /// Maps document fragments to the first matching visual.
        /// </summary>
        private readonly Dictionary<string, Control> _anchors = new(StringComparer.Ordinal);

        /// <summary>
        /// Records the image requests retained by this preview.
        /// </summary>
        private readonly HashSet<Uri> _imageUris = new();

        #endregion

        #region Properties

        /// <summary>
        /// Gets the image URLs used by this document.
        /// </summary>
        internal IReadOnlySet<Uri> ImageUris => _imageUris;

        /// <summary>
        /// Gets the foreground for separators and table borders.
        /// </summary>
        private IBrush DividerBrush => Brush.Parse(_dark ? "#4B5563" : "#D0D7DE");

        /// <summary>
        /// Gets the background for code and table headers.
        /// </summary>
        private IBrush SurfaceBrush => Brush.Parse(_dark ? "#252B33" : "#F1F4F8");

        #endregion

        #region Internal

        /// <summary>
        /// Creates the document root and adds its blocks.
        /// </summary>
        /// <param name="document">The parsed HTML document node.</param>
        /// <returns>The native document root.</returns>
        internal Control Render(HtmlNode document)
        {
            var result = CreateStack();
            result.Classes.Add("markdown-document");

            AddBlocks(result, document.ChildNodes);
            return result;
        }

        #endregion

        #region Private

        /// <summary>
        /// Groups adjacent inline nodes into paragraphs and adds block elements separately.
        /// </summary>
        /// <param name="target">The panel that receives the rendered blocks.</param>
        /// <param name="nodes">The nodes in document order.</param>
        /// <param name="taskInput">The checkbox element rendered separately from the list item body.</param>
        private void AddBlocks(StackPanel target, IEnumerable<HtmlNode> nodes, HtmlNode? taskInput = null)
        {
            var pending = new List<HtmlNode>();

            // Collect inline nodes and render each block element in its own control.
            foreach (var node in nodes)
            {
                if (ReferenceEquals(node, taskInput))
                {
                    continue;
                }

                if (IsBlock(node.Name))
                {
                    FlushInlines();

                    var visual = RenderBlock(node, taskInput);
                    RegisterAnchor(node, visual);

                    target.Children.Add(visual);
                }
                else if (node.NodeType != HtmlNodeType.Comment)
                {
                    pending.Add(node);
                }
            }

            FlushInlines();

            void FlushInlines()
            {
                if (pending.Count == 0)
                {
                    return;
                }

                var text = CreateText(pending);

                if (text.Inlines!.Count > 0)
                {
                    target.Children.Add(text);
                }

                pending.Clear();
            }
        }

        /// <summary>
        /// Creates a visual for an allowed block element.
        /// </summary>
        /// <param name="node">The block element.</param>
        /// <param name="taskInput">The checkbox element to omit from a task paragraph.</param>
        /// <returns>The rendered block.</returns>
        private Control RenderBlock(HtmlNode node, HtmlNode? taskInput)
        {
            switch (node.Name)
            {
                case "h1":
                case "h2":
                case "h3":
                case "h4":
                case "h5":
                case "h6":
                {
                    var heading = CreateText(node.ChildNodes, GetAlignment(node));
                    heading.Classes.Add("markdown-heading");
                    heading.FontWeight = FontWeight.SemiBold;
                    heading.FontSize   = node.Name[1] switch { '1' => 30, '2' => 24, '3' => 20, '4' => 17, _ => 15 };
                    heading.Margin     = new Thickness(0, 5, 0, 0);

                    return heading;
                }
                case "p":
                    return CreateText(
                        ReferenceEquals(taskInput?.ParentNode, node)
                            ? node.ChildNodes.Where(child => !ReferenceEquals(child, taskInput))
                            : node.ChildNodes,
                        GetAlignment(node));
                case "pre":
                {
                    var code = new SelectableTextBlock
                    {
                        Text       = Decode(node.InnerText).TrimEnd('\r', '\n'),
                        FontFamily = new FontFamily("Consolas, Menlo, monospace"),
                    };

                    code.Classes.Add("markdown-code");

                    return new Border
                    {
                        Background   = SurfaceBrush,
                        CornerRadius = new CornerRadius(6),
                        Padding      = new Thickness(12, 8),
                        Child        = new ScrollViewer { Content = code, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto },
                    };
                }
                case "hr":
                    return new Border { Height = 1, Background = DividerBrush, Margin = new Thickness(0, 4) };
                case "ul":
                case "ol":
                    return RenderList(node);
                case "table":
                    return RenderTable(node);
                case "details":
                {
                    var summary = node.ChildNodes.FirstOrDefault(child => child.Name == "summary");
                    var details = CreateStack();

                    AddBlocks(details, node.ChildNodes.Where(child => child != summary));

                    var header = new TextBlock { TextWrapping = TextWrapping.Wrap };

                    if (summary != null)
                    {
                        AddInlines(header.Inlines!, summary.ChildNodes, true);
                    }
                    else
                    {
                        header.Text = "…";
                    }

                    return new Expander
                    {
                        Header              = header,
                        Content             = details,
                        IsExpanded          = node.Attributes.Contains("open"),
                        HorizontalAlignment = HorizontalAlignment.Stretch,
                    };
                }
                case "blockquote":
                    return RenderQuote(node, DividerBrush);
                default:
                {
                    if (TryRenderAlert(node) is { } alert)
                    {
                        return alert;
                    }

                    var container = CreateStack();

                    if (GetAlignment(node) is TextAlignment.Center or TextAlignment.Right
                     && !node.ChildNodes.Any(child => IsBlock(child.Name)))
                    {
                        container.Children.Add(CreateText(node.ChildNodes, GetAlignment(node)));
                    }
                    else
                    {
                        AddBlocks(container, node.ChildNodes);
                    }

                    return container;
                }
            }
        }

        /// <summary>
        /// Renders a blockquote with a side rule and an inset body.
        /// </summary>
        /// <param name="node">The blockquote element.</param>
        /// <param name="borderBrush">The side rule brush.</param>
        /// <returns>The bordered quote body.</returns>
        private Border RenderQuote(HtmlNode node, IBrush borderBrush)
        {
            var body = CreateStack();
            AddBlocks(body, node.ChildNodes);

            return CreateQuoteBorder(body, borderBrush);
        }

        /// <summary>
        /// Wraps a quote or alert body in a border with a left rule.
        /// </summary>
        /// <param name="body">The rendered content.</param>
        /// <param name="brush">The side rule brush.</param>
        /// <returns>The bordered content.</returns>
        private static Border CreateQuoteBorder(StackPanel body, IBrush brush)
        {
            return new Border
            {
                BorderBrush     = brush,
                BorderThickness = new Thickness(3, 0, 0, 0),
                Padding         = new Thickness(12, 4),
                Child           = body,
            };
        }

        /// <summary>
        /// Builds ordered and unordered list rows, with nested blocks inside each item.
        /// </summary>
        /// <param name="list">The ordered or unordered list element.</param>
        /// <returns>The panel containing the list rows.</returns>
        private StackPanel RenderList(HtmlNode list)
        {
            var ordered = list.Name == "ol";

            var number = int.TryParse(Attribute(list, "start"), CultureInfo.InvariantCulture, out var start)
                ? (long)start
                : 1;

            var result = CreateStack();
            result.Spacing = 4;
            result.Margin  = new Thickness(8, 0, 0, 0);

            // Add a marker and the rendered body for each list item.
            foreach (var item in list.ChildNodes.Where(child => child.Name == "li"))
            {
                var body = CreateStack();

                var taskInput = GetTaskInput(item);

                AddBlocks(body, item.ChildNodes, taskInput);

                Control row;
                if (taskInput != null)
                {
                    // Keep the checkbox outside the text layout, where it cannot change the text baseline.
                    var marker = new CheckBox
                    {
                        IsChecked        = taskInput.Attributes.Contains("checked"),
                        IsHitTestVisible = false,
                        Focusable        = false,
                        MinWidth         = 0,
                        Padding          = new Thickness(0),
                    };
                    row = new MarkdownTaskRow(marker, body);
                }
                else
                {
                    var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), ColumnSpacing = 8 };
                    grid.Children.Add(new TextBlock
                    {
                        Text = ordered ? FormattableString.Invariant($"{number}.") : "•"
                    });

                    Grid.SetColumn(body, 1);
                    grid.Children.Add(body);

                    row = grid;
                }

                RegisterAnchor(item, row);
                result.Children.Add(row);

                ++number;
            }

            return result;
        }

        /// <summary>
        /// Creates a scrollable table from header and body rows.
        /// </summary>
        /// <param name="table">The table element.</param>
        /// <returns>The scroll viewer containing the table grid.</returns>
        private ScrollViewer RenderTable(HtmlNode table)
        {
            var rows = table.ChildNodes
                .SelectMany(child => child.Name is "thead" or "tbody"
                    or "tfoot" ? child.ChildNodes.AsEnumerable() : new[] { child })
                .Where(child => child.Name == "tr");

            var grid = new Grid();
            grid.Classes.Add("markdown-table");

            int rowIndex = 0;

            // Add each row's cells and grow the shared column definitions as needed.
            foreach (var row in rows)
            {
                grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

                int column = 0;

                // Use the cell's declared alignment and place it in the corresponding grid column.
                foreach (var cell in row.ChildNodes.Where(child => child.Name is "th" or "td").Take(32))
                {
                    if (column == grid.ColumnDefinitions.Count)
                    {
                        grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto) { MinWidth = 76 });
                    }

                    var text = CreateText(cell.ChildNodes, GetAlignment(cell));
                    var header = cell.Name == "th";

                    if (header)
                    {
                        text.FontWeight = FontWeight.SemiBold;
                    }

                    var border = new Border
                    {
                        Child           = text,
                        BorderBrush     = DividerBrush,
                        BorderThickness = new Thickness(column == 0 ? 1 : 0, rowIndex == 0 ? 1 : 0, 1, 1),
                        Background      = header ? SurfaceBrush : null,
                        Padding         = new Thickness(10, 6),
                        MaxWidth        = 360,
                    };

                    Grid.SetRow(border, rowIndex);
                    Grid.SetColumn(border, column++);

                    grid.Children.Add(border);
                }

                ++rowIndex;
            }

            return new ScrollViewer { Content = grid, HorizontalScrollBarVisibility = ScrollBarVisibility.Auto };
        }

        /// <summary>
        /// Records a heading ID or an explicit named anchor.
        /// </summary>
        /// <param name="node">The element carrying the ID or name.</param>
        /// <param name="visual">The navigation target for that ID.</param>
        private void RegisterAnchor(HtmlNode node, Control visual)
        {
            var id = Attribute(node, "id") ?? (node.Name == "a" ? Attribute(node, "name") : null);

            if (!string.IsNullOrEmpty(id))
            {
                _anchors.TryAdd(id, visual);
            }
        }

        /// <summary>
        /// Identifies elements that start a native block.
        /// </summary>
        /// <param name="name">The name of the element.</param>
        /// <returns>True if the element is a block, false otherwise.</returns>
        private static bool IsBlock(string name) =>
            name
            is "h1" or "h2" or "h3" or "h4" or "h5" or "h6"
            or "p" or "pre" or "hr" or "blockquote"
            or "ul" or "ol" or "table" or "details"
            or "div" or "section" or "article" or "figure" or "figcaption";

        /// <summary>
        /// Finds a checkbox at the start of a tight or loose task-list item.
        /// </summary>
        /// <param name="item">The list item element.</param>
        /// <returns>The leading checkbox element, or null for a regular list item.</returns>
        private static HtmlNode? GetTaskInput(HtmlNode item)
        {
            var node = FirstContent(item);

            if (node?.Name == "p")
            {
                node = FirstContent(node);
            }

            return node?.Name == "input" && string.Equals(Attribute(node, "type"), "checkbox", StringComparison.OrdinalIgnoreCase)
                ? node
                : null;

            static HtmlNode? FirstContent(HtmlNode parent) => parent.ChildNodes.FirstOrDefault(child =>
                child.NodeType != HtmlNodeType.Comment && (child is not HtmlTextNode text || !string.IsNullOrWhiteSpace(text.Text)));
        }

        /// <summary>
        /// Reads an HTML attribute and decodes its character references.
        /// </summary>
        private static string? Attribute(HtmlNode node, string name) => WebUtility.HtmlDecode(node.Attributes[name]?.Value);

        /// <summary>
        /// Decodes a text node's HTML character references once.
        /// </summary>
        private static string Decode(string text) => WebUtility.HtmlDecode(text);

        /// <summary>
        /// Reads only the supported alignment attribute or text-align declaration.
        /// </summary>
        private static TextAlignment GetAlignment(HtmlNode node)
        {
            var alignment = Attribute(node, "align");
            var style     = Attribute(node, "style");

            if (alignment == null && style != null)
            {
                var match = TextAlignPattern().Match(style);
                alignment = match.Success ? match.Groups[1].Value : null;
            }

            return alignment?.Trim().ToLowerInvariant() switch
            {
                "center" => TextAlignment.Center,
                "right"  => TextAlignment.Right,
                _        => TextAlignment.Left,
            };
        }

        /// <summary>
        /// Renders an alert with the Octicon and palette for its GitHub alert class.
        /// </summary>
        /// <param name="node">The block to check for an alert class.</param>
        /// <returns>The rendered alert, or null for an unrecognized block.</returns>
        private Border? TryRenderAlert(HtmlNode node)
        {
            var classes = Attribute(node, "class")?.Split(' ', StringSplitOptions.RemoveEmptyEntries);
            var type = classes?.FirstOrDefault(value => value.StartsWith("markdown-alert-", StringComparison.Ordinal));

            return type switch
            {
                "markdown-alert-note"      => RenderAlert(node, MarkdownAlertIcons.Note     , _dark ? "#58A6FF" : "#0969DA"),
                "markdown-alert-tip"       => RenderAlert(node, MarkdownAlertIcons.Tip      , _dark ? "#3FB950" : "#1A7F37"),
                "markdown-alert-important" => RenderAlert(node, MarkdownAlertIcons.Important, _dark ? "#AB7DF8" : "#8250DF"),
                "markdown-alert-warning"   => RenderAlert(node, MarkdownAlertIcons.Warning  , _dark ? "#D29922" : "#9A6700"),
                "markdown-alert-caution"   => RenderAlert(node, MarkdownAlertIcons.Caution  , _dark ? "#F85149" : "#CF222E"),
                _ => null,
            };
        }

        /// <summary>
        /// Places the icon beside the alert title and renders the remaining blocks below it.
        /// </summary>
        /// <param name="node">The alert element.</param>
        /// <param name="geometry">The alert's icon geometry.</param>
        /// <param name="color">The color for the side rule, icon and title.</param>
        /// <returns>The bordered alert.</returns>
        private Border RenderAlert(HtmlNode node, Geometry geometry, string color)
        {
            var brush = Brush.Parse(color);
            var body = CreateStack();

            var titleNode = node.ChildNodes.FirstOrDefault(child => child.Name == "p"
                && Attribute(child, "class")?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                    .Contains("markdown-alert-title", StringComparer.Ordinal) == true);

            if (titleNode != null)
            {
                var title = CreateText(titleNode.ChildNodes);
                title.Foreground        = brush;
                title.FontWeight        = FontWeight.SemiBold;
                title.VerticalAlignment = VerticalAlignment.Center;

                // Keep the icon out of the text layout so it cannot move the title baseline.
                var icon = new PathIcon
                {
                    Data              = geometry,
                    Foreground        = brush,
                    Width             = 16,
                    Height            = 16,
                    VerticalAlignment = VerticalAlignment.Center,
                    IsHitTestVisible  = false,
                };

                icon.Classes.Add("markdown-alert-icon");

                var header = new Grid
                {
                    ColumnDefinitions = new ColumnDefinitions("Auto,*"),
                    ColumnSpacing     = 8
                };

                Grid.SetColumn(title, 1);

                header.Children.Add(icon);
                header.Children.Add(title);

                body.Children.Add(header);
            }

            AddBlocks(body, node.ChildNodes.Where(child => child != titleNode));

            var border = CreateQuoteBorder(body, brush);
            border.Classes.Add("markdown-alert");

            return border;
        }

        /// <summary>
        /// Creates a vertical panel with spacing between document blocks.
        /// </summary>
        private static StackPanel CreateStack() => new() { Spacing = 8 };

        /// <summary>
        /// Matches the alignment declaration permitted in a style attribute.
        /// </summary>
        [GeneratedRegex(@"(?:^|;)\s*text-align\s*:\s*(left|center|right)\s*(?=;|$)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
        private static partial Regex TextAlignPattern();

        #endregion
    }
}
