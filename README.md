# Krate Toolkit

**Krate Toolkit** (just "Krate" for short) — an offline utility toolbox. Logic lives in `KRATE.Core`;
the GUI and CLI are facades.

    src/KRATE.Core   business logic + localized strings (.resx, 17 languages)
    src/KRATE.Cli    console facade  -> krate.exe
    src/KRATE.Gui    WinUI 3 facade  -> KRATE.exe
    tests/KRATE.Tests

## Just run it (no coding needed)

The desktop app, once built, is a normal Windows program — double-click:

    src/KRATE.Gui/bin/Debug/net8.0-windows10.0.19041.0/win-x64/KRATE.exe

It opens on a **Home** page listing the tools you used most recently; pick any tool
from the left, or type in the search box at the top. Everything runs offline.

The command-line version (`krate`):

    krate                      # list all tools
    krate --help               # usage + examples
    krate --version
    krate sha256 hello         # run a tool
    echo hello | krate sha256  # or pipe via stdin
    krate search color         # find tools by keyword
    krate calc --help          # help for one tool
    krate --lang fr            # switch interface language (persisted)

Tab-completion (adds all tool names + commands):

    krate completion powershell >> $PROFILE      # PowerShell
    krate completion bash       >> ~/.bashrc     # bash
    krate completion zsh        >> ~/.zshrc      # zsh

Tools print to stdout; a tool's own error goes to stderr with exit code 1.

## Build & run

    dotnet test
    dotnet run --project src/KRATE.Cli -- sha256 hello
    dotnet run --project src/KRATE.Cli -- --lang en
    dotnet build src/KRATE.Gui -r win-x64 && src/KRATE.Gui/bin/Debug/net8.0-windows10.0.19041.0/win-x64/KRATE.exe

    dotnet publish src/KRATE.Cli -r win-x64 -c Release   # Native AOT

## Adding a tool

1. Pure function in `src/KRATE.Core/Tools/`.
2. One line in `Catalog.Tools` (`Tool.cs`).
3. `Tool_<Id>_Name` / `_Desc` / `_Aliases` in **every** `Resources/Strings*.resx` (17 languages).
   `EveryTool_IsFullyLocalised` fails for any tool missing them in any language — a missing key
   is not an exception, it renders as the literal `Tool_Foo_Name` in the UI.
4. A test.

Edit the `.resx` files with a UTF-8-aware editor. A PowerShell fixup script once rewrote them
through the ANSI codepage and double-encoded 5331 strings across all 17 languages; nothing
failed, the apps just displayed `Ð¤Ð°Ð¹Ð»`. `Resources_AreNotDoubleEncoded` now catches that.

Text-in/text-out tools then appear in the CLI and GUI automatically — no UI code.
File tools take a path as their text, so dropping a file on the GUI window feeds them.

## Interactive GUI tools

A few tools need real controls, not the shared text box (image convert/resize/compress,
timer/pomodoro). They live as `UserControl` pages in `KRATE.Gui` and are registered in the
`_interactive` list in `MainWindow.xaml.cs` — never in `Core`, so `Core` keeps no UI dependency.
The right pane swaps between the shared `ToolView` and these pages on selection.

## Dependencies

All of them live in `KRATE.Core`; the CLI and GUI add none of their own.

- **QRCoder** — QR encoding is not worth hand-rolling (a subtly invalid code is worse than none).
- **PDFsharp** — PDF split/merge.
- **SixLabors.ImageSharp** — image convert/resize/compress and metadata stripping. Pinned to the
  2.1.x line, which is Apache-2.0; 3.x onward is under the Six Labors Split License. Keep it at
  2.1.11 or later — 2.1.3 carried four high-severity advisories.
- **SharpCompress** — tar/gz/bz2, and extraction of everything 7-Zip is not needed for.
- **Squid-Box.SevenZipSharp** + **SevenZipSharp.Interop** — creating `.7z` only. See below.

## Notes for building

- Native AOT publish needs the MSVC C++ linker (VS "Desktop development with C++"). Without it,
  `dotnet publish -r win-x64` fails at the link step; trim-only publish works and is what CI here uses.
- SevenZipSharp is COM-based, so `KRATE.Cli` must set `<BuiltInComInteropSupport>true</...>`:
  `PublishAot` disables built-in COM interop in *every* build, not just published ones, and 7z
  compression then fails with a message that blames the 7-Zip library. Under real Native AOT,
  COM interop is unavailable outright, so 7z creation would need an out-of-process `7z.exe`.
- `7z.dll` reaches the CLI/GUI output only because `KRATE.Core.csproj` declares it as `Content`.
  The Interop package ships it through an MSBuild `.targets`, and targets do not flow across a
  `ProjectReference` — so `KRATE.Core/bin` had it and the apps that shipped did not.
- GUI pages are verified to build and launch (`crash.log` is written next to the exe on any
  unhandled UI exception); interaction paths are not automatically tested.
