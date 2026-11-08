// SPDX-License-Identifier: MPL-2.0

using BlackSharp.MVVM.ComponentModel;

namespace BlackSharp.UI.Avalonia.Markdown.Demo
{
    /// <summary>
    /// Holds the example document and its preview options.
    /// </summary>
    internal sealed class DemoViewModel : ViewModelBase
    {
        #region Properties

        private string _text = """
            <h1 align="center">BlackSharp</h1>

            <p align="center">
                <img src="https://raw.githubusercontent.com/Blacktempel/BlackSharp/master/Assets/BlackSharpFull.png" width="128" height="128" alt="BlackSharp logo"/>
            </p>

            ## Release notes

            Edit this source to see the preview update. The **CommonMark** tab shows the same source with the standard dialect.

            > [!NOTE]
            > GitHub alerts, tables, tasks, and safe README HTML are available here. :rocket:

            > [!TIP]
            > Edit the shared source to compare both previews.

            > [!IMPORTANT]
            > The editor and preview use the same source text.

            > [!WARNING]
            > Remote images are not loaded unless enabled.

            > [!CAUTION]
            > Only enable remote images for content you trust.

            | Component | Status |
            | --- | :---: |
            | Preview | Live |
            | Editor | Ready |

            - [x] Native controls
            - [ ] Try editing this list

            <details>
            <summary>Show a code example</summary>

            ```csharp
            viewer.ShowEditor = true;
            viewer.IsReadOnly = false;
            viewer.Text = "## Release notes";
            ```

            </details>

            [BlackSharp on GitHub](https://github.com/Blacktempel/BlackSharp)
            """;

        /// <summary>
        /// Gets or sets the Markdown source shared by both tabs.
        /// </summary>
        public string Text
        {
            get => _text;
            set => SetField(ref _text, value);
        }

        private bool _isReadOnly;

        /// <summary>
        /// Gets or sets whether the shared source editor prevents edits.
        /// </summary>
        public bool IsReadOnly
        {
            get => _isReadOnly;
            set => SetField(ref _isReadOnly, value);
        }

        private bool _allowRemoteImages;

        /// <summary>
        /// Gets or sets whether the example can load remote images.
        /// </summary>
        public bool AllowRemoteImages
        {
            get => _allowRemoteImages;
            set => SetField(ref _allowRemoteImages, value);
        }

        #endregion
    }
}
