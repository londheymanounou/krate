# KRATE — Development Blueprint

> Specification document intended to be read by Claude Code to generate the application.
> Read this file in full before starting. The core principles take precedence over everything else.

---

## 1. Vision

**KRATE** is a Swiss-army-knife utility app for desktop: a single application bundling dozens of small tools (text, conversion, hashing, colors, files, math, dates, randomness…) that are usually scattered across various websites.

The name evokes a toolbox / krate, with a nod to the "krate" term from the Rust ecosystem.

**Target audience:** developers and power users, but every tool must stay usable by anyone.

**Differentiator:** everything runs **locally, offline**. Unlike equivalent websites, no data is sent to a server. This is a privacy and speed advantage and should be highlighted.

---

## 2. Core Principles (NON-NEGOTIABLE)

1. **100% offline.** No tool may require internet to function (see §9 for the rare optional exceptions).
2. **No database.** No DBMS. Required static data (lists, conversion tables) are embedded resources loaded on demand.
3. **Lightweight and optimized — zero bloat.** Every tool and every dependency must justify its presence. When in doubt, 20 lines of homemade code beat a multi-MB dependency.
4. **Instant startup.** The app is opened 20 times a day for a quick calculation: slow startup is a dealbreaker. This is performance metric #1.
5. **Immediate responsiveness.** Every tool responds to a click with no perceptible latency.
6. **Deterministic, exact results.** Unless a clearly identified exception, every tool always returns the correct result.

---

## 3. Architecture

Strict **logic / interface** separation. This is the most important architectural point: logic is written once and reused by every interface.

```
KRATE.Core      ← .NET class library. ALL business logic. Zero UI dependencies.
KRATE.Gui       ← WinUI 3 application (Windows). References Core.
KRATE.Cli       ← Console application. References Core.
KRATE.Tests     ← Unit tests for Core.
```

**Rules:**
- `Core` references NO UI library. Pure computation only.
- Each tool is a class/function in `Core`, independently testable.
- The GUI and CLI are only "facades" that call `Core`.
- **Forbidden** to write business logic directly in WinUI pages. If a conversion bug is fixed in `Core`, it must be fixed everywhere at once.
- Platform-specific tools (e.g. Windows system access) go in a separate module, not in the shared `Core`, so a future port isn't blocked.

**Future portability (out of v1 scope):** since `Core` is pure C#/.NET, it can later be reused for a cross-platform version (.NET MAUI, or a native Android app with `Core` exposed differently). Do not implement this now, but **do nothing that prevents it**.

---

## 4. Tech Stack

- **Language:** C# / .NET (latest LTS version).
- **Windows UI:** WinUI 3 (Windows App SDK).
- **CLI:** .NET console application, simple argument parsing.
- **Compilation:** target **Native AOT** + **trimming** to reduce executable size, speed up startup, and lower memory usage.
  - ⚠️ AOT forbids certain reflection-based libraries. Choose dependencies accordingly **from the start**. If a tool absolutely requires reflection, isolate it.
- **Dependencies:** minimal. Every third-party package must be justified. Prefer native .NET / Windows APIs.

---

## 5. Navigation Structure (UX)

With 100+ tools, **organization IS the product**. A tool that can't be found in 2 seconds doesn't exist for the user.

- **Side `NavigationView`** listing the categories (see §6).
- **Global search bar** at the top, filtering tools by name **and by keywords/aliases** (e.g. "color", "hex", "random"). This is the primary navigation method expected by power users.
- **Lazy loading is mandatory:** no tool page is instantiated at startup. Each tool loads only when the user opens it. `NavigationView` does this naturally as long as it isn't forced otherwise. This is what keeps startup fast even with 100 tools.
- Embedded resources (lists, tables, icons) load **on demand**, never all at startup.
- Ideally: a home page with recent / favorite tools.

**File ergonomics:** tools that process files must support **drag-and-drop**. This is precisely what a desktop app does better than a website — a strong axis for KRATE.

---

## 6. Full Tool Catalog

> Grouped by category = navigation structure. A tool may surface via multiple keywords in search.

### 6.1 Text
- Counter (words, characters, lines, estimated reading time)
- Case converter (UPPERCASE ↔ lowercase, Title Case, iNVERSE)
- Naming convention converter (camelCase, snake_case, kebab-case, PascalCase)
- Text cleanup (multiple spaces, line breaks, formatting pasted from clipboard)
- Accent removal (é → e, ç → c)
- Line sorting (alphabetical, by length, random) + duplicate removal
- Reverse / mirror / "zalgo" effect
- Diff between two texts
- Word frequency counter
- French typography corrector (non-breaking spaces before `; : ! ?`)
- Masking / anonymization (replace emails, numbers with placeholders)
- Slug generator (for URLs)
- Text language detector
- Lorem Ipsum generator
- Table of contents generator (from headings)
- Markdown table generator
- ASCII art from text
- Text ↔ Morse
- Fancy text (stylized Unicode fonts for social media)

### 6.2 Encoding & data conversion
- Base64 (encode/decode, including image → Base64)
- URL encode/decode
- HTML entities encode/decode
- Escape/unescape (JSON, SQL, shell)
- Number base converter (binary, octal, decimal, hexadecimal)
- Data format converter (JSON ↔ CSV ↔ YAML ↔ XML)
- Markdown ↔ HTML
- Unix timestamp ↔ readable date
- JWT (encode/decode — local decoding, no server validation)
- Cron → human-readable text
- Scientific notation conversion

### 6.3 Hashing & security
- Text hashing (MD5, SHA-1, SHA-256, SHA-512)
- File hashing (drag-and-drop)
- File comparator (identical or not, via checksum)
- Password generator (configurable length, symbols, digits)
- Password strength checker
- UUID / GUID generator
- Checksum generation/verification (to validate a download)

### 6.4 Developer
- JSON, XML, SQL formatter / validator
- Beautifier / minifier (JS, CSS, HTML)
- Regex tester
- Regex generator from examples
- `.gitignore` generator by language
- Hex editor / viewer
- File encoding converter (UTF-8, ANSI, ISO-8859-1…)
- Line ending converter (CRLF ↔ LF)
- Path converter (Windows ↔ Unix slashes)
- Filename cleaner (removes Windows-forbidden characters)
- QR code generator
- Barcode generator

### 6.5 Colors & design
- HEX ↔ RGB ↔ HSL converter (+ screen color picker)
- Harmonious palette generator (complementary, triadic…)
- Dominant palette extractor from an image
- Gradient generator (with CSS code)
- Contrast checker (WCAG accessibility) between two colors
- Color blindness simulator on a color
- Color temperature converter (Kelvin)
- px ↔ rem ↔ em ↔ pt converter
- CSS shadow / border-radius generator
- Favicon generator

### 6.6 Images & files
- Image compression / resizing
- Image format converter (PNG, JPG, WEBP, ICO)
- Metadata extraction (EXIF, file properties)
- Watermark addition
- Image dimensions / resolution / ratio calculator (16:9…)
- PDF merge / split
- Bulk file renaming
- Duplicate finder
- Folder size calculation
- Folder tree generator as text (`tree`)
- File splitter / joiner (split a large file into parts)
- Test file generator (create a file of X MB)

### 6.7 Math & calculation
- Scientific calculator
- Equation solver
- Percentage / rule of three calculation
- Fraction ↔ decimal converter
- GCD / LCM, factorization, prime detection
- Statistics (mean, median, standard deviation on a list)
- Powers and roots
- Sequence generator (Fibonacci, arithmetic, geometric)
- Simple probabilities

### 6.8 Conversions (units)
- General unit converter (length, weight, temperature, speed…)
- Imperial ↔ metric (complete)
- Angles (degrees ↔ radians ↔ gradians)
- Data units (KB, MB, GB, GiB…)
- Roman ↔ Arabic numerals
- Numbers to words (multilingual)
- Clothing sizes / shoe sizes (FR / US / UK — local tables)
- Speed / distance / time

### 6.9 Dates & time
- Date calculator (difference between two dates, add days)
- Precise age calculator (years, months, days)
- Business days between two dates
- Duration converter (seconds ↔ h/min/d)
- Week number / calendar generator
- Time zone converter (multiple cities in parallel)
- Stopwatch / timer / countdown
- Pomodoro timer
- Metronome / tone generator (frequencies in Hz)

### 6.10 Randomness
- Random number generator
- Dice (customizable number of faces)
- Coin flip
- Card draw
- Random pick from a list (drawing lots)
- Customizable wheel of fortune
- Random team / schedule splitter
- Random color generator

### 6.11 Everyday & miscellaneous
- BMI calculator
- Tip calculator
- Loan / monthly payment calculator
- Counter / clicker
- Quick notepad with auto-save
- Clipboard history
- System info (RAM, CPU, disk)
- IP subnet calculator (CIDR)

---

## 7. Implementation Notes by Family

- **Randomness:** use an appropriate generator. For passwords and anything security-related, use a **cryptographically secure** generator (`RandomNumberGenerator`), not the standard `Random`.
- **Random name/color generators:** small hardcoded or algorithmic lists. No database.
- **Conversions (units, sizes, roman):** static tables as embedded resources, loaded on demand.
- **Files/images:** prefer native .NET / Windows Imaging APIs. Avoid heavy image-processing libraries if native suffices.
- **QR codes / barcodes:** local generation. If a library is needed, pick a lightweight, AOT-compatible one.
- **JWT:** local decoding/inspection **only**, do not claim to validate a signature against a server.

---

## 8. Optimization (recap of levers, by impact order)

1. **Lazy loading** of tools (most important given their number).
2. On-demand loading of embedded resources.
3. **Native AOT + trimming** at build.
4. Minimal third-party dependencies.
5. **Don't over-optimize.** Measure before optimizing. Don't waste time shaving milliseconds off already-instant operations. Premature optimization is a trap.

Concrete goal: an app that starts instantly, weighs a few MB, and uses little RAM at rest.

---

## 9. Network Exceptions (optional, outside v1 core)

Only two tools need internet. They are **optional** and must not break the offline functioning of the rest:

- **Currency converter:** hybrid mode recommended — fetch rates when the network is available, cache them, and offline use the last known rates **while displaying their date**. May be deferred past v1.
- **Ping / latency test:** requires the network by nature. Optional.

If the choice arises between "fully offline" and "including these tools", offline wins: these two can be removed or deferred.

---

## 10. Internationalization (i18n) — Multi-language Support

The app must be **available in multiple languages** (UI fully localizable).

**Principles:**
- **No hardcoded user-facing strings.** All UI text (labels, tool names, category names, buttons, tooltips, error messages, search keywords/aliases) lives in resource files, never inline in code or XAML.
- Use the standard .NET localization mechanism: **`.resx` resource files** (e.g. `Resources.resx`, `Resources.fr.resx`, `Resources.es.resx`…), with `Resources.resx` as the neutral/fallback culture.
- The GUI and the CLI must **share the same resource files** (place them in `Core` or a dedicated `KRATE.Resources` project referenced by both), so a string is translated once and reused everywhere.
- **Language selection:** auto-detect the OS language at first launch, with a manual override in settings. The choice is persisted locally (small local settings file, not a database).
- **Fallback:** any missing translation falls back to the neutral culture (English). A missing string must never crash or show an empty label.

**Launch languages (v1):**
- **English** (neutral/default culture)
- **French**

**Structured for easy extension:** adding a language must mean only adding a new `.resx` set — no code changes. Keep the architecture ready for Spanish, German, etc. later.

**Important distinctions:**
- **UI language ≠ tool data.** The "numbers to words" tool and the "French typography corrector" have their OWN language logic (independent of the UI language): they operate on the language chosen *within the tool*, and must keep working regardless of the app's display language.
- Locale-sensitive formatting (dates, decimal separators, thousands separators) must respect the selected culture where relevant, but tools that need a stable machine format (e.g. code output, timestamps) must NOT be affected by display locale — keep those culture-invariant.

---

## 11. Suggested Build Order

**Phase 0 — Foundations**
- Set up the solution: `Core`, `Gui`, `Cli`, `Tests` (+ shared resources for i18n).
- WinUI 3 GUI shell with `NavigationView` + search bar + working lazy loading.
- CLI skeleton (argument parsing → `Core` call → output).
- Wire up i18n from the start (resource files, language detection + override), even with just English + French.
- Validate the pipeline on **a single simple tool end-to-end** (e.g. SHA-256 hash): logic in `Core`, exposed in GUI AND CLI, with a unit test, and fully localized strings.

**Phase 1 — Core (simple, high-usage, purely algorithmic tools)**
Prioritize the easy-to-code, frequently-used ones: text case, counter, Base64/URL/HTML, number bases, text + file hashing, UUID, password, JSON format/validate, HEX↔RGB↔HSL, Unix timestamp, unit converter, roman numerals, randomness (numbers/dice/coin/draw), date calculator.

**Phase 2 — Remaining algorithmic tools**
All other text, dev, math, conversion, color, date, and misc entries.

**Phase 3 — File/image/visual tools**
Image processing, PDF, bulk renaming, folder tree, QR/barcodes, color picker, palettes, metronome, wheel of fortune, etc. (heavier or requiring visuals).

**Phase 4 — Network optionals**
Currency converter (hybrid), ping.

**Cross-cutting rule:** every tool ships with its logic in `Core` + GUI exposure + CLI exposure (where relevant) + a unit test + fully localized strings. Never duplicate logic.

---

## 12. Final Reminders for the Implementer

- The project's treasure is `Core`. Keep it pure, tested, UI-dependency-free.
- Every dependency and every tool must justify its presence. When in doubt, code it yourself.
- Lightness comes mostly from what you **don't** add.
- One tool that works perfectly beats ten half-baked ones.
- No hardcoded user-facing strings — everything localizable from day one.
- Verify the exact name in the stores before publishing (plan B: KrateKit, KrateBox, Krate).