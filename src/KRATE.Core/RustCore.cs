using System.Globalization;
using System.Runtime.InteropServices;

namespace Krate.Core;

/// <summary>The seam to the Rust core. Every catalogue tool exists in both implementations and is
/// held byte-identical by <c>RustParityTests</c>; this is what makes the Rust one the code that
/// actually runs.
///
/// <para>It degrades rather than fails: if <c>krate_core.dll</c> is missing or a call throws,
/// <see cref="Available"/> goes false for the rest of the process and every tool falls back to the
/// C# implementation. A missing native library should not take the app down when there is a working
/// implementation right there.</para>
///
/// <para>Rust owns every string it returns, so each one goes back to <c>krate_free</c> — freeing it
/// with .NET's allocator is the classic way to corrupt the heap across an FFI boundary.</para></summary>
public static partial class RustCore
{
    const string Lib = "krate_core";

    [StructLayout(LayoutKind.Sequential)]
    struct Result
    {
        public int Ok;
        public IntPtr Text;
    }

    [LibraryImport(Lib, EntryPoint = "krate_run", StringMarshalling = StringMarshalling.Utf8)]
    private static partial Result NativeRun(string id, string input);

    [LibraryImport(Lib, EntryPoint = "krate_set_language", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void NativeSetLanguage(string language);

    [LibraryImport(Lib, EntryPoint = "krate_set_runtime", StringMarshalling = StringMarshalling.Utf8)]
    private static partial void NativeSetRuntime(string runtime);

    [LibraryImport(Lib, EntryPoint = "krate_free")]
    private static partial void NativeFree(IntPtr text);

    [LibraryImport(Lib, EntryPoint = "krate_tool_count")]
    private static partial int NativeToolCount();

    static bool _disabled;
    static bool _probed;

    /// <summary>Whether the native core can be used. Probed once, then sticky.</summary>
    public static bool Available
    {
        get
        {
            if (_disabled) return false;
            if (_probed) return true;
            try
            {
                // A cheap call that touches the library and nothing else.
                if (NativeToolCount() <= 0) { _disabled = true; return false; }
                // The core cannot discover its host runtime, so tell it.
                NativeSetRuntime($".NET {Environment.Version}");
                NativeSetLanguage(Strings.Culture.Name is { Length: > 0 } name ? name : "en");
                _probed = true;
                return true;
            }
            catch (Exception e) when (e is DllNotFoundException or BadImageFormatException or EntryPointNotFoundException)
            {
                _disabled = true;
                return false;
            }
        }
    }

    /// <summary>Turns the native core off for the rest of the process, falling back to C#.
    /// Used by the tests that must exercise the managed implementation.</summary>
    public static void Disable() => _disabled = true;

    /// <summary>Keeps the native core's language in step with <see cref="Strings.Culture"/>.
    /// Called whenever the interface language changes.</summary>
    public static void SetLanguage(CultureInfo culture)
    {
        if (!Available) return;
        try { NativeSetLanguage(culture.Name is { Length: > 0 } name ? name : "en"); }
        catch (Exception) { _disabled = true; }
    }

    /// <summary>Runs a tool natively. Throws <see cref="ArgumentException"/> for a tool error, so
    /// callers handle it exactly as they handled the managed implementation.</summary>
    public static string Run(string id, string input)
    {
        var result = NativeRun(id, input);
        try
        {
            var text = Marshal.PtrToStringUTF8(result.Text) ?? "";
            // ok == 0 means text is the error message, not output.
            if (result.Ok == 0) throw new ArgumentException(text);
            return text;
        }
        finally { NativeFree(result.Text); }
    }
}
