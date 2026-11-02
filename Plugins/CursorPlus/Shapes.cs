using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace CursorPlusApp;

// Картинка курсора: пиксели BGRA (прозрачность обычная, не «предумноженная») и точка, которой курсор «целится» (hot spot).
sealed record CurImg(int W, int H, int HotX, int HotY, byte[] Bgra);

// Готовые формы курсора, нарисованные кодом (векторно, на любой размер), и перевод картинок.
static class Shapes
{
    public sealed record ShapeInfo(string Id, string Title);

    public static readonly ShapeInfo[] All =
    {
        new("windows", "Как в Windows"), new("classic", "Классика"), new("round", "Мягкая"), new("neon", "Неон"),
        new("dot", "Точка"), new("ring", "Кольцо"), new("cross", "Прицел"), new("ghost", "Призрак"), new("star", "Звезда"),
    };

    public static readonly int[] Sizes = { 32, 48, 64, 96, 128, 192, 256 };

    // стрелка в клетке 32×32
    static readonly Point[] Arrow = { new(2, 1.5), new(2, 20.5), new(6.6, 16.6), new(10, 24.5), new(13, 23.2), new(9.6, 15.5), new(15.6, 15.5) };

    // куда «целится» форма, в долях картинки
    public static (double X, double Y) Hot(string shape) => shape switch
    {
        "classic" or "round" or "neon" => (2.0 / 32, 1.5 / 32),
        "ghost" => (7.0 / 32, 4.0 / 32),
        _ => (0.5, 0.5),
    };

    public static bool IsProcedural(string shape) => shape is "classic" or "round" or "neon" or "dot" or "ring" or "cross" or "ghost" or "star";

    static Color C((int R, int G, int B) c, int a = 255) => Color.FromArgb((byte)a, (byte)c.R, (byte)c.G, (byte)c.B);
    static Brush B((int R, int G, int B) c, int a = 255) => new SolidColorBrush(C(c, a));
    static Pen P((int R, int G, int B) c, double w, int a = 255, bool round = true) =>
        new(B(c, a), w) { LineJoin = round ? PenLineJoin.Round : PenLineJoin.Miter, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round };

    static Geometry Poly(IEnumerable<Point> pts, bool close = true)
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            var list = pts.ToList();
            ctx.BeginFigure(list[0], true, close);
            ctx.PolyLineTo(list.Skip(1).ToList(), true, true);
        }
        g.Freeze();
        return g;
    }

    static Geometry GhostBody()
    {
        var g = new StreamGeometry();
        using (var ctx = g.Open())
        {
            ctx.BeginFigure(new Point(5, 28), true, true);
            ctx.LineTo(new Point(5, 14), true, true);
            ctx.ArcTo(new Point(27, 14), new Size(11, 11), 0, false, SweepDirection.Clockwise, true, true);
            ctx.LineTo(new Point(27, 28), true, true);
            ctx.PolyLineTo(new List<Point> { new(23.33, 24.6), new(19.67, 28), new(16, 24.6), new(12.33, 28), new(8.67, 24.6) }, true, true);
        }
        g.Freeze();
        return g;
    }

    static Geometry StarGeom()
    {
        var pts = new List<Point>();
        for (var i = 0; i < 10; i++)
        {
            var r = i % 2 == 0 ? 12.8 : 5.4;
            var a = -Math.PI / 2 + i * Math.PI / 5;
            pts.Add(new Point(16 + r * Math.Cos(a), 16.8 + r * Math.Sin(a)));
        }
        return Poly(pts);
    }

    static void Draw(DrawingContext dc, string shape, (int R, int G, int B) fill, (int R, int G, int B) line)
    {
        switch (shape)
        {
            case "classic":
                dc.DrawGeometry(null, P(line, 2.8, 255, false), Poly(Arrow));   // сначала обводка, потом заливка поверх: обводка видна только снаружи
                dc.DrawGeometry(B(fill), null, Poly(Arrow));
                break;
            case "round":
                dc.DrawGeometry(null, P(line, 3.4), Poly(Arrow));
                dc.DrawGeometry(B(fill), null, Poly(Arrow));
                break;
            case "neon":
            {
                var g = Poly(Arrow);
                dc.DrawGeometry(B(fill, 46), null, g);
                foreach (var (w, a) in new[] { (8.0, 26), (5.6, 46), (3.6, 90), (2.2, 255) })
                    dc.DrawGeometry(null, P(fill, w, a), g);
                dc.DrawGeometry(null, P(Col.Mix(fill, (255, 255, 255), 0.6), 1.0), g);
                break;
            }
            case "dot":
                dc.DrawEllipse(null, P(line, 3.2), new Point(16, 16), 6.6, 6.6);
                dc.DrawEllipse(B(fill), null, new Point(16, 16), 6.6, 6.6);
                dc.DrawEllipse(B(Col.Mix(fill, (255, 255, 255), 0.55), 150), null, new Point(14.2, 14.2), 2.2, 2.2);
                break;
            case "ring":
                dc.DrawEllipse(null, P(line, 6.4), new Point(16, 16), 9.2, 9.2);
                dc.DrawEllipse(null, P(fill, 3.0), new Point(16, 16), 9.2, 9.2);
                dc.DrawEllipse(B(fill), P(line, 1.6), new Point(16, 16), 2.2, 2.2);
                break;
            case "cross":
                foreach (var (w, c) in new[] { (5.4, line), (2.6, fill) })
                {
                    var pen = P(c, w);
                    dc.DrawLine(pen, new Point(3, 16), new Point(12, 16));
                    dc.DrawLine(pen, new Point(20, 16), new Point(29, 16));
                    dc.DrawLine(pen, new Point(16, 3), new Point(16, 12));
                    dc.DrawLine(pen, new Point(16, 20), new Point(16, 29));
                }
                dc.DrawEllipse(B(fill), P(line, 1.4), new Point(16, 16), 1.7, 1.7);
                break;
            case "ghost":
                dc.DrawGeometry(null, P(line, 2.8), GhostBody());
                dc.DrawGeometry(B(fill), null, GhostBody());
                dc.DrawEllipse(B(line), null, new Point(12, 14), 1.9, 2.8);
                dc.DrawEllipse(B(line), null, new Point(20, 14), 1.9, 2.8);
                break;
            case "star":
                dc.DrawGeometry(null, P(line, 2.8), StarGeom());
                dc.DrawGeometry(B(fill), null, StarGeom());
                break;
            // указатели, которые Windows рисует «наоборот» (текст, точный выбор) или красным (недоступно): рисуем сами в цвете курсора
            case "ibeam":
                foreach (var (w, c) in new[] { (5.0, line), (2.2, fill) })
                {
                    var pen = P(c, w);
                    dc.DrawLine(pen, new Point(16, 5.5), new Point(16, 26.5));
                    dc.DrawLine(pen, new Point(11, 5.5), new Point(21, 5.5));
                    dc.DrawLine(pen, new Point(11, 26.5), new Point(21, 26.5));
                }
                break;
            case "crosshair":
                foreach (var (w, c) in new[] { (5.0, line), (2.2, fill) })
                {
                    var pen = P(c, w);
                    dc.DrawLine(pen, new Point(2.5, 16), new Point(13, 16));
                    dc.DrawLine(pen, new Point(19, 16), new Point(29.5, 16));
                    dc.DrawLine(pen, new Point(16, 2.5), new Point(16, 13));
                    dc.DrawLine(pen, new Point(16, 19), new Point(16, 29.5));
                }
                break;
            case "no":
                dc.DrawEllipse(null, P(line, 6.2), new Point(16, 16), 10, 10);
                dc.DrawEllipse(null, P(fill, 3.0), new Point(16, 16), 10, 10);
                dc.DrawLine(P(line, 5.8), new Point(9.4, 9.4), new Point(22.6, 22.6));
                dc.DrawLine(P(fill, 2.8), new Point(9.4, 9.4), new Point(22.6, 22.6));
                break;
        }
    }

    // Форма нужного размера (квадрат px × px). null — для «windows» и «свой» это делают другие классы.
    public static CurImg? Render(string shape, int px, (int R, int G, int B) fill, (int R, int G, int B) line)
    {
        if (!IsProcedural(shape) && shape is not ("ibeam" or "crosshair" or "no")) return null;
        var k = px / 32.0;
        var dv = new DrawingVisual();
        using (var dc = dv.RenderOpen())
        {
            dc.PushTransform(new ScaleTransform(k, k));
            Draw(dc, shape, fill, line);
            dc.Pop();
        }
        var rtb = new RenderTargetBitmap(px, px, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        var (hx, hy) = Hot(shape);
        return new CurImg(px, px, (int)Math.Round(hx * px), (int)Math.Round(hy * px), Img.Straight(rtb));
    }
}

// Работа с картинками: пиксели, масштаб, картинка для окна.
static class Img
{
    // BitmapSource (любой формат) → пиксели BGRA с обычной прозрачностью
    public static byte[] Straight(BitmapSource src)
    {
        var c = src.Format == PixelFormats.Bgra32 ? src : new FormatConvertedBitmap(src, PixelFormats.Bgra32, null, 0);
        var stride = c.PixelWidth * 4;
        var data = new byte[stride * c.PixelHeight];
        c.CopyPixels(data, stride, 0);
        return data;
    }

    public static BitmapSource ToSource(CurImg img)
    {
        var bmp = BitmapSource.Create(img.W, img.H, 96, 96, PixelFormats.Bgra32, null, img.Bgra, img.W * 4);
        bmp.Freeze();
        return bmp;
    }

    public static BitmapSource FromPixels(byte[] bgra, int w, int h)
    {
        var bmp = BitmapSource.Create(w, h, 96, 96, PixelFormats.Bgra32, null, bgra, w * 4);
        bmp.Freeze();
        return bmp;
    }

    // Вписать картинку в квадрат size × size (по центру). smooth — плавное сглаживание, иначе «пиксельно» (ближайший пиксель).
    public static (byte[] Bgra, double Scale, double OffX, double OffY) Fit(BitmapSource src, int size, bool smooth)
    {
        var scale = Math.Min((double)size / src.PixelWidth, (double)size / src.PixelHeight);
        var w = src.PixelWidth * scale; var h = src.PixelHeight * scale;
        var ox = (size - w) / 2; var oy = (size - h) / 2;
        var dv = new DrawingVisual();
        RenderOptions.SetBitmapScalingMode(dv, smooth ? BitmapScalingMode.HighQuality : BitmapScalingMode.NearestNeighbor);
        using (var dc = dv.RenderOpen()) dc.DrawImage(src, new Rect(ox, oy, w, h));
        var rtb = new RenderTargetBitmap(size, size, 96, 96, PixelFormats.Pbgra32);
        rtb.Render(dv);
        return (Straight(rtb), scale, ox, oy);
    }
}
