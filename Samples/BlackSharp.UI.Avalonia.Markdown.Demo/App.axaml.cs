// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace BlackSharp.UI.Avalonia.Markdown.Demo
{
    /// <summary>
    /// Loads the sample's theme and opens its window.
    /// </summary>
    internal sealed partial class App : Application
    {
        #region Public

        /// <inheritdoc/>
        public override void Initialize()
        {
            AvaloniaXamlLoader.Load(this);
        }

        /// <inheritdoc/>
        public override void OnFrameworkInitializationCompleted()
        {
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.MainWindow = new MainWindow { DataContext = new DemoViewModel() };
            }

            base.OnFrameworkInitializationCompleted();
        }

        #endregion
    }
}
