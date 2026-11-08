// SPDX-License-Identifier: MPL-2.0

using Avalonia;

namespace BlackSharp.UI.Avalonia.Markdown.Demo
{
    /// <summary>
    /// Starts the desktop sample.
    /// </summary>
    internal static class Program
    {
        #region Private

        /// <summary>
        /// Starts Avalonia with the current platform backend.
        /// </summary>
        [STAThread]
        private static void Main(string[] args)
        {
            AppBuilder.Configure<App>()
                .UsePlatformDetect()
                .StartWithClassicDesktopLifetime(args);
        }

        #endregion
    }
}
