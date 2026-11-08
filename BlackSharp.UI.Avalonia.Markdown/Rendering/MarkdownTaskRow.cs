// SPDX-License-Identifier: MPL-2.0

using Avalonia;
using Avalonia.Controls;

namespace BlackSharp.UI.Avalonia.Markdown.Rendering
{
    /// <summary>
    /// Lays out a task checkbox next to the first line of its item.
    /// </summary>
    internal sealed class MarkdownTaskRow : Panel
    {
        #region Constructor

        /// <summary>
        /// Adds the checkbox and the list item body to the row.
        /// </summary>
        /// <param name="marker">The task state indicator.</param>
        /// <param name="body">The rendered blocks that make up the item.</param>
        internal MarkdownTaskRow(CheckBox marker, StackPanel body)
        {
            _marker = marker;
            _body   = body;

            Children.Add(marker);
            Children.Add(body);
        }

        #endregion

        #region Fields

        /// <summary>
        /// The horizontal space between the checkbox and the item body, in DIPs.
        /// </summary>
        private const double MarkerSpacing = 8;

        /// <summary>
        /// The native checkbox that indicates the task state.
        /// </summary>
        private readonly CheckBox _marker;

        /// <summary>
        /// The list item blocks, including wrapped text and nested lists.
        /// </summary>
        private readonly StackPanel _body;

        #endregion

        #region Protected

        /// <inheritdoc/>
        protected override Size MeasureOverride(Size availableSize)
        {
            _marker.Measure(availableSize);

            var markerSize = _marker.DesiredSize;
            var bodyLeft = markerSize.Width + MarkerSpacing;

            _body.Measure(new Size(Math.Max(0, availableSize.Width - bodyLeft), availableSize.Height));

            var bodySize = _body.DesiredSize;
            var centerOffset = GetFirstLineCenter() - markerSize.Height / 2;

            return new Size(bodyLeft + bodySize.Width, Math.Max(
                Math.Max(0,  centerOffset) + markerSize.Height,
                Math.Max(0, -centerOffset) + bodySize.Height));
        }

        /// <inheritdoc/>
        protected override Size ArrangeOverride(Size finalSize)
        {
            var markerSize = _marker.DesiredSize;

            var bodyLeft     = markerSize.Width + MarkerSpacing;
            var centerOffset = GetFirstLineCenter() - markerSize.Height / 2;

            // Offset the shorter first line or marker so both centers share the same row coordinate.
            _marker.Arrange(new Rect(0, Math.Max(0, centerOffset), markerSize.Width, markerSize.Height));

            _body.Arrange(new Rect(bodyLeft, Math.Max(0, -centerOffset),
                Math.Max(0, finalSize.Width - bodyLeft), _body.DesiredSize.Height));

            return finalSize;
        }

        #endregion

        #region Private

        /// <summary>
        /// Gets the center of the body's first text line, or the center of the first block if it is not text.
        /// </summary>
        /// <returns>The vertical distance in DIPs from the start of the body.</returns>
        private double GetFirstLineCenter()
        {
            var firstBlock = _body.Children.FirstOrDefault();

            if (firstBlock is TextBlock text && text.TextLayout.TextLines.Count > 0)
            {
                return text.Margin.Top + text.Padding.Top + text.TextLayout.TextLines[0].Height / 2;
            }

            return (firstBlock?.DesiredSize.Height ?? 0) / 2;
        }

        #endregion
    }
}
