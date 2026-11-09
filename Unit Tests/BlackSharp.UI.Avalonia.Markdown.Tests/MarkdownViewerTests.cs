// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Data;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Styling;
using Avalonia.VisualTree;
using BlackSharp.UI.Avalonia.Markdown.Controls;
using BlackSharp.UI.Avalonia.Markdown.Rendering;
using SkiaSharp;
using Xunit;

namespace BlackSharp.UI.Avalonia.Markdown.Tests
{
    /// <summary>
    /// Exercises preview construction and editing on Avalonia's headless platform.
    /// </summary>
    public sealed class MarkdownViewerTests
    {
        #region Public

        /// <summary>
        /// Checks that source edits update a bound control and refresh its preview.
        /// </summary>
        [AvaloniaFact]
        public async Task EditorUpdatesBoundTextAndPreview()
        {
            // Arrange
            var source = new TextBox { Text = "# Initial" };

            var viewer = new MarkdownViewer();
            viewer.Bind(MarkdownViewer.TextProperty, new Binding(nameof(TextBox.Text)) { Source = source });

            var window = Show(viewer);

            try
            {
                var editor = Editor(viewer);
                var editorWasHidden = !editor.IsVisible;
                viewer.ShowEditor = true;
                await WaitUntilAsync(() => Heading(viewer, "Initial") != null);
                editor.Focus();
                editor.SelectAll();

                // Act
                window.KeyTextInput("## Live preview");
                await WaitUntilAsync(() => Heading(viewer, "Live preview") != null);

                // Assert
                Assert.True(editorWasHidden);
                Assert.Equal("## Live preview", viewer.Text);
                Assert.Equal(viewer.Text, source.Text);
                Assert.Null(Heading(viewer, "Initial"));

                // Act
                source.Text = "# From the host";
                await WaitUntilAsync(() => Heading(viewer, "From the host") != null);
                viewer.IsReadOnly = true;
                editor.Focus();
                editor.SelectAll();
                window.KeyTextInput("This edit must be ignored.");

                // Assert
                Assert.Equal("# From the host", editor.Text);
                Assert.Equal("# From the host", editor.SelectedText);
                Assert.Equal(editor.Text, viewer.Text);

                Assert.True(editor.IsVisible);
                Assert.True(editor.IsReadOnly);
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that a background parse cannot replace a newer preview.
        /// </summary>
        [AvaloniaFact]
        public async Task BackgroundParseDoesNotReplaceNewerSource()
        {
            // Arrange
            var viewer = new MarkdownViewer
            {
                Text = "# Previous\n\n" + new string('a', 8192),
            };

            var window = Show(viewer);

            try
            {
                // Act
                viewer.Text = "# Current";
                await WaitUntilAsync(() => Heading(viewer, "Current") != null);

                // Assert
                Assert.Null(Heading(viewer, "Previous"));
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that rendering a cached task list keeps its checkbox after a theme change.
        /// </summary>
        [AvaloniaFact]
        public async Task ThemeChangeKeepsTaskListMarkers()
        {
            // Arrange
            var viewer = new GitHubMarkdownViewer { Text = "- [x] Complete" };
            var window = Show(viewer, ThemeVariant.Light);

            try
            {
                var marker = Assert.Single(viewer.GetVisualDescendants().OfType<CheckBox>());

                // Act
                window.RequestedThemeVariant = ThemeVariant.Dark;

                await WaitUntilAsync(() => viewer.GetVisualDescendants().OfType<CheckBox>()
                    .Any(current => !ReferenceEquals(current, marker)));

                // Assert
                Assert.True(Assert.Single(viewer.GetVisualDescendants().OfType<CheckBox>()).IsChecked);
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that the supplied HTML example centers the title and 128-pixel image.
        /// </summary>
        [AvaloniaFact]
        public void GitHubReadmeRendersCenteredHeadingAndSizedImage()
        {
            // Arrange
            var requested = new List<Uri>();

            var viewer = new GitHubMarkdownViewer
            {
                Text = "\uFEFF" + """
                    <h1 align="center">BlackSharp</h1>

                    <p align="center">
                        <img src="https://raw.githubusercontent.com/Blacktempel/BlackSharp/master/Assets/BlackSharpFull.png" width="128" height="128"/>
                    </p>
                    """,
                AllowRemoteImages = true,
                ImageLoader = (uri, _) =>
                {
                    requested.Add(uri);
                    return Task.FromResult<IImage?>(new DrawingImage());
                },
            };

            // Act
            var window = Show(viewer);
            try
            {
                // Assert
                Assert.Equal(TextAlignment.Center, Heading(viewer, "BlackSharp")?.TextAlignment);

                var image = Assert.Single(viewer.GetVisualDescendants().OfType<Image>());
                Assert.Equal(128, image.Width);
                Assert.Equal(128, image.Height);

                var paragraph = Assert.Single(image.GetVisualAncestors().OfType<SelectableTextBlock>());
                Assert.Equal(TextAlignment.Center, paragraph.TextAlignment);
                Assert.Equal("raw.githubusercontent.com", Assert.Single(requested).Host);
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that the GitHub dialect creates native table, alert, task and expander controls.
        /// </summary>
        [AvaloniaFact]
        public void GitHubExtensionsCreateNativeControls()
        {
            // Arrange
            var viewer = new GitHubMarkdownViewer
            {
                Text = """
                    | Name | Value |
                    | :--- | ---: |
                    | Foo | 10 |

                    > [!NOTE]
                    > Useful advice.

                    - [x] Complete
                    - [ ] Pending

                    <details>
                    <summary>More details</summary>

                    Hidden **content**.

                    </details>

                    ~~Removed~~ and :rocket:
                    """,
            };

            // Act
            var window = Show(viewer);
            try
            {
                // Assert
                var visuals = viewer.GetVisualDescendants().ToArray();
                var table = Assert.Single(visuals.OfType<Grid>(), grid => grid.Classes.Contains("markdown-table"));
                Assert.Equal(2, table.ColumnDefinitions.Count);
                Assert.Equal(TextAlignment.Right, Assert.Single(table.GetVisualDescendants()
                    .OfType<SelectableTextBlock>(), block => PlainText(block.Inlines!) == "10").TextAlignment);

                Assert.Single(visuals.OfType<Border>(), border => border.Classes.Contains("markdown-alert"));

                Assert.Equal(new bool?[] { true, false }, visuals.OfType<CheckBox>().Select(box => box.IsChecked));

                Assert.False(Assert.Single(visuals.OfType<Expander>()).IsExpanded);

                Assert.Contains(visuals.OfType<SelectableTextBlock>(), block => PlainText(block.Inlines!).Contains("🚀", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks the geometry, color and placement of each GitHub alert icon in both themes.
        /// </summary>
        /// <param name="dark">Whether to use the dark theme.</param>
        [AvaloniaTheory]
        [InlineData(false)]
        [InlineData(true)]
        public void GitHubAlertsDisplayTheirOcticons(bool dark)
        {
            // Arrange
            var viewer = new GitHubMarkdownViewer
            {
                Text = """
                    > [!NOTE]
                    > Note body

                    > [!TIP]
                    > Tip body

                    > [!IMPORTANT]
                    > Important body

                    > [!WARNING]
                    > Warning body

                    > [!CAUTION]
                    > Caution body

                    > Plain quotation
                    """,
            };

            var expectedIcons = new[]
            {
                MarkdownAlertIcons.Note,
                MarkdownAlertIcons.Tip,
                MarkdownAlertIcons.Important,
                MarkdownAlertIcons.Warning,
                MarkdownAlertIcons.Caution,
            };

            var titles = new[] { "Note", "Tip", "Important", "Warning", "Caution" };
            var window = Show(viewer, dark ? ThemeVariant.Dark : ThemeVariant.Light);

            try
            {
                // Act
                window.SetRenderScaling(1.5);

                using var frame = window.CaptureRenderedFrame();
                var visuals = viewer.GetVisualDescendants().ToArray();
                var icons = visuals.OfType<PathIcon>().Where(icon => icon.Classes.Contains("markdown-alert-icon")).ToArray();

                // Assert
                Assert.NotNull(frame);

                Assert.Equal(expectedIcons, icons.Select(icon => icon.Data));

                // Check each alert's title, body and icon against the rendered row and side rule.
                for (var index = 0; index < icons.Length; ++index)
                {
                    var icon = icons[index];

                    var border = Assert.Single(icon.GetVisualAncestors().OfType<Border>(),
                        candidate => candidate.Classes.Contains("markdown-alert"));

                    var header = Assert.IsType<Grid>(icon.Parent);

                    var title = Assert.Single(header.Children.OfType<SelectableTextBlock>());

                    Assert.Equal(titles[index], PlainText(title.Inlines!), ignoreCase: true);

                    Assert.Contains(border.GetVisualDescendants().OfType<SelectableTextBlock>(),
                        block => PlainText(block.Inlines!) == titles[index] + " body");

                    Assert.Same(border.BorderBrush, icon.Foreground);
                    Assert.Same(icon.Foreground, title.Foreground);

                    Assert.True(icon.Data!.Bounds.Width > 0 && icon.Data.Bounds.Height > 0);
                    Assert.True(icon.Bounds.Right < title.Bounds.Left);

                    Assert.InRange(Math.Abs(icon.Bounds.Center.Y - title.Bounds.Center.Y), 0, 1);
                }

                var quote = Assert.Single(visuals.OfType<SelectableTextBlock>(),
                    block => PlainText(block.Inlines!) == "Plain quotation");

                Assert.DoesNotContain(quote.GetVisualAncestors().OfType<Border>(),
                    border => border.Classes.Contains("markdown-alert"));
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that the standard viewer treats HTML as text and does not apply GitHub extensions.
        /// </summary>
        [AvaloniaFact]
        public void CommonMarkKeepsRawHtmlAndTableSourceAsText()
        {
            // Arrange
            var viewer = new MarkdownViewer
            {
                Text = """
                    <h1 align="center">Literal heading</h1>

                    | Column |
                    | --- |
                    | Value |
                    """,
            };

            // Act
            var window = Show(viewer);
            try
            {
                // Assert
                var visuals = viewer.GetVisualDescendants().ToArray();
                Assert.DoesNotContain(visuals.OfType<Grid>(), grid => grid.Classes.Contains("markdown-table"));
                Assert.DoesNotContain(visuals.OfType<SelectableTextBlock>(), block => block.Classes.Contains("markdown-heading"));

                Assert.Contains(visuals.OfType<SelectableTextBlock>(),
                    block => PlainText(block.Inlines!).Contains("<h1 align=\"center\">", StringComparison.Ordinal));
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that unsafe destinations and embedded content are inert and images require opt-in.
        /// </summary>
        [AvaloniaFact]
        public void UnsafeHtmlAndDestinationsAreNotInteractive()
        {
            // Arrange
            var requested = new List<Uri>();
            var activated = new List<Uri>();

            var viewer = new GitHubMarkdownViewer
            {
                Text = """
                    <script>Do not display me</script>

                    <iframe src="https://example.com">Do not display me</iframe>

                    <a href="javascript&#58;alert(1)">Unsafe link</a>

                    ![Remote fallback](https://example.com/pixel.png)

                    [File](file:///C:/Windows/win.ini) and [Safe link](docs/guide.md)
                    """,
                BaseUri = new Uri("https://example.com/project/"),
                ImageLoader = (uri, _) =>
                {
                    requested.Add(uri);
                    return Task.FromResult<IImage?>(null);
                },
            };

            viewer.LinkClicked += (_, e) => activated.Add(e.Uri);

            // Act
            var window = Show(viewer);
            try
            {
                var visuals = viewer.GetVisualDescendants().ToArray();
                var link = Assert.Single(visuals.OfType<HyperlinkButton>());
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                // Assert
                Assert.Equal(new Uri("https://example.com/project/docs/guide.md"), Assert.Single(activated));

                Assert.Empty(requested);

                Assert.Contains(visuals.OfType<SelectableTextBlock>(), block => PlainText(block.Inlines!) == "Remote fallback");
                Assert.DoesNotContain(visuals.OfType<SelectableTextBlock>(), block => PlainText(block.Inlines!).Contains("Do not display me"));
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that actual pointer clicks activate a link across its visible label.
        /// </summary>
        [AvaloniaFact]
        public void PointerClicksActivateTheWholeLinkLabel()
        {
            // Arrange
            var destination = new Uri("https://example.com/");
            var activated = new List<Uri>();
            var viewer = new MarkdownViewer { Text = $"Before [Open the example website]({destination}) after." };

            viewer.LinkClicked += (_, e) => activated.Add(e.Uri);

            var window = Show(viewer);

            try
            {
                window.SetRenderScaling(1.5);

                using var frame = window.CaptureRenderedFrame();
                var link = Assert.Single(viewer.GetVisualDescendants().OfType<HyperlinkButton>());
                var label = Assert.Single(link.GetVisualDescendants().OfType<TextBlock>());
                var points = new[] { new Point(0.1, 0.15), new Point(0.5, 0.5), new Point(0.9, 0.85) };

                // Act
                // Click positions across the rendered text, including its center.
                foreach (var point in points)
                {
                    var location = label.TranslatePoint(new Point(point.X * label.Bounds.Width, point.Y * label.Bounds.Height), window)!.Value;
                    window.MouseMove(location);
                    window.MouseDown(location, MouseButton.Left, RawInputModifiers.None);
                    window.MouseUp(location, MouseButton.Left, RawInputModifiers.None);
                }

                // Assert
                Assert.Equal(Enumerable.Repeat(destination, points.Length), activated);
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Compares the painted checkbox and first-line text pixels at the requested display scale.
        /// </summary>
        /// <param name="scaling">The headless window's display scale.</param>
        /// <param name="isChecked">Whether the Markdown task is checked.</param>
        /// <param name="wrap">Whether to use a label that wraps in a narrow window.</param>
        [AvaloniaTheory]
        [InlineData(1.0, true, false)]
        [InlineData(1.25, false, false)]
        [InlineData(1.5, true, false)]
        [InlineData(2.0, false, false)]
        [InlineData(1.25, true, true)]
        [InlineData(2.0, false, true)]
        public void TaskMarkerAlignsWithPaintedLabel(double scaling, bool isChecked, bool wrap)
        {
            // Arrange
            var text = isChecked ? "- [x] Native controls" : "- [ ] Try editing this list";

            if (wrap)
            {
                text += " with a second line that must not pull the marker down.";
            }

            var viewer = new GitHubMarkdownViewer
            {
                Text = text,
                Foreground = Brushes.White,
            };

            var window = Show(viewer, ThemeVariant.Dark, width: wrap ? 220 : 900);
            window.Background = Brushes.Black;

            try
            {
                // Act
                window.SetRenderScaling(scaling);
                using var frame = window.CaptureRenderedFrame();
                Assert.NotNull(frame);

                using var encoded = new MemoryStream();
                frame.Save(encoded, PngBitmapEncoderOptions.Default);

                encoded.Position = 0;
                using var bitmap = SKBitmap.Decode(encoded);

                var marker = Assert.Single(viewer.GetVisualDescendants().OfType<CheckBox>());
                var label = Assert.Single(viewer.GetVisualDescendants().OfType<SelectableTextBlock>());

                var markerOrigin = marker.TranslatePoint(default, window)!.Value;
                var labelOrigin = label.TranslatePoint(default, window)!.Value;

                var start = (int)Math.Floor(markerOrigin.X * scaling);
                var split = (int)Math.Ceiling((markerOrigin.X + marker.Bounds.Width) * scaling);
                var markerBottom = (int)Math.Ceiling((markerOrigin.Y + marker.Bounds.Height) * scaling);
                var firstLineBottom = (int)Math.Floor((labelOrigin.Y + label.Padding.Top + label.TextLayout.TextLines[0].Height) * scaling);

                var labelRight = Math.Min(bitmap.Width, split + (int)Math.Ceiling(160 * scaling));

                var markerRows = FindPaintedRows(bitmap, start, split, markerBottom);
                var labelRows = FindPaintedRows(bitmap, split, labelRight, firstLineBottom);

                // Assert
                Assert.Equal(wrap, label.TextLayout.TextLines.Count > 1);

                Assert.True(Math.Abs(markerRows.Center.Y - labelRows.Center.Y) <= Math.Ceiling(2 * scaling),
                    $"Checkbox pixels: {markerRows.Top}–{markerRows.Bottom}; text pixels: {labelRows.Top}–{labelRows.Bottom}.");
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that an unchanged image reuses its request and that removal cancels a pending request.
        /// </summary>
        [AvaloniaFact]
        public async Task PreviewReusesAndCancelsImageRequests()
        {
            // Arrange
            const string Image = "![Preview](images/preview.png)";
            var requests = new List<CancellationToken>();

            var viewer = new GitHubMarkdownViewer
            {
                Text = Image,
                ImageBaseUri = new Uri("https://example.com/"),
                AllowRemoteImages = true,
                ImageLoader = async (_, token) =>
                {
                    requests.Add(token);
                    await Task.Delay(Timeout.InfiniteTimeSpan, token);
                    return null;
                },
            };

            var window = Show(viewer);

            try
            {
                // Act
                viewer.Text = "# Edit\n\n" + Image;
                await WaitUntilAsync(() => Heading(viewer, "Edit") != null);

                var request = Assert.Single(requests);
                viewer.Text = "# Removed";
                await WaitUntilAsync(() => request.IsCancellationRequested);

                // Assert
                Assert.Single(requests);

                Assert.NotNull(Heading(viewer, "Removed"));
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that a GitHub heading fragment requests navigation inside the preview.
        /// </summary>
        [AvaloniaFact]
        public void FragmentLinkRequestsScrollToHeading()
        {
            // Arrange
            var viewer = new GitHubMarkdownViewer { Text = "[Jump](#jump-target)\n\n## Jump Target" };
            var window = Show(viewer);

            try
            {
                var target = Heading(viewer, "Jump Target");

                Visual? requestedTarget = null;
                viewer.AddHandler(Control.RequestBringIntoViewEvent, (_, e) => requestedTarget = e.TargetObject, handledEventsToo: true);

                var link = Assert.Single(viewer.GetVisualDescendants().OfType<HyperlinkButton>());

                // Act
                link.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));

                // Assert
                Assert.NotNull(target);
                Assert.Same(target, requestedTarget);
            }
            finally
            {
                window.Close();
            }
        }

        /// <summary>
        /// Checks that a picture selects its matching source when the theme changes.
        /// </summary>
        [AvaloniaFact]
        public async Task PictureUsesTheSourceForTheActiveTheme()
        {
            // Arrange
            var requested = new List<Uri>();

            var viewer = new GitHubMarkdownViewer
            {
                Text = """
                    <picture>
                      <source media="(prefers-color-scheme: dark)" srcset="dark.png">
                      <source media="(prefers-color-scheme: light)" srcset="light.png">
                      <img src="default.png" alt="Theme illustration">
                    </picture>
                    """,
                ImageBaseUri = new Uri("https://example.com/images/"),
                AllowRemoteImages = true,
                ImageLoader = (uri, _) =>
                {
                    requested.Add(uri);
                    return Task.FromResult<IImage?>(new DrawingImage());
                },
            };

            var window = Show(viewer, ThemeVariant.Light);

            try
            {
                // Act
                window.RequestedThemeVariant = ThemeVariant.Dark;
                await WaitUntilAsync(() => requested.Count == 2);

                // Assert
                Assert.Equal(new[] { "/images/light.png", "/images/dark.png" }, requested.Select(uri => uri.AbsolutePath));
            }
            finally
            {
                window.Close();
            }
        }

        #endregion

        #region Private

        /// <summary>
        /// Opens a headless window with the supplied viewer.
        /// </summary>
        /// <param name="viewer">The viewer hosted in the window.</param>
        /// <param name="theme">The requested theme, or null for the default.</param>
        /// <param name="width">The window width in DIPs.</param>
        /// <returns>The open headless window.</returns>
        private static Window Show(MarkdownViewer viewer, ThemeVariant? theme = null, double width = 900)
        {
            var window = new Window { Width = width, Height = 680, Content = viewer, RequestedThemeVariant = theme ?? ThemeVariant.Default };
            window.Show();

            return window;
        }

        /// <summary>
        /// Finds the source editor by its style class.
        /// </summary>
        private static TextBox Editor(MarkdownViewer viewer) => Assert.Single(viewer.GetVisualDescendants()
            .OfType<TextBox>(), box => box.Classes.Contains("markdown-editor"));

        /// <summary>
        /// Finds a rendered heading by its plain text.
        /// </summary>
        private static SelectableTextBlock? Heading(MarkdownViewer viewer, string value) => viewer.GetVisualDescendants()
            .OfType<SelectableTextBlock>()
            .FirstOrDefault(block => block.Classes.Contains("markdown-heading") && PlainText(block.Inlines!) == value);

        /// <summary>
        /// Reads the text of runs and nested spans.
        /// </summary>
        private static string PlainText(InlineCollection inlines) => string.Concat(inlines.Select(inline => inline switch
        {
            Run run => run.Text,
            Span span => PlainText(span.Inlines),
            _ => string.Empty,
        }));

        /// <summary>
        /// Finds the vertical bounds of the non-black pixels in the given columns.
        /// </summary>
        /// <param name="bitmap">The rendered frame on a black background.</param>
        /// <param name="left">The first column to examine.</param>
        /// <param name="right">The exclusive end column.</param>
        /// <param name="bottom">The exclusive end row.</param>
        /// <returns>A rectangle spanning the rows containing visible pixels.</returns>
        private static Rect FindPaintedRows(SKBitmap bitmap, int left, int right, int bottom)
        {
            var first = -1;
            var last = -1;

            // Record the first and last rows that contain a visible pixel in the selected columns.
            for (var y = 0; y < bottom; ++y)
            {
                // Scan this row for a pixel that contrasts with the black background.
                for (var x = left; x < right; ++x)
                {
                    var pixel = bitmap.GetPixel(x, y);
                    if (Math.Max(pixel.Red, Math.Max(pixel.Green, pixel.Blue)) < 64)
                    {
                        continue;
                    }

                    if (first < 0)
                    {
                        first = y;
                    }

                    last = y;
                    break;
                }
            }

            Assert.True(first >= 0, $"No painted pixels found in columns {left}–{right}.");
            return new Rect(left, first, right - left, last - first + 1);
        }

        /// <summary>
        /// Waits for a deferred change or fails if the predicate remains false.
        /// </summary>
        private static async Task WaitUntilAsync(Func<bool> condition)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));

            // Yield to the dispatcher between checks to let the preview timer and layout run.
            while (!condition())
            {
                await Task.Delay(25, timeout.Token);
            }
        }

        #endregion
    }
}
