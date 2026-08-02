using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Shapes;
using Windows.Foundation;

namespace Krate.Gui;

/// <summary>Plots y = f(x) on a Canvas using Core's <see cref="Calc.Plot"/>. Windows Calculator has
/// a graphing mode; this is the same idea over KRATE's own tested engine — no charting dependency.</summary>
public sealed partial class GraphPage : UserControl
{
    public GraphPage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Graph_Name");
    }

    void OnChanged(object sender, object e) => Draw();
    void OnResize(object sender, SizeChangedEventArgs e) => Draw();

    void Draw()
    {
        if (Plot is null) return; // ValueChanged on XMin/XMax fires mid-parse, before Plot exists
        var w = Plot.ActualWidth;
        var h = Plot.ActualHeight;
        Plot.Children.Clear();
        Error.Text = "";
        if (w < 2 || h < 2) return;
        Plot.Clip = new RectangleGeometry { Rect = new Rect(0, 0, w, h) }; // keep the curve inside the card

        double xMin = XMin.Value, xMax = XMax.Value;
        (double X, double Y)[] points;
        try { points = Calc.Plot(Fx.Text, xMin, xMax, (int)w); }
        catch (Exception ex) { Error.Text = ex.Message; return; }

        // Y range from the finite samples, padded so the curve isn't flush against the edges.
        var ys = points.Where(p => !double.IsNaN(p.Y)).Select(p => p.Y).ToArray();
        if (ys.Length == 0) { Error.Text = Strings.Get("Graph_NoPoints"); return; }
        double yMin = ys.Min(), yMax = ys.Max();
        if (yMax - yMin < 1e-9) { yMin -= 1; yMax += 1; } // a flat line still needs a visible band
        var pad = (yMax - yMin) * 0.1;
        yMin -= pad; yMax += pad;

        double Sx(double x) => (x - xMin) / (xMax - xMin) * w;
        double Sy(double y) => h - (y - yMin) / (yMax - yMin) * h;

        // Axes (drawn only when 0 falls inside the visible range).
        var axis = (Brush)Application.Current.Resources["ControlStrokeColorDefaultBrush"];
        if (yMin < 0 && yMax > 0) AddLine(0, Sy(0), w, Sy(0), axis, 1);
        if (xMin < 0 && xMax > 0) AddLine(Sx(0), 0, Sx(0), h, axis, 1);

        // The curve — one polyline per unbroken run, so gaps at undefined x stay gaps.
        var stroke = (Brush)Application.Current.Resources["AccentFillColorDefaultBrush"];
        var run = new PointCollection();
        foreach (var (x, y) in points)
        {
            if (double.IsNaN(y)) { Flush(run, stroke); run = new PointCollection(); continue; }
            run.Add(new Point(Sx(x), Sy(y)));
        }
        Flush(run, stroke);
    }

    void Flush(PointCollection run, Brush stroke)
    {
        if (run.Count < 2) return;
        Plot.Children.Add(new Polyline { Points = run, Stroke = stroke, StrokeThickness = 2 });
    }

    void AddLine(double x1, double y1, double x2, double y2, Brush stroke, double thickness) =>
        Plot.Children.Add(new Line { X1 = x1, Y1 = y1, X2 = x2, Y2 = y2, Stroke = stroke, StrokeThickness = thickness });
}
