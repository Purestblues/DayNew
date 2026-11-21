using System.Windows;
using System.Windows.Controls;

namespace DayNew.Core;

/// <summary>
/// 自适应等宽网格面板：按可用宽度与最小列宽计算列数，
/// 并将剩余空间均分到每一列，消除右侧多余留白。
/// </summary>
public sealed class ResponsiveGridPanel : Panel
{
    public static readonly DependencyProperty MinColumnWidthProperty =
        DependencyProperty.Register(
            nameof(MinColumnWidth),
            typeof(double),
            typeof(ResponsiveGridPanel),
            new FrameworkPropertyMetadata(240.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty ColumnGapProperty =
        DependencyProperty.Register(
            nameof(ColumnGap),
            typeof(double),
            typeof(ResponsiveGridPanel),
            new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public static readonly DependencyProperty RowGapProperty =
        DependencyProperty.Register(
            nameof(RowGap),
            typeof(double),
            typeof(ResponsiveGridPanel),
            new FrameworkPropertyMetadata(10.0, FrameworkPropertyMetadataOptions.AffectsMeasure));

    public double MinColumnWidth
    {
        get => (double)GetValue(MinColumnWidthProperty);
        set => SetValue(MinColumnWidthProperty, value);
    }

    public double ColumnGap
    {
        get => (double)GetValue(ColumnGapProperty);
        set => SetValue(ColumnGapProperty, value);
    }

    public double RowGap
    {
        get => (double)GetValue(RowGapProperty);
        set => SetValue(RowGapProperty, value);
    }

    private (int Columns, double ItemWidth) ComputeLayout(double availableWidth)
    {
        var minW = Math.Max(1.0, MinColumnWidth);
        var colGap = Math.Max(0.0, ColumnGap);

        if (availableWidth <= 0 || double.IsInfinity(availableWidth) || double.IsNaN(availableWidth))
            return (1, minW);

        var cols = Math.Max(1, (int)Math.Floor((availableWidth + colGap) / (minW + colGap)));
        var itemWidth = Math.Max(1.0, (availableWidth - (cols - 1) * colGap) / cols);
        return (cols, itemWidth);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var visibleCount = 0;
        foreach (UIElement child in InternalChildren)
            if (child.Visibility != Visibility.Collapsed) visibleCount++;

        if (visibleCount == 0) return default;

        var colGap = Math.Max(0.0, ColumnGap);
        var rowGap = Math.Max(0.0, RowGap);
        var (cols, itemWidth) = ComputeLayout(availableSize.Width);
        var childConstraint = new Size(itemWidth, double.PositiveInfinity);

        var col = 0;
        var rowHeight = 0.0;
        var totalHeight = 0.0;
        var rowCount = 0;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;

            child.Measure(childConstraint);
            rowHeight = Math.Max(rowHeight, child.DesiredSize.Height);
            col++;

            if (col == cols)
            {
                if (rowCount > 0) totalHeight += rowGap;
                totalHeight += rowHeight;
                rowCount++;
                col = 0;
                rowHeight = 0.0;
            }
        }

        if (col > 0)
        {
            if (rowCount > 0) totalHeight += rowGap;
            totalHeight += rowHeight;
        }

        var usedCols = double.IsInfinity(availableSize.Width) ? Math.Min(cols, visibleCount) : cols;
        var totalWidth = double.IsInfinity(availableSize.Width)
            ? usedCols * itemWidth + Math.Max(0, usedCols - 1) * colGap
            : availableSize.Width;

        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var colGap = Math.Max(0.0, ColumnGap);
        var rowGap = Math.Max(0.0, RowGap);
        var (cols, itemWidth) = ComputeLayout(finalSize.Width);

        var rowChildren = new List<UIElement>(cols);
        var y = 0.0;

        void FlushRow()
        {
            if (rowChildren.Count == 0) return;

            var rowHeight = 0.0;
            foreach (var item in rowChildren)
                rowHeight = Math.Max(rowHeight, item.DesiredSize.Height);

            for (var i = 0; i < rowChildren.Count; i++)
            {
                var item = rowChildren[i];
                var x = i * (itemWidth + colGap);
                item.Arrange(new Rect(x, y, itemWidth, item.DesiredSize.Height));
            }

            y += rowHeight + rowGap;
            rowChildren.Clear();
        }

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;

            rowChildren.Add(child);
            if (rowChildren.Count == cols) FlushRow();
        }

        FlushRow();
        return finalSize;
    }
}
