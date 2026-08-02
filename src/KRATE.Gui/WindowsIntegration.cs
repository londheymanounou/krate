using Microsoft.Win32;

namespace Krate.Gui;

/// <summary>Adds a single "Krate" cascading submenu to Windows Explorer's right-click menu (like 7-Zip):
/// hover it and pick Compress, Encrypt, Extract, Convert, Edit, or a text tool. Everything lives under
/// HKCU (no admin) and each item launches this same exe with a verb.</summary>
// ponytail: one static ExtendedSubCommands flyout on * and Directory. Actions self-validate (Extract on a
// non-archive just reports "not an archive"), so we skip per-extension filtering — that would need a COM
// IContextMenu shell handler, which an unpackaged app can't add cleanly.
public static class WindowsIntegration
{
    // Text tools shown in the submenu; they run on the file's content.
    static readonly string[] TextTools = ["Count", "Clean", "Base64", "Base64Decode", "JsonFormat", "CsvToJson", "Morse", "Slug"];

    static string Exe => Environment.ProcessPath ?? "";
    static string VerbKey(string scope, string verb) => $@"Software\Classes\{scope}\shell\KRATE.{verb}";

    public static bool IsInstalled()
    {
        using var key = Registry.CurrentUser.OpenSubKey(VerbKey("*", "Menu"));
        return key is not null;
    }

    public static void Install()
    {
        var exe = Exe;
        BuildMenu("*", exe, forFile: true);          // any file → full submenu
        BuildMenu("Directory", exe, forFile: false); // folders → just compress
    }

    public static void Uninstall()
    {
        Registry.CurrentUser.DeleteSubKeyTree(VerbKey("*", "Menu"), throwOnMissingSubKey: false);
        Registry.CurrentUser.DeleteSubKeyTree(VerbKey("Directory", "Menu"), throwOnMissingSubKey: false);
    }

    static void BuildMenu(string scope, string exe, bool forFile)
    {
        var parent = VerbKey(scope, "Menu");
        using (var key = Registry.CurrentUser.CreateSubKey(parent))
        {
            key.SetValue("MUIVerb", S("Ctx_Krate")); // the "Krate" flyout label
            key.SetValue("Icon", exe);
            key.SetValue("SubCommands", "");          // signals a static cascade (ExtendedSubCommands)
        }

        var shell = $@"{parent}\shell";
        var n = 0;
        void Item(string name, string label, string command)
        {
            using var child = Registry.CurrentUser.CreateSubKey($@"{shell}\{n++:00}_{name}");
            child.SetValue("MUIVerb", label);
            child.SetValue("Icon", exe);
            using var cmd = child.CreateSubKey("command");
            cmd.SetValue(null, command);
        }

        Item("Compress", S("Archive_Compress"), Cmd(exe, "--compress"));
        if (!forFile) return; // a folder can only be compressed

        Item("Encrypt", S("Encrypt_Do"), Cmd(exe, "--encrypt"));
        Item("Extract", S("Archive_ExtractAll"), Cmd(exe, "--extract"));
        Item("Convert", S("Converter_Convert"), Cmd(exe, "--convert"));
        Item("Edit", S("Notepad_Title"), Cmd(exe, "--edit"));
        foreach (var id in TextTools)
            Item(id, Krate.Core.Catalog.Find(id)?.Name ?? id, $"\"{exe}\" --tool {id} \"%1\"");
    }

    static string Cmd(string exe, string verb) => $"\"{exe}\" {verb} \"%1\"";

    static string S(string key) => Krate.Core.Strings.Get(key);
}
