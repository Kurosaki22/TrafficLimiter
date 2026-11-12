using System.Globalization;
using System.Windows;
using System.Windows.Media;

namespace TrafficLimiter.App;

/// <summary>
/// Flächen-/Liniendiagramm für Down/Up-Raten. Weiche Kurven (Catmull-Rom), zeitbasiert gleitend
/// (läuft pro Frame weiter, nicht nur pro Messung) und mit animierter Y-Achse.
/// </summary>
public class RateChart : FrameworkElement
{
    public static readonly DependencyProperty SamplesProperty = DependencyProperty.Register(
        nameof(Samples), typeof(IReadOnlyList<Sample>), typeof(RateChart),
        new FrameworkPropertyMetadata(Array.Empty<Sample>(), FrameworkPropertyMetadataOptions.AffectsRender));

    public IReadOnlyList<Sample> Samples { get => (IReadOnlyList<Sample>)GetValue(SamplesProperty); set => SetValue(SamplesProperty, value); }

    static readonly Color DownColor = Color.FromRgb(0x3B, 0x82, 0xF6), UpColor = Color.FromRgb(0xF5, 0x9E, 0x0B);
    const double Delay = 0.5;       // Sekunden Verzögerung, damit der neueste Punkt sanft hereinläuft
    const double AxisHeight = 20;

    readonly Dictionary<string, FormattedText> _textCache = new();
    double _max;                    // aktuell angezeigtes Maximum (animiert)
    bool _hooked;

    public RateChart()
    {
        IsHitTestVisible = false;
        Loaded += (_, _) => { if (!_hooked) { CompositionTarget.Rendering += OnFrame; _hooked = true; } };
        Unloaded += (_, _) => { if (_hooked) { CompositionTarget.Rendering -= OnFrame; _hooked = false; } };
    }

    void OnFrame(object? s, EventArgs e) { if (IsVisible) InvalidateVisual(); }

    Brush Res(string key, Brush fallback) => TryFindResource(key) as Brush ?? fallback;

    FormattedText Text(string s, Brush brush, double size)
    {
        if (_textCache.Count > 64) _textCache.Clear();
        string k = s + size;
        if (!_textCache.TryGetValue(k, out var ft))
            _textCache[k] = ft = new FormattedText(s, CultureInfo.CurrentUICulture, FlowDirection.LeftToRight,
                new Typeface("Segoe UI Variable Text"), size, brush, VisualTreeHelper.GetDpi(this).PixelsPerDip);
        return ft;
    }

    /// <summary>Nächste "runde" Obergrenze (1/2/5 × 10^n).</summary>
    static double Nice(double v)
    {
        double p = Math.Pow(10, Math.Floor(Math.Log10(v)));
        double f = v / p;
        return (f <= 1 ? 1 : f <= 2 ? 2 : f <= 5 ? 5 : 10) * p;
    }

    protected override void OnRender(DrawingContext dc)
    {
        double w = ActualWidth, h = ActualHeight;
        if (w < 4 || h < 4) return;
        var samples = Samples;
        const double span = 60, top = 20;
        double plotH = h - AxisHeight, bottom = plotH;
        double tRight = Clock.Now - Delay;

        // Y-Achse: weich auf das nächste runde Maximum der sichtbaren Werte nachführen
        long peak = 0;
        foreach (var s in samples) if (s.T >= tRight - span) peak = Math.Max(peak, Math.Max(s.Down, s.Up));
        double target = Nice(Math.Max(peak * 1.15, 20 * 1024));
        _max = _max <= 0 ? target : _max + (target - _max) * 0.08;

        double X(double t) => w - (tRight - t) / span * w;
        double Y(long v) => bottom - Math.Min(v / _max, 1.05) * (bottom - top);

        dc.PushClip(new RectangleGeometry(new Rect(0, 0, w, h)));

        var gridPen = new Pen(Res("ChartGrid", Brushes.Gray), 1);
        var muted = Res("TextMuted", Brushes.Gray);
        for (int i = 1; i <= 4; i++)
        {
            double y = bottom - i / 4.0 * (bottom - top);
            dc.DrawLine(gridPen, new Point(0, y), new Point(w, y));
            dc.DrawText(Text(Fmt.Bytes((long)(_max * i / 4)) + "/s", muted, 11), new Point(4, Math.Max(0, y - 16)));
        }
        dc.DrawLine(gridPen, new Point(0, bottom), new Point(w, bottom));
        dc.DrawText(Text("−60 s", muted, 11), new Point(4, plotH + 3));
        dc.DrawText(Text("−30 s", muted, 11), new Point(w / 2 - 14, plotH + 3));
        var now = Text(Loc.T("chart_now"), muted, 11);
        dc.DrawText(now, new Point(w - now.Width - 4, plotH + 3));

        if (samples.Count >= 2)
        {
            Draw(dc, samples, s => s.Up, UpColor, X, Y, top, bottom);
            Draw(dc, samples, s => s.Down, DownColor, X, Y, top, bottom);
        }
        dc.Pop();
    }

    static void Draw(DrawingContext dc, IReadOnlyList<Sample> samples, Func<Sample, long> sel, Color color,
        Func<double, double> x, Func<long, double> y, double top, double bottom)
    {
        var pts = new Point[samples.Count];
        for (int i = 0; i < pts.Length; i++) pts[i] = new Point(x(samples[i].T), y(sel(samples[i])));

        var line = new StreamGeometry();
        using (var c = line.Open()) { c.BeginFigure(pts[0], false, false); Curve(c, pts, bottom); }
        var fill = new StreamGeometry();
        using (var c = fill.Open())
        {
            c.BeginFigure(new Point(pts[0].X, bottom), true, true);
            c.LineTo(pts[0], false, false);
            Curve(c, pts, bottom);
            c.LineTo(new Point(pts[^1].X, bottom), false, false);
        }
        var grad = new LinearGradientBrush(Color.FromArgb(110, color.R, color.G, color.B),
            Color.FromArgb(0, color.R, color.G, color.B), 90)
        { MappingMode = BrushMappingMode.Absolute, StartPoint = new Point(0, top), EndPoint = new Point(0, bottom) };
        dc.DrawGeometry(grad, null, fill);
        dc.DrawGeometry(null, new Pen(new SolidColorBrush(color), 2.2)
            { LineJoin = PenLineJoin.Round, StartLineCap = PenLineCap.Round, EndLineCap = PenLineCap.Round }, line);
    }

    /// <summary>Catmull-Rom-Spline als kubische Béziers; Kontrollpunkte bleiben über der Basislinie.</summary>
    static void Curve(StreamGeometryContext c, Point[] p, double bottom)
    {
        for (int i = 0; i < p.Length - 1; i++)
        {
            var p0 = p[Math.Max(i - 1, 0)]; var p1 = p[i]; var p2 = p[i + 1]; var p3 = p[Math.Min(i + 2, p.Length - 1)];
            var c1 = new Point(p1.X + (p2.X - p0.X) / 6, Math.Min(p1.Y + (p2.Y - p0.Y) / 6, bottom));
            var c2 = new Point(p2.X - (p3.X - p1.X) / 6, Math.Min(p2.Y - (p3.Y - p1.Y) / 6, bottom));
            c.BezierTo(c1, c2, p2, true, true);
        }
    }
}
