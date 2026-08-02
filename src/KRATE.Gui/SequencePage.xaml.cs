using System.Globalization;
using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>Sequence generator with a type dropdown and parameter fields — over Core's <see cref="Maths.Sequence"/>.
/// Fibonacci and primes only need a count, so the start/step fields hide for them.</summary>
public sealed partial class SequencePage : UserControl
{
    // Display name → the keyword Core expects, and whether start/step apply.
    static readonly (string Name, string Key, bool Params)[] Kinds =
    [
        ("Fibonacci", "fib", false), ("Primes", "primes", false),
        ("Arithmetic", "arith", true), ("Geometric", "geom", true),
    ];

    public SequencePage()
    {
        InitializeComponent();
        Title.Text = Strings.Get("Tool_Sequence_Name");
        foreach (var k in Kinds) Kind.Items.Add(k.Name);
        Kind.SelectedIndex = 0;
    }

    void OnChanged(object sender, object e) => Generate();

    void Generate()
    {
        if (Output is null || Kind.SelectedIndex < 0) return;
        var kind = Kinds[Kind.SelectedIndex];

        // Step is labelled "Ratio" for geometric; both start/step hide for fib/primes.
        Start.Visibility = Step.Visibility = kind.Params ? Visibility.Visible : Visibility.Collapsed;
        Step.Header = kind.Key == "geom" ? Strings.Get("Seq_Ratio") : Strings.Get("Seq_Step");

        var count = (int)Count.Value;
        var input = kind.Params
            ? $"{kind.Key} {Fmt(Start.Value)} {Fmt(Step.Value)} {count}"
            : $"{kind.Key} {count}";
        try { Output.Text = Maths.Sequence(input); }
        catch (Exception ex) { Output.Text = ex.Message; }
    }

    static string Fmt(double v) => (double.IsNaN(v) ? 0 : v).ToString(CultureInfo.InvariantCulture);
}
