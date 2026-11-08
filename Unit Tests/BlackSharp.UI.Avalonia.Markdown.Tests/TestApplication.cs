// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Headless;
using Avalonia.Themes.Fluent;
using BlackSharp.UI.Avalonia.Markdown.Tests;

[assembly: AvaloniaTestApplication(typeof(TestApplication))]

namespace BlackSharp.UI.Avalonia.Markdown.Tests
{
    /// <summary>
    /// Provides Avalonia's headless platform, Skia rendering and the Fluent theme to UI tests.
    /// </summary>
    public sealed class TestApplication : Application
    {
        #region Public

        /// <summary>
        /// Configures the application to run.
        /// </summary>
        /// <returns>The configured builder.</returns>
        public static AppBuilder BuildAvaloniaApp()
        {
            return AppBuilder.Configure<TestApplication>()
                .UseSkia()
                .UseHeadless(new AvaloniaHeadlessPlatformOptions { UseHeadlessDrawing = false });
        }

        /// <inheritdoc/>
        public override void Initialize()
        {
            Styles.Add(new FluentTheme());
        }

        #endregion
    }
}
