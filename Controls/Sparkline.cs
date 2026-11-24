using System;
using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace AiBoxDashboard.Controls;

/// <summary>A small line chart of recent readings; the newest reading sits at the right edge.</summary>
public sealed class Sparkline : Control
{
    public static readonly StyledProperty<IReadOnlyList<double>?> ValuesProperty =
        AvaloniaProperty.Register<Sparkline, IReadOnlyList<double>?>(nameof(Values));

    public static readonly StyledProperty<double> MinimumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Minimum));

    public static readonly StyledProperty<double> MaximumProperty =
        AvaloniaProperty.Register<Sparkline, double>(nameof(Maximum), 100);

    public static readonly StyledProperty<int> CapacityProperty =
        AvaloniaProperty.Register<Sparkline, int>(nameof(Capacity), 120);

    public static readonly StyledProperty<IBrush?> StrokeProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Stroke), Brushes.Gray);

    public static readonly StyledProperty<IBrush?> FillProperty =
        AvaloniaProperty.Register<Sparkline, IBrush?>(nameof(Fill));

    static Sparkline()
    {
        AffectsRender<Sparkline>(ValuesProperty, MinimumProperty, MaximumProperty, CapacityProperty, StrokeProperty, FillProperty);
    }

    public IReadOnlyList<double>? Values
    {
        get => GetValue(ValuesProperty);
        set => SetValue(ValuesProperty, value);
    }

    public double Minimum
    {
        get => GetValue(MinimumProperty);
        set => SetValue(MinimumProperty, value);
    }

    public double Maximum
    {
        get => GetValue(MaximumProperty);
        set => SetValue(MaximumProperty, value);
    }

    /// <summary>How many readings span the full width.</summary>
    public int Capacity
    {
        get => GetValue(CapacityProperty);
        set => SetValue(CapacityProperty, value);
    }

    public IBrush? Stroke
    {
        get => GetValue(StrokeProperty);
        set => SetValue(StrokeProperty, value);
    }

    public IBrush? Fill
    {
        get => GetValue(FillProperty);
        set => SetValue(FillProperty, value);
    }

    public override void Render(DrawingContext context)
    {
        var values = Values;
        var width = Bounds.Width;
        var height = Bounds.Height;
        if (values is null || values.Count < 2 || width <= 0 || height <= 2)
            return;

        var min = Minimum;
        var range = Math.Max(Maximum - min, 1e-9);
        var step = width / Math.Max(Capacity - 1, 1);
        var left = width - (values.Count - 1) * step;

        Point At(int i)
        {
            var share = Math.Clamp((values[i] - min) / range, 0, 1);
            return new Point(left + i * step, height - 1 - share * (height - 2));
        }

        if (Fill is { } fill)
        {
            var area = new StreamGeometry();
            using (var g = area.Open())
            {
                g.BeginFigure(new Point(left, height), true);
                for (var i = 0; i < values.Count; i++)
                    g.LineTo(At(i));
                g.LineTo(new Point(width, height));
                g.EndFigure(true);
            }

            context.DrawGeometry(fill, null, area);
        }

        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(At(0), false);
            for (var i = 1; i < values.Count; i++)
                g.LineTo(At(i));
            g.EndFigure(false);
        }

        context.DrawGeometry(null, new Pen(Stroke, 1.5) { LineJoin = PenLineJoin.Round }, line);
    }
}
