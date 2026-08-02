using System.Globalization;
using System.Net;
using System.Runtime.InteropServices;

namespace Krate.Core;

public static class Everyday
{
    /// <summary>double.Parse throws a raw, untranslated FormatException that reaches the user
    /// verbatim in every language — every calculator here routes through this.</summary>
    static double[] Numbers(string input)
    {
        var parts = input.Split([' ', ',', ';', '\t', '\n', '/'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var values = new double[parts.Length];
        for (var i = 0; i < parts.Length; i++)
            if (!double.TryParse(parts[i], NumberStyles.Float, CultureInfo.InvariantCulture, out values[i]))
                throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        return values;
    }

    static string Fmt(double v) => string.Create(CultureInfo.InvariantCulture, $"{v:0.##}");

    /// <summary>"70 175" → BMI for 70 kg and 175 cm.</summary>
    public static string Bmi(string input)
    {
        var n = Numbers(input);
        if (n.Length < 2) throw new ArgumentException(Strings.Get("Error_BmiUsage"));
        var (kg, metres) = (n[0], n[1] > 3 ? n[1] / 100 : n[1]); // accept cm or m
        if (kg <= 0 || metres <= 0) throw new ArgumentException(Strings.Get("Error_BmiUsage"));

        var bmi = kg / (metres * metres);
        var band = bmi switch
        {
            < 18.5 => "Bmi_Under",
            < 25 => "Bmi_Normal",
            < 30 => "Bmi_Over",
            _ => "Bmi_Obese",
        };
        return string.Join('\n', $"BMI  {Fmt(bmi)}", Strings.Get(band), Strings.Get("Bmi_Disclaimer"));
    }

    /// <summary>"48.50 15 3" → 15% tip on 48.50, split between 3 people.</summary>
    public static string Tip(string input)
    {
        var n = Numbers(input);
        if (n.Length == 0) throw new ArgumentException(Strings.Get("Error_NeedNumber"));
        var (bill, percent, people) = (n[0], n.Length > 1 ? n[1] : 15, n.Length > 2 ? Math.Max(1, n[2]) : 1);
        var tip = bill * percent / 100;
        var total = bill + tip;
        return string.Join('\n',
            Strings.Get("Tip_Tip", Fmt(percent), Fmt(tip)),
            Strings.Get("Tip_Total", Fmt(total)),
            people > 1 ? Strings.Get("Tip_Each", people, Fmt(total / people)) : "").TrimEnd('\n');
    }

    /// <summary>"200000 3.5 25" → monthly payment on 200 000 at 3.5% over 25 years.</summary>
    public static string Loan(string input)
    {
        var n = Numbers(input);
        if (n.Length < 3) throw new ArgumentException(Strings.Get("Error_LoanUsage"));
        var (principal, annualRate, years) = (n[0], n[1], n[2]);
        var months = (int)Math.Round(years * (years < 100 ? 12 : 1)); // years, or months if it's a big number
        var monthlyRate = annualRate / 100 / 12;

        // Standard amortisation; the zero-interest case would divide by zero.
        var payment = monthlyRate == 0
            ? principal / months
            : principal * monthlyRate / (1 - Math.Pow(1 + monthlyRate, -months));
        var total = payment * months;

        return string.Join('\n',
            Strings.Get("Loan_Monthly", Fmt(payment)),
            Strings.Get("Loan_Total", Fmt(total)),
            Strings.Get("Loan_Interest", Fmt(total - principal)),
            Strings.Get("Loan_Payments", months));
    }

    /// <summary>"192.168.1.10/24" → network, broadcast, mask, usable host range and count.</summary>
    public static string Subnet(string input)
    {
        var parts = input.Trim().Split('/');
        if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var ip) || ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            throw new ArgumentException(Strings.Get("Error_CidrUsage"));
        var prefix = int.Parse(parts[1], CultureInfo.InvariantCulture);
        if (prefix is < 0 or > 32) throw new ArgumentException(Strings.Get("Error_CidrUsage"));

        var address = ToUInt(ip);
        var mask = prefix == 0 ? 0u : uint.MaxValue << (32 - prefix);
        var network = address & mask;
        var broadcast = network | ~mask;
        // /31 and /32 have no network/broadcast pair to exclude. Counted in 64 bits because a
        // /0 overflows a uint shift: "1u << 32" masks to "1u << 0", and 1 - 2 wrapped round to
        // 4294967295 — one host too many for the entire address space.
        var total = prefix >= 31 ? 1UL << (32 - prefix) : (1UL << (32 - prefix)) - 2;

        return string.Join('\n',
            $"NETWORK    {ToIp(network)}/{prefix}",
            $"NETMASK    {ToIp(mask)}",
            $"WILDCARD   {ToIp(~mask)}",
            $"BROADCAST  {ToIp(broadcast)}",
            $"HOSTS      {ToIp(prefix >= 31 ? network : network + 1)} – {ToIp(prefix >= 31 ? broadcast : broadcast - 1)}",
            $"USABLE     {total}",
            Strings.Get(IsPrivate(network) ? "Cidr_Private" : "Cidr_Public"));
    }

    /// <summary>Machine facts: OS, CPU, memory and disks. Input is ignored.</summary>
    public static string SysInfo(string _)
    {
        var gc = GC.GetGCMemoryInfo();
        var lines = new List<string>
        {
            $"OS         {RuntimeInformation.OSDescription}",
            $"ARCH       {RuntimeInformation.OSArchitecture}",
            $"RUNTIME    .NET {Environment.Version}",
            $"MACHINE    {Environment.MachineName}",
            $"CPU CORES  {Environment.ProcessorCount}",
            $"MEMORY     {Bytes(gc.TotalAvailableMemoryBytes)}",
        };
        foreach (var d in DriveInfo.GetDrives().Where(d => d.IsReady))
            lines.Add($"DISK {d.Name,-6} {Bytes(d.AvailableFreeSpace)} free / {Bytes(d.TotalSize)}");
        return string.Join('\n', lines);
    }

    static string Bytes(long b)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = b;
        var i = 0;
        while (v >= 1024 && i < units.Length - 1) { v /= 1024; i++; }
        return string.Create(CultureInfo.InvariantCulture, $"{v:0.#} {units[i]}");
    }

    static uint ToUInt(IPAddress ip) => BitConverter.ToUInt32(ip.GetAddressBytes().Reverse().ToArray());
    static string ToIp(uint v) => new IPAddress(BitConverter.GetBytes(v).Reverse().ToArray()).ToString();

    static bool IsPrivate(uint network) =>
        (network & 0xFF000000) == 0x0A000000 ||          // 10.0.0.0/8
        (network & 0xFFF00000) == 0xAC100000 ||          // 172.16.0.0/12
        (network & 0xFFFF0000) == 0xC0A80000;            // 192.168.0.0/16

    /// <summary>GUI uses WeatherPage; CLI handles it directly. This placeholder keeps it in the searchable catalog.</summary>
    public static string Weather(string _) => throw new NotSupportedException();
    
    /// <summary>GUI uses SnakePage; CLI handles it directly. This placeholder keeps it in the searchable catalog.</summary>
    public static string Snake(string _) => throw new NotSupportedException();
    
    /// <summary>GUI uses Game2048Page; CLI handles it directly. This placeholder keeps it in the searchable catalog.</summary>
    public static string Game2048(string _) => throw new NotSupportedException();
    
    /// <summary>GUI uses TetrisPage; CLI handles it directly. This placeholder keeps it in the searchable catalog.</summary>
    public static string Tetris(string _) => throw new NotSupportedException();
}
