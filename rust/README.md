# Krate Toolkit — Rust core

Named `krate` because `krate` is a reserved keyword in Rust and cannot be a package name.

This is the port target for `src/KRATE.Core`. It runs alongside the C# core rather than
replacing it in one step: each tool moves over, gets a row in `RustParityTests.Ported`, and is
only considered ported once both implementations return identical output for every input in
all 17 languages.

    cargo test              # unit tests + source hygiene
    cargo clippy --all-targets
    cargo build --release   # produces target/release/krate_core.dll for the parity tests

## Layout

    build.rs      reads ../src/KRATE.Core/Resources/*.resx and generates the string catalogue
    src/i18n.rs   language selection and lookup; a missing key echoes itself, as in C#
    src/tools.rs  the catalogue and the ported tools
    src/ffi.rs    C ABI for the WinUI shell (P/Invoke) and, later, Android (JNI)

The `.resx` files stay the single source of truth for translations while the port is in
flight, so the two implementations cannot drift apart. Nothing is duplicated by hand.

## Toolchain

`rust-toolchain.toml` pins the **GNU** toolchain because the MSVC linker is not installed on
this machine — the same gap that blocks .NET Native AOT. Installing the Visual Studio C++
workload fixes both; after that, switch the channel to `stable-x86_64-pc-windows-msvc` and
nothing else needs to change.

## Porting a tool

1. Write it in `src/tools.rs` with unit tests mirroring the C# assertions.
2. Add it to `CATALOG`.
3. Add a row to `Ported` in `tests/KRATE.Tests/RustParityTests.cs` with inputs covering the
   edge cases — including whatever previously broke.
4. `cargo build --release`, then `dotnet test`. Both must be green.

Do not edit any source or `.resx` file with Windows PowerShell's `Get-Content`/`Set-Content`
without an explicit `-Encoding`: it decodes UTF-8 as the ANSI codepage and double-encodes
every non-ASCII character. `sources_are_utf8_without_bom_or_double_encoding` guards this krate;
`Resources_AreNotDoubleEncoded` guards the C# resources.
