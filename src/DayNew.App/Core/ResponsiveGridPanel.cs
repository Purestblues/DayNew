using System.Windows;
using System.Windows.Controls;

namespace DayNew.Core;

/// <summary>
/// 自适应 Bento 网格面板：按可用宽度与最小列宽计算总列数，
/// 支持子项通过 ColumnSpan 跨越多列并按列水位线错落排布（单列等高时退化为常规等距网格）。
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

    public static readonly DependencyProperty ColumnSpanProperty =
        DependencyProperty.RegisterAttached(
            "ColumnSpan",
            typeof(int),
            typeof(ResponsiveGridPanel),
            new FrameworkPropertyMetadata(1, FrameworkPropertyMetadataOptions.AffectsParentMeasure));

    public static int GetColumnSpan(DependencyObject obj) => (int)obj.GetValue(ColumnSpanProperty);

    public static void SetColumnSpan(DependencyObject obj, int value) => obj.SetValue(ColumnSpanProperty, value);

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
        var colHeights = new double[cols];
        var anyPlaced = false;

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;

            var span = Math.Clamp(GetColumnSpan(child), 1, cols);
            var slotWidth = span * itemWidth + Math.Max(0, span - 1) * colGap;
            child.Measure(new Size(slotWidth, double.PositiveInfinity));

            var (bestCol, bestY) = FindBestSlot(colHeights, span);
            var nextY = bestY + child.DesiredSize.Height + rowGap;
            for (var k = 0; k < span; k++)
                colHeights[bestCol + k] = nextY;

            anyPlaced = true;
        }

        var totalHeight = anyPlaced ? Math.Max(0.0, colHeights.Max() - rowGap) : 0.0;
        var totalWidth = double.IsInfinity(availableSize.Width)
            ? cols * itemWidth + Math.Max(0, cols - 1) * colGap
            : availableSize.Width;

        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        var colGap = Math.Max(0.0, ColumnGap);
        var rowGap = Math.Max(0.0, RowGap);
        var (cols, itemWidth) = ComputeLayout(finalSize.Width);
        var colHeights = new double[cols];

        foreach (UIElement child in InternalChildren)
        {
            if (child.Visibility == Visibility.Collapsed) continue;

            var span = Math.Clamp(GetColumnSpan(child), 1, cols);
            var slotWidth = span * itemWidth + Math.Max(0, span - 1) * colGap;
            var (bestCol, bestY) = FindBestSlot(colHeights, span);

            var x = bestCol * (itemWidth + colGap);
            var h = child.DesiredSize.Height;
            child.Arrange(new Rect(x, bestY, slotWidth, h));

            var nextY = bestY + h + rowGap;
            for (var k = 0; k < span; k++)
                colHeights[bestCol + k] = nextY;
        }

        return finalSize;
    }

    private static (int BestCol, double BestY) FindBestSlot(double[] colHeights, int span)
    {
        var bestCol = 0;
        var bestY = double.PositiveInfinity;
        var limit = Math.Max(0, colHeights.Length - span);

        for (var c = 0; c <= limit; c++)
        {
            var maxH = 0.0;
            for (var k = 0; k < span; k++)
                maxH = Math.Max(maxH, colHeights[c + k]);

            if (maxH < bestY - 0.5)
            {
                bestY = maxH;
                bestCol = c;
            }
        }

        return (bestCol, double.IsInfinity(bestY) ? 0.0 : bestY);
    }
}
