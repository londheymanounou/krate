using Krate.Core;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Krate.Gui;

/// <summary>One reusable numeric-form page (title, up to three labelled NumberBoxes, a result card).
/// BMI, tip and loan are just different configs — no three near-identical pages.</summary>
public sealed partial class CalcFormPage : UserControl
{
    readonly NumberBox[] _boxes;
    readonly int _count;
    readonly Func<double[], string> _compute;

    public CalcFormPage(string titleKey, (string LabelKey, double Default)[] fields, Func<double[], string> compute)
    {
        InitializeComponent();
        Title.Text = Strings.Get(titleKey);
        _compute = compute;
        _boxes = [A, B, C];
        _count = fields.Length;

        for (var i = 0; i < _boxes.Length; i++)
        {
            if (i < _count) { _boxes[i].Header = Strings.Get(fields[i].LabelKey); _boxes[i].Value = fields[i].Default; }
            else _boxes[i].Visibility = Visibility.Collapsed;
        }
        Update();
    }

    void OnChanged(NumberBox sender, NumberBoxValueChangedEventArgs args) => Update();

    void Update()
    {
        var values = _boxes.Take(_count).Select(b => double.IsNaN(b.Value) ? 0 : b.Value).ToArray();
        try { Result.Text = _compute(values); }
        catch (Exception ex) { Result.Text = ex.Message; }
    }

    // Join values with '.' decimals whatever the UI culture — Core parses invariantly.
    static string Args(double[] v) => string.Join(' ', v.Select(x => x.ToString(System.Globalization.CultureInfo.InvariantCulture)));

    // The three configs, so the registry stays a one-liner each.
    public static CalcFormPage Bmi() => new("Tool_Bmi_Name",
        [("Bmi_Weight", 70), ("Bmi_Height", 175)], v => Everyday.Bmi(Args(v)));

    public static CalcFormPage Tip() => new("Tool_Tip_Name",
        [("Tip_Bill", 50), ("Tip_Percent", 15), ("Tip_People", 2)], v => Everyday.Tip(Args(v)));

    public static CalcFormPage Loan() => new("Tool_Loan_Name",
        [("Loan_Amount", 200000), ("Loan_Rate", 3.5), ("Loan_Years", 25)], v => Everyday.Loan(Args(v)));

    public static CalcFormPage Combinatorics() => new("Tool_Combinatorics_Name",
        [("Comb_N", 5), ("Comb_R", 2)], v => Maths.Combinatorics(Args(v)));

    public static CalcFormPage Factor() => new("Tool_Factor_Name",
        [("Factor_Number", 360)], v => Maths.Factor(Args(v)));

    public static CalcFormPage Percent() => new("Tool_Percent_Name",
        [("Percent_A", 20), ("Percent_B", 150)], v => Maths.Percent(Args(v)));

    public static CalcFormPage Fraction() => new("Tool_Fraction_Name",
        [("Fraction_Value", 0.75)], v => Maths.Fraction(Args(v)));

    public static CalcFormPage Solve() => new("Tool_Solve_Name",
        [("Solve_A", 1), ("Solve_B", -3), ("Solve_C", 2)], v => Maths.Solve(Args(v)));
}
