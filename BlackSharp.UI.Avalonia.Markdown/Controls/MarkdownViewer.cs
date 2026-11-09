// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Media;
using Avalonia.Styling;
using Avalonia.Threading;
using BlackSharp.Core.Logging;
using BlackSharp.UI.Avalonia.Markdown.Events;
using BlackSharp.UI.Avalonia.Markdown.Rendering;
using HtmlAgilityPack;

namespace BlackSharp.UI.Avalonia.Markdown.Controls
{
    /// <summary>
    /// Displays CommonMark in a native Avalonia preview and optionally provides an editable source pane.
    /// </summary>
    public class MarkdownViewer : UserControl
    {
        #region Constructor

        /// <summary>
        /// Creates the source editor and the scrollable preview.
        /// </summary>
        public MarkdownViewer()
        {
            _editor = new TextBox
            {
                AcceptsReturn = true,
                TextWrapping  = TextWrapping.Wrap,
                FontFamily    = new FontFamily("Consolas, Menlo, monospace"),
                MinHeight     = 0,
            };

            _editor.Classes.Add("markdown-editor");
            _editor.Bind(TextBox.TextProperty, new Binding(nameof(Text))
            {
                Source = this,
                Mode   = BindingMode.TwoWay,
            });

            _editor.Bind(TextBox.IsReadOnlyProperty     , new Binding(nameof(IsReadOnly     )) { Source = this, Mode = BindingMode.OneWay });
            _editor.Bind(TextBox.PlaceholderTextProperty, new Binding(nameof(EditorWatermark)) { Source = this });

            _preview = new ScrollViewer
            {
                HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
            };

            _preview.Classes.Add("markdown-preview");

            _layout = new Grid { ColumnSpacing = 12, RowSpacing = 12 };
            _layout.Children.Add(_editor );
            _layout.Children.Add(_preview);

            Content = _layout;

            _renderTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(120) };
            _renderTimer.Tick += OnRenderTick;

            SizeChanged               += (_, _) => UpdateLayoutMode();
            ActualThemeVariantChanged += (_, _) => ScheduleRender();

            UpdateLayoutMode();
        }

        #endregion

        #region Fields

        /// <summary>
        /// Caps the source passed to the preview parser, in UTF-16 code units.
        /// </summary>
        private const int MaximumPreviewSourceLength = 256 * 1024;

        /// <summary>
        /// Caps the number of parsed nodes visited by the native preview renderer.
        /// </summary>
        private const int MaximumPreviewNodeCount = 4096;

        /// <summary>
        /// Caps the raw text displayed after a parsing or rendering failure.
        /// </summary>
        private const int MaximumFallbackTextLength = 4096;

        /// <summary>
        /// Sets the largest source length parsed synchronously, in UTF-16 code units.
        /// </summary>
        private const int SynchronousParseSourceLength = 4096;

        /// <summary>
        /// Lays out the source editor and preview according to the available width.
        /// </summary>
        private readonly Grid _layout;

        /// <summary>
        /// Holds the editable Markdown source.
        /// </summary>
        private readonly TextBox _editor;

        /// <summary>
        /// Scrolls the rendered document.
        /// </summary>
        private readonly ScrollViewer _preview;

        /// <summary>
        /// Coalesces consecutive source and presentation changes.
        /// </summary>
        private readonly DispatcherTimer _renderTimer;

        /// <summary>
        /// Shares image requests between previews that refer to the same destinations.
        /// </summary>
        private readonly MarkdownImageStore _images = new();

        /// <summary>
        /// Indicates that the viewer can render in a visual tree.
        /// </summary>
        private bool _isAttached;

        /// <summary>
        /// Tracks whether a document is being parsed on a background thread.
        /// </summary>
        private bool _isParsing;

        /// <summary>
        /// Holds the source of the last parsed document, including documents rejected by the node limit.
        /// </summary>
        private string? _parsedSource;

        /// <summary>
        /// Holds the Markdown dialect used for the cached document.
        /// </summary>
        private bool _parsedUseGitHubSyntax;

        /// <summary>
        /// Holds the cached document node, or null when the parsed document exceeds the node limit.
        /// </summary>
        private HtmlNode? _parsedDocument;

        /// <summary>
        /// Cancels assignments to image controls in the previous preview.
        /// </summary>
        private CancellationTokenSource? _previewLifetime;

        #endregion

        #region Avalonia Properties

        /// <summary>
        /// Defines the <see cref="Text"/> property.
        /// </summary>
        public static readonly StyledProperty<string?> TextProperty =
            AvaloniaProperty.Register<MarkdownViewer, string?>(nameof(Text), defaultBindingMode: BindingMode.TwoWay);

        /// <summary>
        /// Defines the <see cref="IsReadOnly"/> property.
        /// </summary>
        public static readonly StyledProperty<bool> IsReadOnlyProperty =
            AvaloniaProperty.Register<MarkdownViewer, bool>(nameof(IsReadOnly));

        /// <summary>
        /// Defines the <see cref="ShowEditor"/> property.
        /// </summary>
        public static readonly StyledProperty<bool> ShowEditorProperty =
            AvaloniaProperty.Register<MarkdownViewer, bool>(nameof(ShowEditor));

        /// <summary>
        /// Defines the <see cref="EditorWatermark"/> property.
        /// </summary>
        public static readonly StyledProperty<string?> EditorWatermarkProperty =
            AvaloniaProperty.Register<MarkdownViewer, string?>(nameof(EditorWatermark));

        /// <summary>
        /// Defines the <see cref="BaseUri"/> property.
        /// </summary>
        public static readonly StyledProperty<Uri?> BaseUriProperty =
            AvaloniaProperty.Register<MarkdownViewer, Uri?>(nameof(BaseUri));

        /// <summary>
        /// Defines the <see cref="ImageBaseUri"/> property.
        /// </summary>
        public static readonly StyledProperty<Uri?> ImageBaseUriProperty =
            AvaloniaProperty.Register<MarkdownViewer, Uri?>(nameof(ImageBaseUri));

        /// <summary>
        /// Defines the <see cref="AllowRemoteImages"/> property.
        /// </summary>
        public static readonly StyledProperty<bool> AllowRemoteImagesProperty =
            AvaloniaProperty.Register<MarkdownViewer, bool>(nameof(AllowRemoteImages));

        /// <summary>
        /// Defines the <see cref="ImageLoader"/> property.
        /// </summary>
        public static readonly StyledProperty<Func<Uri, CancellationToken, Task<IImage?>>?> ImageLoaderProperty =
            AvaloniaProperty.Register<MarkdownViewer, Func<Uri, CancellationToken, Task<IImage?>>?>(nameof(ImageLoader));

        #endregion

        #region Properties

        /// <summary>
        /// Gets or sets the Markdown source. Null is displayed as an empty document.
        /// </summary>
        public string? Text
        {
            get => GetValue(TextProperty);
            set => SetValue(TextProperty, value);
        }

        /// <summary>
        /// Gets or sets whether the source editor prevents edits. The default is false.
        /// </summary>
        public bool IsReadOnly
        {
            get => GetValue(IsReadOnlyProperty);
            set => SetValue(IsReadOnlyProperty, value);
        }

        /// <summary>
        /// Gets or sets whether the source editor is displayed next to the preview. The default is false.
        /// </summary>
        public bool ShowEditor
        {
            get => GetValue(ShowEditorProperty);
            set => SetValue(ShowEditorProperty, value);
        }

        /// <summary>
        /// Gets or sets the optional placeholder shown by an empty source editor.
        /// </summary>
        public string? EditorWatermark
        {
            get => GetValue(EditorWatermarkProperty);
            set => SetValue(EditorWatermarkProperty, value);
        }

        /// <summary>
        /// Gets or sets the absolute base URI used to resolve relative links and, by default, images.
        /// </summary>
        public Uri? BaseUri
        {
            get => GetValue(BaseUriProperty);
            set => SetValue(BaseUriProperty, value);
        }

        /// <summary>
        /// Gets or sets the absolute base URI used to resolve relative images. Null uses <see cref="BaseUri"/>.
        /// </summary>
        public Uri? ImageBaseUri
        {
            get => GetValue(ImageBaseUriProperty);
            set => SetValue(ImageBaseUriProperty, value);
        }

        /// <summary>
        /// Gets or sets whether the preview may resolve and load HTTPS images. The default is false.
        /// </summary>
        public bool AllowRemoteImages
        {
            get => GetValue(AllowRemoteImagesProperty);
            set => SetValue(AllowRemoteImagesProperty, value);
        }

        /// <summary>
        /// Gets or sets an optional HTTPS image provider. When unset, the built-in raster loader is used.
        /// </summary>
        /// <remarks>
        /// The delegate receives the resolved URI and a cancellation token. It can return null to keep the alt text visible.
        /// The caller owns any returned image and must keep it alive while the viewer uses it.
        /// </remarks>
        public Func<Uri, CancellationToken, Task<IImage?>>? ImageLoader
        {
            get => GetValue(ImageLoaderProperty);
            set => SetValue(ImageLoaderProperty, value);
        }

        /// <summary>
        /// Gets or sets the optional logger for render and image load failures.
        /// </summary>
        public ILogger? Logger { get; set; }

        /// <summary>
        /// Gets whether to enable GitHub Markdown extensions and process safe HTML elements.
        /// </summary>
        protected virtual bool UseGitHubSyntax => false;

        #endregion

        #region Protected

        /// <inheritdoc/>
        protected override void OnPropertyChanged(AvaloniaPropertyChangedEventArgs change)
        {
            base.OnPropertyChanged(change);

            if (change.Property == ShowEditorProperty && _layout != null)
            {
                UpdateLayoutMode();
            }
            else if (change.Property == TextProperty
                  || change.Property == BaseUriProperty
                  || change.Property == ImageBaseUriProperty)
            {
                ScheduleRender();
            }
            else if (change.Property == AllowRemoteImagesProperty
                  || change.Property == ImageLoaderProperty)
            {
                if (_isAttached)
                {
                    ClearPreview();
                    Render();
                }
            }
        }

        /// <inheritdoc/>
        protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
        {
            base.OnAttachedToVisualTree(e);
            _isAttached = true;

            Render();
        }

        /// <inheritdoc/>
        protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
        {
            _isAttached = false;
            _renderTimer.Stop();

            _parsedSource   = null;
            _parsedDocument = null;

            ClearPreview();
            base.OnDetachedFromVisualTree(e);
        }

        #endregion

        #region Private

        /// <summary>
        /// Shows the preview alone or arranges the editor and preview side by side or vertically.
        /// </summary>
        private void UpdateLayoutMode()
        {
            var showEditor = ShowEditor;
            _editor.IsVisible = showEditor;

            var stacked = showEditor && Bounds.Width < 680;

            _layout.ColumnDefinitions = new ColumnDefinitions(!showEditor || stacked ? "*" : "*,*");
            _layout.RowDefinitions    = new RowDefinitions(stacked ? "*,*" : "*");

            Grid.SetRow   (_preview, stacked ? 1 : 0);
            Grid.SetColumn(_preview, !showEditor || stacked ? 0 : 1);
        }

        /// <summary>
        /// Restarts the preview delay while the viewer is attached.
        /// </summary>
        private void ScheduleRender()
        {
            if (_isAttached)
            {
                _renderTimer.Stop();
                _renderTimer.Start();
            }
        }

        /// <summary>
        /// Stops the delay and renders the latest source.
        /// </summary>
        /// <param name="sender">The dispatcher timer.</param>
        /// <param name="e">The timer event data.</param>
        private void OnRenderTick(object? sender, EventArgs e)
        {
            _renderTimer.Stop();
            Render();
        }

        /// <summary>
        /// Uses a cached parse or parses the current Markdown source for the preview.
        /// </summary>
        private void Render()
        {
            _renderTimer.Stop();

            if (!_isAttached || _isParsing)
            {
                return;
            }

            var text = Text ?? string.Empty;

            var useGitHubSyntax = UseGitHubSyntax;

            if (text.Length > MaximumPreviewSourceLength)
            {
                _parsedSource   = null;
                _parsedDocument = null;
                ShowPreviewLimit();
                return;
            }

            if (_parsedUseGitHubSyntax == useGitHubSyntax && string.Equals(_parsedSource, text, StringComparison.Ordinal))
            {
                RenderDocument(_parsedDocument, text);
                return;
            }

            _parsedSource   = null;
            _parsedDocument = null;

            if (text.Length > SynchronousParseSourceLength)
            {
                _isParsing = true;
                _ = ParseAndRenderAsync(text, useGitHubSyntax);
                return;
            }

            try
            {
                CacheAndRenderDocument(ParseDocument(text, useGitHubSyntax), text, useGitHubSyntax);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ShowPreviewFailure(text, exception);
            }
        }

        /// <summary>
        /// Parses one document in the background and renders it if its source and dialect still match.
        /// </summary>
        /// <param name="text">The source to parse.</param>
        /// <param name="useGitHubSyntax">Whether to parse GitHub Markdown extensions.</param>
        /// <returns>A task that completes after the dispatcher handles the parse result.</returns>
        private async Task ParseAndRenderAsync(string text, bool useGitHubSyntax)
        {
            HtmlNode?  document = null;
            Exception? error    = null;

            try
            {
                document = await Task.Run(() => ParseDocument(text, useGitHubSyntax)).ConfigureAwait(false);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                error = exception;
            }

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                _isParsing = false;

                if (!_isAttached)
                {
                    return;
                }

                if (!string.Equals(Text ?? string.Empty, text, StringComparison.Ordinal) || UseGitHubSyntax != useGitHubSyntax)
                {
                    // A timer may already be waiting for the current editor input to settle.
                    if (!_renderTimer.IsEnabled)
                    {
                        Render();
                    }

                    return;
                }

                _renderTimer.Stop();

                if (error != null)
                {
                    ShowPreviewFailure(text, error);
                }
                else
                {
                    CacheAndRenderDocument(document, text, useGitHubSyntax);
                }
            });
        }

        /// <summary>
        /// Parses the source and rejects documents that exceed the preview's node limit.
        /// </summary>
        /// <param name="text">The source to parse.</param>
        /// <param name="useGitHubSyntax">Whether to parse GitHub Markdown extensions.</param>
        /// <returns>The parsed document, or null when it exceeds the node limit.</returns>
        private static HtmlNode? ParseDocument(string text, bool useGitHubSyntax)
        {
            var document = MarkdownDocumentParser.Parse(text, useGitHubSyntax);

            return document.Descendants().Take(MaximumPreviewNodeCount + 1).Count() > MaximumPreviewNodeCount
                 ? null
                 : document;
        }

        /// <summary>
        /// Retains the parse result for presentation changes and renders the document.
        /// </summary>
        /// <param name="document">The parsed document, or null when it exceeds the node limit.</param>
        /// <param name="text">The original Markdown source.</param>
        /// <param name="useGitHubSyntax">The dialect used to parse the source.</param>
        private void CacheAndRenderDocument(HtmlNode? document, string text, bool useGitHubSyntax)
        {
            _parsedDocument        = document;
            _parsedSource          = text;
            _parsedUseGitHubSyntax = useGitHubSyntax;

            RenderDocument(document, text);
        }

        /// <summary>
        /// Builds the Avalonia preview and retains requests for unchanged image destinations.
        /// </summary>
        /// <param name="document">The parsed document, or null when it exceeds the node limit.</param>
        /// <param name="text">The original Markdown source used for the error fallback.</param>
        private void RenderDocument(HtmlNode? document, string text)
        {
            if (document == null)
            {
                ShowPreviewLimit();
                return;
            }

            CancelPreviousPreview();

            try
            {
                _previewLifetime = new CancellationTokenSource();

                var renderer = new MarkdownRenderer(
                    _preview, BaseUri, ImageBaseUri ?? BaseUri, AllowRemoteImages, ActualThemeVariant == ThemeVariant.Dark,
                    uri => _images.GetAsync(uri, ImageLoader, Logger),
                    uri => LinkClicked?.Invoke(this, new MarkdownLinkClickedEventArgs(uri)), _previewLifetime.Token);

                _preview.Content = renderer.Render(document);
                _images.Retain(renderer.ImageUris);
            }
            catch (Exception exception) when (exception is not OutOfMemoryException)
            {
                ShowPreviewFailure(text, exception);
            }
        }

        /// <summary>
        /// Releases the preview, displays a fallback and logs the failure.
        /// </summary>
        /// <param name="text">The source to show if it fits the fallback limit.</param>
        /// <param name="exception">The parsing or rendering error.</param>
        private void ShowPreviewFailure(string text, Exception exception)
        {
            ClearPreview();

            _preview.Content = new SelectableTextBlock
            {
                Text         = text.Length <= MaximumFallbackTextLength ? text : "Markdown preview unavailable.",
                TextWrapping = TextWrapping.Wrap,
            };

            Logger?.Log(LogLevel.Warn, nameof(MarkdownViewer), "The Markdown preview could not be rendered.", exception);
        }

        /// <summary>
        /// Releases the previous preview and displays the preview budget message.
        /// </summary>
        private void ShowPreviewLimit()
        {
            ClearPreview();

            _preview.Content = new TextBlock
            {
                Text         = "This document exceeds the Markdown preview limit.",
                TextWrapping = TextWrapping.Wrap
            };
        }

        /// <summary>
        /// Removes the preview and releases image requests and owned bitmaps.
        /// </summary>
        private void ClearPreview()
        {
            CancelPreviousPreview();

            _preview.Content = null;
            _images.Clear();
        }

        /// <summary>
        /// Stops asynchronous updates to the previous preview.
        /// </summary>
        private void CancelPreviousPreview()
        {
            _previewLifetime?.Cancel();
            _previewLifetime?.Dispose();
            _previewLifetime = null;
        }

        #endregion

        #region Events

        /// <summary>
        /// Occurs when an HTTP, HTTPS or mail link is activated. The host decides whether to open it.
        /// </summary>
        public event EventHandler<MarkdownLinkClickedEventArgs>? LinkClicked;

        #endregion
    }
}
