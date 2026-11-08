// SPDX-License-Identifier: MPL-2.0

using BlackSharp.UI.Avalonia.Controls;
using BlackSharp.UI.Avalonia.Markdown.Events;

namespace BlackSharp.UI.Avalonia.Markdown.Demo
{
    /// <summary>
    /// Hosts a shared source editor and the GitHub Markdown and CommonMark previews.
    /// </summary>
    internal sealed partial class MainWindow : DecoratedWindow
    {
        #region Constructor

        /// <summary>
        /// Loads the sample window.
        /// </summary>
        public MainWindow()
        {
            InitializeComponent();
        }

        #endregion

        #region Private

        /// <summary>
        /// Opens a user-activated link through the desktop launcher.
        /// </summary>
        private async void OnLinkClicked(object? sender, MarkdownLinkClickedEventArgs e)
        {
            await Launcher.LaunchUriAsync(e.Uri);
        }

        #endregion
    }
}
