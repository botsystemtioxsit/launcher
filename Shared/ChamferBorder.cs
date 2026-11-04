using System.Windows;
using System.Windows.Media;

namespace GhostShellUi;

/// <summary>
/// Прямоугольник со «срезанными» углами (сверху слева и снизу справа) — основа киберпанк-плашек. Ничего не содержит: кладётся
/// в Grid под текст. Срез в пикселях (не растёт с размером), толщина линии — Thickness. Общий файл для приложений (ссылкой).
/// </summary>
public sealed class ChamferBorder : FrameworkElement
{
    public static readonly DependencyProperty FillProperty = DependencyProperty.Register(nameof(Fill), typeof(Brush), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty StrokeProperty = DependencyProperty.Register(nameof(Stroke), typeof(Brush), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(null, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty ThicknessProperty = DependencyProperty.Register(nameof(Thickness), typeof(double), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(1.0, FrameworkPropertyMetadataOptions.AffectsRender));
    public static readonly DependencyProperty CutProperty = DependencyProperty.Register(nameof(Cut), typeof(double), typeof(ChamferBorder),
        new FrameworkPropertyMetadata(6.0, FrameworkPropertyMetadataOptions.AffectsRender));

    public Brush? Fill { get => (Brush?)GetValue(FillProperty); set => SetValue(FillProperty, value); }
    public Brush? Stroke { get => (Brush?)GetValue(StrokeProperty); set => SetValue(StrokeProperty, value); }
    public double Thickness { get => (double)GetValue(ThicknessProperty); set => SetValue(ThicknessProperty, value); }
    public double Cut { get => (double)GetValue(CutProperty); set => SetValue(CutProperty, value); }

    public ChamferBorder() { IsHitTestVisible = false; SnapsToDevicePixels = true; }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 2 || h < 2) return;
        double t = Math.Max(0, Thickness), o = t / 2, c = Math.Max(0, Math.Min(Cut, Math.Min(w, h) / 2));
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(c, o), true, true);
            ctx.LineTo(new Point(w - o, o), true, false);
            ctx.LineTo(new Point(w - o, h - c), true, false);
            ctx.LineTo(new Point(w - c, h - o), true, false);
            ctx.LineTo(new Point(o, h - o), true, false);
            ctx.LineTo(new Point(o, c), true, false);
        }
        g.Freeze();
        dc.DrawGeometry(Fill, Stroke != null && t > 0 ? new Pen(Stroke, t) : null, g);
    }
}
