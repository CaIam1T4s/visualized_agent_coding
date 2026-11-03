using System;
using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

namespace happy_agent_coding.Controls;

/// <summary>Blueprint node control used while the old block-named control remains as a compatibility entry.</summary>
public sealed class BlueprintNodeControl : Control
{
    private static readonly Typeface BlockTypeface =
        new("Inter", FontStyle.Normal, FontWeight.SemiBold);
    private static readonly Typeface ParameterTypeface =
        new("Inter", FontStyle.Normal, FontWeight.Medium);
    private static readonly IBrush DefaultFill = new SolidColorBrush(Color.Parse("#4C97FF"));
    private static readonly IBrush DefaultText = new SolidColorBrush(Colors.White);
    private static readonly IBrush ParameterFill = new SolidColorBrush(Color.Parse("#F5F7FA"));
    private static readonly IBrush ParameterText = new SolidColorBrush(Color.Parse("#20242A"));

    public static readonly StyledProperty<string> PrefixProperty =
        AvaloniaProperty.Register<BlueprintNodeControl, string>(nameof(Prefix), "移动");
    public static readonly StyledProperty<string> ParameterProperty =
        AvaloniaProperty.Register<BlueprintNodeControl, string>(nameof(Parameter), "10");
    public static readonly StyledProperty<string> SuffixProperty =
        AvaloniaProperty.Register<BlueprintNodeControl, string>(nameof(Suffix), "步");
    public static readonly StyledProperty<IBrush> FillProperty =
        AvaloniaProperty.Register<BlueprintNodeControl, IBrush>(nameof(Fill), DefaultFill);
    public static readonly StyledProperty<IBrush> TextBrushProperty =
        AvaloniaProperty.Register<BlueprintNodeControl, IBrush>(nameof(TextBrush), DefaultText);

    static BlueprintNodeControl()
    {
        AffectsMeasure<BlueprintNodeControl>(PrefixProperty, ParameterProperty, SuffixProperty);
        AffectsRender<BlueprintNodeControl>(FillProperty, TextBrushProperty);
    }

    public string Prefix { get => GetValue(PrefixProperty); set => SetValue(PrefixProperty, value); }
    public string Parameter { get => GetValue(ParameterProperty); set => SetValue(ParameterProperty, value); }
    public string Suffix { get => GetValue(SuffixProperty); set => SetValue(SuffixProperty, value); }
    public IBrush Fill { get => GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public IBrush TextBrush { get => GetValue(TextBrushProperty); set => SetValue(TextBrushProperty, value); }

    protected override Size MeasureOverride(Size availableSize)
    {
        var prefix = Format(Prefix, BlockTypeface, TextBrush);
        var parameter = Format(Parameter, ParameterTypeface, ParameterText);
        var suffix = Format(Suffix, BlockTypeface, TextBrush);
        var width = 28 + prefix.Width + (string.IsNullOrEmpty(Parameter) ? 0 : parameter.Width + 22) + suffix.Width;
        return new Size(Math.Max(128, width), 46);
    }

    public override void Render(DrawingContext context)
    {
        var width = Math.Max(128, Bounds.Width);
        var bodyHeight = Math.Max(32, Bounds.Height - 4);
        context.DrawGeometry(Fill, new Pen(new SolidColorBrush(Color.Parse("#D9E6FF")), 1),
            CreateBlockGeometry(width, bodyHeight));

        var x = 14.0;
        var prefix = Format(Prefix, BlockTypeface, TextBrush);
        var suffix = Format(Suffix, BlockTypeface, TextBrush);
        context.DrawText(prefix, new Point(x, (bodyHeight - prefix.Height) / 2));
        x += prefix.Width + 4;

        if (!string.IsNullOrEmpty(Parameter))
        {
            var parameter = Format(Parameter, ParameterTypeface, ParameterText);
            var slot = new Rect(x, (bodyHeight - parameter.Height) / 2 - 4, parameter.Width + 14, parameter.Height + 8);
            context.DrawRectangle(ParameterFill, null, new RoundedRect(slot, 5));
            context.DrawText(parameter, new Point(slot.X + 7, slot.Y + 4));
            x = slot.Right + 4;
        }

        context.DrawText(suffix, new Point(x, (bodyHeight - suffix.Height) / 2));
    }

    private static FormattedText Format(string text, Typeface typeface, IBrush brush) =>
        new(text, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight, typeface, 14, brush);

    private static StreamGeometry CreateBlockGeometry(double width, double height)
    {
        var geometry = new StreamGeometry();
        using var context = geometry.Open();
        var radius = 8.0;
        var notch = Math.Min(width / 2, 9.0);
        var center = width / 2;
        context.BeginFigure(new Point(radius, 0), true);
        context.LineTo(new Point(center - notch, 0), true);
        context.LineTo(new Point(center - notch + 4, 5), true);
        context.LineTo(new Point(center + notch - 4, 5), true);
        context.LineTo(new Point(center + notch, 0), true);
        context.LineTo(new Point(width - radius, 0), true);
        context.QuadraticBezierTo(new Point(width, 0), new Point(width, radius), true);
        context.LineTo(new Point(width, height - radius), true);
        context.QuadraticBezierTo(new Point(width, height), new Point(width - radius, height), true);
        context.LineTo(new Point(center + notch, height), true);
        context.LineTo(new Point(center + notch - 4, height + 4), true);
        context.LineTo(new Point(center - notch + 4, height + 4), true);
        context.LineTo(new Point(center - notch, height), true);
        context.LineTo(new Point(radius, height), true);
        context.QuadraticBezierTo(new Point(0, height), new Point(0, height - radius), true);
        context.LineTo(new Point(0, radius), true);
        context.QuadraticBezierTo(new Point(0, 0), new Point(radius, 0), true);
        context.EndFigure(true);
        return geometry;
    }
}
