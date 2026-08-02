# KRATE for Android — handoff plan

Written 2026-07-31 for whoever picks up the Android app. The Rust core is done and already
cross-platform; what remains is Android plumbing and UI.

**Read this whole file before starting.** Several decisions here look arbitrary and are not — the
"Do not undo" section exists because the obvious-looking fix in each case is wrong.

---

## What already exists

`rust/` is a complete implementation of all **140 KRATE tools**, in pure Rust, behind a C ABI.
It is not a prototype: a differential test harness (`tests/KRATE.Tests/RustParityTests.cs`) holds
every tool byte-identical to the original C# across 17 languages, and it found 18 real defects in
the C# while doing so.

- **380 Rust unit tests**, clippy clean.
- **Localisation is inside the core.** All 17 languages are compiled in from the `.resx` files by
  `rust/build.rs`. Call `krate_set_language("fr")` and every tool's output, error messages included,
  switches. **The Android UI does not need to translate tool content** — only its own chrome.
- **`cargo check --target aarch64-linux-android` passes today.** That is verified, not assumed.

### The FFI surface (`rust/src/ffi.rs`)

```c
KrateResult krate_run(const char* id, const char* input);  // {int ok; char* text;}
void  krate_set_language(const char* tag);       // "en", "fr", "zh-CN", ...
void  krate_set_runtime(const char* text);       // SysInfo's RUNTIME line, e.g. "Android 15"
int   krate_tool_count(void);
char* krate_tool_id(int index);
char* krate_tool_name(int index);                // localised
int   krate_currency_store_rates(const char* base, const char* json);
void  krate_free(char* text);                    // EVERY returned string comes back here
```

Rust owns every returned string. Freeing one with anything but `krate_free` corrupts the heap.
`ok == 0` means `text` is an error message, not output — show it, don't parse it.

Every tool is `string in / string out`. That is the whole API. A tool's input format is documented
in its `Tool_<Id>_Desc` resource string, reachable from the core.

---

## Phase 1 — build the core for Android

1. Install the NDK, then `rustup target add aarch64-linux-android armv7-linux-androideabi
   x86_64-linux-android` (aarch64 is already installed).
2. Use `cargo-ndk` (`cargo install cargo-ndk`), which handles the linker/sysroot wiring:
   `cargo ndk -t arm64-v8a -t armeabi-v7a -o app/src/main/jniLibs build --release`
3. Expect this to work with **no source changes**. If something fails to link, it is a dependency
   pulling a Windows-only crate — check `Cargo.toml`'s `[target.'cfg(not(windows))'.dependencies]`
   and the `#[cfg(windows)]` blocks in `clock.rs`, `system.rs`, `currency.rs`.

**On Android the C toolchain exists**, which it does not on the Windows dev machine. Several
workarounds in this repo were forced by that absence and are simply unnecessary on Android — see
"Do not undo".

### Binding choice

The core exposes a **C ABI**, not JNI. Two options, in order of preference:

- **JNA or `dev.rikka.ndk` style direct binding** — no glue code, but slower per call.
- **A thin JNI shim in Rust** (`#[no_mangle] extern "system" fn Java_com_krate_Core_run(...)`)
  — faster, ~40 lines. Prefer this if tool calls end up on a hot path. They will not: every tool
  returns in well under a millisecond except `Encrypt` (600k PBKDF2 iterations, deliberately) and
  the archive tools.

**Call tools off the main thread.** The desktop GUI originally ran them synchronously on the UI
thread and froze on `Encrypt`; that bug is fixed there and should not be recreated here.

---

## Phase 2 — the app

- **Kotlin + Jetpack Compose, Material 3 Expressive.** See the design-language section below — this
  is a requirement, not a preference. The original intent for KRATE was a native UI per platform
  (WinUI on Windows, Material You on Android) rather than one cross-platform toolkit.
- **Catalogue-driven UI.** Enumerate tools with `krate_tool_count`/`krate_tool_id`/`krate_tool_name`
  and build the list from that — do not hardcode 140 screens. The desktop app renders almost every
  tool with **one** shared view: a name, a description, an input box, and an output box. That view
  plus a category list is the entire app for ~130 of the 140 tools.
- **The exceptions** that want a purpose-built screen, because they take options or report progress:
  `Zip`/`Unzip`, `Encrypt`/`Decrypt`, `Currency`, `Timezone`, `Cron`, `Rename`, `PdfSplit`/`PdfMerge`,
  `StripMetadata`, and the four games (`Snake`, `Game2048`, `Tetris`, `Weather`) which are
  **placeholders in the catalogue** — they throw/refuse by design and are implemented by the shell.
- **Set `krate_set_runtime("Android <version>")` at startup**, or `SysInfo`'s RUNTIME line reads
  "Rust". Mirror `App.OnLaunched` in the desktop app.

### Design language — Material 3 Expressive (required)

The app must be **Material 3 Expressive (MD3E)**, Google's 2025 evolution of Material 3, with full
Material You dynamic colour. Concretely that means:

- **Dynamic colour from the wallpaper** on Android 12+, with a sensible fallback palette below it.
  KRATE's own brand colour (the dark tile + white mark in `assets/krate-logo.svg`) is the seed for
  that fallback, not an override of the user's scheme.
- **The expressive shape and motion system** — larger corner radii, shape morphing on interaction,
  and spring-based motion rather than fixed-duration easing.
- **Expressive typography**: bigger, heavier headings with much more size contrast between levels
  than baseline M3. On a tool list this is what makes categories scannable.
- **The newer M3E components** where they fit: button groups, split buttons, FAB menus, loading
  indicators, and the updated navigation components.

**Check the current API status before building.** MD3E's Compose APIs (the expressive theme entry
point, motion schemes, and several of the components above) arrived across the `material3` 1.4/1.5
line and parts of it were experimental for a while. Read the current `androidx.compose.material3`
release notes and use what is stable **now** rather than trusting a version number quoted in this
document — it was written in July 2026 and this area moves.

### Reference apps

From the user's list — <https://github.com/nyas1/Material-You-app-list>, which tags entries `MD3E`
(expressive), `MD` and `MDY` (Material You / dynamic colour).

| App | Why it is worth reading |
|---|---|
| **[Image Toolbox](https://github.com/T8RIN/ImageToolbox)** | **The closest structural match to KRATE, by a distance.** A toolbox of dozens of tools with **search, favourites and recently-used** — the same home page KRATE already has — plus feature-based modularisation in Compose + Material 3. Study its *information architecture*, not just its theming: it has already solved "how do you navigate 100+ tools without a wall of buttons". Verified Compose + Material 3; not verified as Expressive. |
| **[SD Maid SE](https://github.com/d4rken-org/sdmaid-se)** | A large utility app with many independent features and a clean modern Compose codebase. Good reference for structure at scale. |
| **[App Manager](https://github.com/MuntashirAkon/AppManager)** | Very dense feature set presented without clutter. Useful for how much detail one screen can carry. |
| **[ColorBlendr](https://github.com/Mahmud0808/ColorBlendr)** / **[Iconify](https://github.com/Mahmud0808/Iconify)** | Both are about Material You theming itself — the place to look for dynamic-colour handling done thoroughly. |
| **[Record Master](https://github.com/PranshulGG/RecordMaster)**, **[Aperture](https://github.com/XDanfr/Aperture)** | Tagged for expressive/Material styling on the list. Record Master's development has stopped, so treat it as a visual reference only, not an architectural one. |

**Where KRATE differs from all of them:** its business logic is already written and lives in Rust.
None of these apps have that seam, so copy their UI patterns and navigation, not their data layer.

### Platform differences to handle in the app

| Concern | Windows core does | Android app must |
|---|---|---|
| **Currency** | fetches over WinHTTP | fetch the JSON itself and call `krate_currency_store_rates`; the core reads the shared cache and otherwise reports "offline" |
| **File tools** | plain paths | SAF/scoped storage. Tools take a path string, so resolve URIs to real paths (or copy to app storage) before calling |
| **`SysInfo` disks** | `DISK C:\ ...` | reports `/`, `/data`, `/storage/emulated/0` via `statvfs` |
| **`DnsLookup`** | reverse-resolves the host name | no reverse lookup off Windows; the queried name is echoed instead |
| **ffmpeg / yt-dlp / micro** | bundled `.exe`s beside the app | not bundled. `Media`, `YouTube` and `Notepad` will report the tool as missing. Either ship Android builds or hide those tools |

---

## Do not undo

Each of these looks like dead weight and is load-bearing.

1. **`csprng.rs` is hand-rolled and must stay.** It exists because `getrandom` could not link on the
   Windows dev machine (`dlltool` needs GNU `as`, which is absent). It is already cross-platform:
   `RtlGenRandom` on Windows, `/dev/urandom` elsewhere. It generates **passwords** — do not replace
   it with a seeded PRNG for convenience.
2. **`rust/.cargo/config.toml` scopes its rustflag to `cfg(windows)`.** As a bare `[build]` flag it
   broke every non-Windows target outright (`getrandom` rejects `windows_legacy` off Windows). Do
   not widen it.
3. **`rust-toolchain.toml` pins the GNU toolchain** because the dev machine has no MSVC linker. On a
   machine that has one, MSVC is fine — but changing the pin will break that machine's builds.
4. **The parity harness compares the C# methods directly**, not through `Tool.Run`. `Tool.Run` now
   routes to Rust, so pointing the harness at it would compare Rust against itself and pass
   vacuously. If you refactor `RustParityTests`, keep `CSharp` as the reference.
5. **Six documented divergences from the C#** are deliberate and recorded in their modules: QR mask
   pattern, XML error positions, invalid-regex wording, archive/PDF produced bytes, StripMetadata
   (lossless on purpose), and SysInfo's RUNTIME line. Do not "fix" them.

---

## Suggested order

1. `cargo ndk` build → `.so` in `jniLibs` → a throwaway activity that calls `krate_run("Upper", "hi")`
   and shows `HI`. That single call proves the whole seam.
2. Catalogue list + the shared tool view. That is ~130 tools working.
3. Language switching via `krate_set_language`, wired to the system locale.
4. The purpose-built screens, in rough order of value: `Currency`, `Timezone`, `Encrypt`/`Decrypt`,
   `Zip`/`Unzip`, `Cron`.
5. File-tool storage handling, which is the fiddliest Android-specific part.

## Where the details live

- `rust/src/*.rs` — every module's header comment explains what it mirrors and any divergence.
- `tests/KRATE.Tests/RustParityTests.cs` — the contract each tool is held to.
- `README.md`, `krate.md` — the product-level description.
