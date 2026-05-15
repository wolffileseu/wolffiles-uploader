using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Windows.Foundation;

namespace WolffilesUploader.Controls;

public sealed class HorizontalWrapPanel : Panel
{
    public static readonly DependencyProperty HorizontalSpacingProperty =
        DependencyProperty.Register(nameof(HorizontalSpacing), typeof(double),
            typeof(HorizontalWrapPanel),
            new PropertyMetadata(0.0, (d, _) => ((HorizontalWrapPanel)d).InvalidateMeasure()));

    public static readonly DependencyProperty VerticalSpacingProperty =
        DependencyProperty.Register(nameof(VerticalSpacing), typeof(double),
            typeof(HorizontalWrapPanel),
            new PropertyMetadata(0.0, (d, _) => ((HorizontalWrapPanel)d).InvalidateMeasure()));

    public double HorizontalSpacing
    {
        get => (double)GetValue(HorizontalSpacingProperty);
        set => SetValue(HorizontalSpacingProperty, value);
    }

    public double VerticalSpacing
    {
        get => (double)GetValue(VerticalSpacingProperty);
        set => SetValue(VerticalSpacingProperty, value);
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        var maxW = double.IsInfinity(availableSize.Width) ? double.PositiveInfinity : availableSize.Width;
        double totalWidth = 0, totalHeight = 0;
        double rowWidth = 0, rowHeight = 0;

        foreach (var child in Children)
        {
            child.Measure(new Size(maxW, double.PositiveInfinity));
            var ds = child.DesiredSize;

            if (rowWidth > 0 && rowWidth + HorizontalSpacing + ds.Width > maxW)
            {
                totalWidth = System.Math.Max(totalWidth, rowWidth);
                totalHeight += rowHeight + VerticalSpacing;
                rowWidth = 0;
                rowHeight = 0;
            }

            if (rowWidth > 0) rowWidth += HorizontalSpacing;
            rowWidth += ds.Width;
            rowHeight = System.Math.Max(rowHeight, ds.Height);
        }

        totalWidth = System.Math.Max(totalWidth, rowWidth);
        totalHeight += rowHeight;
        return new Size(totalWidth, totalHeight);
    }

    protected override Size ArrangeOverride(Size finalSize)
    {
        double x = 0, y = 0, rowHeight = 0;

        foreach (var child in Children)
        {
            var ds = child.DesiredSize;

            if (x > 0 && x + HorizontalSpacing + ds.Width > finalSize.Width)
            {
                x = 0;
                y += rowHeight + VerticalSpacing;
                rowHeight = 0;
            }

            if (x > 0) x += HorizontalSpacing;
            child.Arrange(new Rect(x, y, ds.Width, ds.Height));
            x += ds.Width;
            rowHeight = System.Math.Max(rowHeight, ds.Height);
        }

        return finalSize;
    }
}
