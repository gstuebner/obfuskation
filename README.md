# Obfuskation

**English** · [Deutsch](README.de.md)

Replaces sensitive data in CSV, JSON, and plain text files with plausible pseudonyms and can fully reverse the substitutions. Designed for scenarios where sample data needs to be fed to an AI or LLM, but real data must never leave the organization.

The primary value lies in the return path: the AI's response — generated code, analyses, sample outputs — can be mapped back to real values using `deobfuscate`.

There are two interfaces to the same core: the command-line tool `obfuskation` for scripts and automation, and the graphical interface `obfuskation-gui` for everyday work. Both share the same underlying library and substitution table — whatever one replaces, the other can restore.

## Documentation

- [User documentation](docs/anwenderdokumentation.md) (German) — for everyone
  preparing sample data for an AI with the graphical interface.
- [Developer documentation](docs/entwicklerdokumentation.md) (German) — for
  everyone building, extending or reviewing the tool.
- [Test documentation](docs/testdokumentation.md) (German) — test plan, test
  cases and the findings of the manual test run.

## Download

Pre-built binaries for Windows and Linux are available under [Releases](../../releases). Two portable files per platform, no installation required:

| File | Purpose |
|---|---|
| `obfuskation` / `obfuskation.exe` | Command-line interface |
| `obfuskation-gui` / `obfuskation-gui.exe` | Graphical user interface |

Prerequisite is the **.NET 8 runtime**. If you prefer not to install it, you can build a self-contained version using `./build-release.sh --self-contained` that bundles everything.

On Linux, make the files executable (`chmod +x`). On Windows, SmartScreen warns on first launch because binaries are unsigned — click *More info* → *Run anyway*.

---

## Please Read First

**1. The tool pseudonymizes, it does not anonymize.**  
Names and account numbers disappear, while structure and distribution remain. A combination of postal code, birth year, and account balance may still trace back to an individual. Whether this is acceptable in your specific case is determined by your Data Protection Officer (DPO) — not by this tool.

**2. `mapping.json` is the most sensitive artifact of the entire process.**  
The file contains all real data in a compact, machine-readable form. It must never be committed to a repository, placed in a directory shared externally, or stored in an off-premises backup. The tool deliberately stores it outside the project directory, sets permissions to `0600`, and refuses storage inside a Git working tree.

**3. Text rules are a process of exclusion.**  
Whatever matches no pattern remains unchanged. IBAN, BIC, email, and phone numbers can be detected reliably — person names, company names, and addresses in free text practically cannot. When in doubt, `redact` or `drop` is the correct choice for free-text fields, not `scanText`.

**4. Run `scan` before every external transfer.** Not optional.

**5. Name output files recognizably** (`customers.pseudo.csv`) so that originals and pseudonymized files are never confused.

---

## Workflow

```fish
# 1. Derive rule scaffold from a real sample file
obfuskation init --profile bank-statements --from customers.csv

# 2. Review obfuskation.json: every column defaults to "error" and
#    requires a deliberate human decision

# 3. Replace
obfuskation obfuscate customers.csv -o customers.pseudo.csv --strict

# 4. Verify — only share after this check passes
obfuskation scan customers.pseudo.csv

# 5. Translate AI response back
pbpaste | obfuskation deobfuscate
```

`init --from` inspects the column headers and generates a rule with `"action": "error"` for each, accompanied by a suggestion in a comment. Suggestions are intentionally not applied automatically: what happens to a field must be decided by a human. As long as even a single column is set to `error`, execution halts before anything is written.

---

## The Graphical User Interface

```fish
obfuskation-gui                        # looks for obfuskation.json like the CLI
obfuskation-gui customers.csv          # open file immediately
obfuskation-gui --config profile.json customers.csv
```

When started without arguments, it looks for an `obfuskation.json` in the current working directory, falling back to the most recently used profile.

**Layout:** At the top, the opened file with detected format, character encoding, and delimiter, next to it a "Recent ▾" button, shown once the profile already knows other files, to switch between them without the Open dialog. On the left, the fields, each with a status indicator — solid turquoise means *decided*, a red circle means *pending*, and as long as even one is pending, processing will abort. On the right, for a single selected field, up to three sample values from the file first — visible even while the handling is still pending — then the handling of the field, plus a live preview using an actual value (»Max Mustermann → Paul Gerber«) once it's being replaced. At the bottom, the three actions.

Wide tables can be handled in bulk: select several fields at once — Ctrl-click for individual ones, Shift-click for a range, Ctrl+A for all. Action and generator then apply to the whole selection; a generator only to those fields in it that are actually being replaced. Field content and preview stay reserved for a single selected field.

Accessible via **More**:

- **Text Rules** — with an interactive test input. Test patterns against real sample text before running them on actual data; overbroad patterns become apparent immediately.
- **Substitution Table** — path, record counts per namespace, and file permissions. Displays **no values**, identical to `mapping list`.
- **Quick Help** (»Kurzhilfe…«) — six short cards covering what the tool is for, what a profile is and why it matters, and the path through the program, aimed at someone opening the interface for the first time.
- **About** — the five notices above and the paths in use.

**Light and Dark:** The toggle in the top right cycles through three states — system default (follows operating system setting), dark, light. The preference is remembered in `~/.config/obfuskation/gui.json`. That file only stores convenience settings: selected view, recently opened profiles, window dimensions — **no file contents and no processed data**.

For large files, the GUI displays progress and allows cancellation; cancelling writes neither an output file nor entries to the substitution table.

**Managing profiles.** A profile bundles the field rules and the substitution table under one name; **New from File…** now asks for a name, an optional description, and a storage location (default: `~/.config/obfuskation/profile/<name>.json`), and **Profiles…** opens a sortable, searchable overview of every known profile together with the files it was last used with. Renaming a profile from that overview never touches the substitution table's entries — only the name changes, in the profile itself and, if the table already exists, inside it too, so no pseudonym is ever invalidated. Closing the window or switching to another profile while rules are unsaved triggers a confirmation (save, discard, or cancel). **Remove from List** hides an entry permanently (the profile file itself is untouched; **Choose from File…** brings it back), and **Delete Profile…** actually deletes the profile file, optionally the substitution table as well — with a confirmation that spells out that the table holds every real value and that deleting it makes recovery impossible. The profile currently open in the main window can't be deleted this way.

**New from File…** accepts multiple files at once: pick several related files that share a key column (see below), and the resulting profile's rule scaffold covers the fields of all of them, not just one.

**Batch runs.** Next to **Create Pseudo File…**, the **All ▾** menu offers **Create All Pseudo Files…** and **Create All Plaintext Files…** — they process every file the profile knows about (from **Recent ▾**) that still exists, writing each output next to its input with the same `.pseudo`/`.klartext` suffix as a single run. A single confirmation covers the whole batch — naming the file count, the output pattern, and how many existing target files would be overwritten — and **no further prompt follows**, so batch runs overwrite silently once confirmed. Cancelling stops before the next file; files already written stay and are counted in the summary, and a file that's missing or fails (e.g. an undecided field) is skipped and named in the final message.

### Desktop Menu Integration (Linux)

See `packaging/README.md` — `.desktop` entry and icon for GNOME.

---

## Multiple Files with Shared Key Fields

The typical scenario: master data, accounts, and transactions reside in separate files and are linked by a customer ID or person number. This number must be substituted identically across all files — otherwise relationships break and test datasets become useless.

**This happens automatically.** No special configuration is needed, only one condition: **process all files using the same profile.**

```fish
obfuskation obfuscate master-data.csv   -o master-data.pseudo.csv   --strict
obfuskation obfuscate accounts.csv      -o accounts.pseudo.csv      --strict
obfuskation obfuscate transactions.csv  -o transactions.pseudo.csv  --strict
```

In the GUI accordingly: open the profile once, load files sequentially via **Open…**, and click **»Pseudodatei erzeugen…«** (*create pseudo file* — the interface is German) for each. The profile remains loaded.

ID `4711` becomes the exact same value across all three files because pseudonyms are derived deterministically from the plaintext and recorded in the shared substitution table. The second and third runs report `0 new entries` for that field — a reliable confirmation that links are preserved.

**Column names may differ.** If a column is named `Personennummer` in one file and `PersNr` in another, configuring both rules with the **same generator** is sufficient — the generator determines the namespace, not the column name.

```jsonc
{ "match": "Personennummer", "action": "pseudonymize", "generator": "numericId" },
{ "match": "PersNr",         "action": "pseudonymize", "generator": "numericId" }
```

### When two fields should *not* be linked

The reverse also applies, and this is a common pitfall: customer ID `4711` and invoice number `4711` would receive the exact same pseudonym if using the same generator. The test data would then suggest a link that never existed.

To prevent this, define a custom namespace under `generators` based on a built-in generator:

```jsonc
"generators": {
  "invoiceNumber": { "type": "numericId" }
}
```

```jsonc
{ "match": "CustomerID",    "action": "pseudonymize", "generator": "numericId" },
{ "match": "InvoiceNumber", "action": "pseudonymize", "generator": "invoiceNumber" }
```

Both produce numeric IDs of the same format, but draw from separate pools. In the GUI, `invoiceNumber` then appears in the generator dropdown, marked as a distinct namespace.

### Important for De-obfuscation

`deobfuscate` also requires the same profile — it reads from the same table. A different profile means: different table, different salt, no matching entries.

---

## Configuration

```jsonc
{
  "version": 1,
  "profileName": "bank-statements",
  "mappingStore": "~/.local/share/obfuskation/bank-statements/mapping.json",

  "input": {
    "csvDelimiter": null,   // null = auto-detect (";" is the common default in European CSVs)
    "encoding": null,       // null = auto-detect (BOM, then UTF-8 check, then Windows-1252)
    "hasHeaderRecord": true
  },

  "defaults": {
    "unknownField": "error",        // error | pseudonymize | passthrough
    "redactionPlaceholder": "***",
    "emptyValues": ["-", "N/A"]     // trimmed, case-insensitive; such values pass through unchanged, never get a pseudonym
  },

  // Applies to CSV columns and JSON properties.
  // The first matching rule wins — order defines priority.
  "fields": [
    { "match": "CustomerName",     "matchType": "exact", "action": "pseudonymize", "generator": "personName" },
    { "match": "^IBAN",            "matchType": "regex", "action": "pseudonymize", "generator": "iban" },
    { "match": "DateOfBirth",      "matchType": "exact", "action": "pseudonymize", "generator": "dateShift" },
    { "match": "Amount",           "matchType": "exact", "action": "passthrough" },
    { "match": "PaymentReference", "matchType": "exact", "action": "scanText", "textRules": ["iban", "email"] },
    { "match": "$.customers[*].ssn","matchType": "jsonPath", "action": "redact" }
  ],

  "textRules": [
    { "name": "iban", "priority": 100, "generator": "iban", "pattern": "..." }
  ],

  "generators": {
    "dateShift":    { "maxDays": 400, "formats": ["dd.MM.yyyy"] },
    "email":        { "domain": "example.invalid" },
    "internalNote": { "type": "redact", "placeholder": "[removed]" }  // own placeholder, independent of defaults.redactionPlaceholder
  }
}
```

### Actions

| `action` | Effect | Reversible |
|---|---|:--:|
| `pseudonymize` | Replace value with a type-appropriate pseudonym | yes |
| `passthrough` | Keep original value unchanged | — |
| `scanText` | Scan content against text rules and replace matches | yes |
| `redact` | Replace with `***` | **no** |
| `drop` | Remove field entirely from output | **no** |
| `error` | Abort execution — decision is still pending | — |

`matchType` can be `exact` (default, case-insensitive), `regex`, or `jsonPath` (JSON only, e.g. `$.customers[*].iban`).

### Generators

| Name | Result |
|---|---|
| `personName`, `firstName`, `lastName` | Names from embedded wordlists |
| `companyName` | Company name with legal form |
| `iban` | Country code and length of original, **ISO 7064 check digits valid** |
| `bic` | Valid BIC/SWIFT format |
| `email` | Address under `example.invalid` (reserved per RFC 2606) |
| `phone` | Digits replaced, original formatting preserved |
| `numericId` | Digit count preserved, leading zeros retained |
| `dateShift` | All dates shifted by the same offset — sequence and intervals preserved |
| `dateRange` | Random date drawn from a period (`from`/`to`); without them, the original's calendar year is kept |
| `dateGeneralize` | Rounded to the start of month, quarter, or year — **not reversible** |
| `pattern` | Value built from a character mask (`A`/`a`/`9`/`X`/`\`), or format-preserving from the original if no mask is set |
| `wordlist` | Deterministic pick from a custom value list (`values`) |
| `partialMask` | Keeps `keepFirst`/`keepLast` characters visible, masks the rest — **not reversible** |
| `street`, `city`, `postalCode` | Address components from wordlists |
| `token` | Generic `TOK_A1B2C3D4`, optionally with a prefix in front |
| `redact` | Fixed `***`, or a custom `placeholder` for that namespace |

### Readable tokens: prefix

A `generators` entry of type `token` can carry a `prefix` that is prepended
to every generated pseudonym — useful for a column that doesn't match any
built-in generator but should still stay readable:

```jsonc
"generators": {
  "articleCategory": { "type": "token", "prefix": "ArticleCategory~" }
},
"fields": [
  { "match": "ArticleCategory", "action": "pseudonymize", "generator": "articleCategory" }
]
```

`TOK_A1B2C3D4` becomes `ArticleCategory~TOK_A1B2C3D4`. Allowed characters are
letters, digits, `_` and `-`, ending in `~` or `_`, at most 32 characters.
**Applies only to `token`** — every other generator produces the format of
its own value (a valid IBAN, a shifted date), and a prefix would destroy
that. `init` automatically proposes such a namespace for columns without a
matching generator, and the GUI lets you set the prefix on a rule's
"Kennzeichnung" field. Details and pitfalls (German):
[user documentation](docs/anwenderdokumentation.md).

---

## Custom Patterns (Generator Library)

Organization-specific patterns — internal assetTags, ticket numbers,
in-house IDs — don't belong in a profile that might end up in a shared
repository, and retyping them into every new profile invites drift. A
**generator library** at a fixed, per-user location solves both: it is
merged into every profile at runtime and is never written back into a
profile file.

```
~/.config/obfuskation/generators.json
```

```jsonc
{
  "version": 1,
  "generators": {
    "assetTag": { "type": "pattern", "pattern": "INV999999" }
  },
  "textRules": [
    { "name": "assetTag", "priority": 95, "pattern": "\\bINV\\d{6}\\b", "generator": "assetTag" }
  ]
}
```

Same shapes as in a profile (`generators`, `textRules`) — no second schema,
no second validation. **A profile always wins over the library** for the
same key: a profile's own `generators` entry shadows a library entry of the
same name, and a profile text rule of the same `name` replaces the
library's rule outright rather than running alongside it. That makes it
safe to build up a shared library over time without ever risking a silent,
unwanted override — whoever edits a specific profile can always be more
specific than the library.

**The file is private and does not belong in this repository.** It
typically holds organization-internal naming conventions that must not
become visible just because a profile referencing them is public.

`obfuskation library list` shows the configured keys, their base type, and
their text rule names — patterns included: unlike the mapping table, the
library holds no real data, only the shape of it, so there's nothing to
protect by hiding it. `obfuskation library path` prints the file's
location. `--no-library` runs without the library, for troubleshooting or
to reproduce a result independent of the local machine's configuration. In
the GUI, library-sourced generators appear in the generator dropdown
alongside the profile's own, marked with their origin; they are read-only
there in this release — edit the file directly.

---

## How Reversibility Works

Pseudonyms are derived deterministically:

```
seed = HMAC-SHA256(Profile Salt, Generator Name + Plaintext + Counter)
```

This yields two properties that make test data genuinely usable: the same value receives the exact same pseudonym across all files and runs, preserving relational links across customer or record IDs.

During insertion, the engine verifies that a pseudonym is neither already assigned nor present in the source data as plaintext; otherwise, the counter is incremented and recomputed.

**`dateShift` intentionally does not receive a table entry.** A shifted date could coincide with an authentic date from the same dataset, creating ambiguity. Instead, it is recalculated using the constant offset. Consequently: **dates can only be reversed in CSV and JSON**, where the column rule provides context — not in unstructured free text.

### Known Property

A format-preserving generator draws from the same pool of values as real data. With narrow ranges — such as five-digit customer IDs — an earlier assigned pseudonym might later appear as genuine plaintext. Reversible mapping remains unambiguous because the matching plaintext was itself replaced by its own pseudonym. It is noticeable only because a value appears on both sides of the dataset.

---

## Commands

```
obfuskation init [--profile <name>] [--from <file>] [--description <text>]
                 [--central] [--force]
obfuskation obfuscate <file> [-o <dest>] [--strict] [--dry-run] [--json]
obfuskation deobfuscate [<file>] [-o <dest>] [--json]
obfuskation scan <file> [--json]
obfuskation mapping list|path
obfuskation profile list [--sort name|used|changed] [--json]
obfuskation library list|path
```

Common options: `--config <path-or-profile-name>`, `--format csv|json|text`, `--allow-unsafe-store`, `--no-library`.

`--config` also accepts a plain profile name instead of a path — `--config demo` resolves to `~/.config/obfuskation/profile/demo.json` if no literal file named `demo` exists, the same central location the GUI uses. `init --central` writes there directly instead of `obfuskation.json` in the current directory, and `profile list` shows every profile the tool knows about — the central folder plus everything remembered in the usage index — with name, file count, last-used and last-modified timestamps.

- `--strict` — Fields without an explicit rule cause execution to abort, regardless of profile defaults.
- `--dry-run` — Writes neither output files nor table entries, but produces the full report.
- `--json` — Output report as JSON to stdout. Requires `-o`, otherwise report and data mix. The report contains **only counts and field names, never values**, and can safely be logged.
- `--no-library` — Runs without the generator library
  (`~/.config/obfuskation/generators.json`); useful for troubleshooting or a
  reproducible run independent of the local machine's configuration.
- `deobfuscate` without file argument reads from standard input (`stdin`).

### Exit Codes

| Code | Meaning |
|---|---|
| 0 | Success |
| 1 | General error |
| 2 | Configuration missing or invalid |
| 3 | Field without rule in strict mode |
| 4 | `scan` found suspect unreplaced values |
| 5 | Substitution table conflicting or locked |

---

## Architecture

```
src/Obfuskation.Core/    Class library — core business logic
src/Obfuskation.Cli/     Command-line interface, thin wrapper
src/Obfuskation.Gui/     Desktop UI (Avalonia), thin wrapper
tests/                   xUnit — Core and GUI tested separately
assets/                  SVG master templates of application icon
build/icon-erzeugen.sh   Generates ICO and PNG from SVG
packaging/               .desktop entry for Linux
```

The core library is headless and produces no console output. Both CLI and GUI consume it directly: all operations run through `ObfuscationEngine`, the profile is a clean data object bound to UI forms, and `ProfileValidator` returns diagnostics with field paths for direct highlighting in input fields.

The GUI view models maintain zero window references — file dialogs are injected via factories, secondary dialogs requested via events. This keeps UI logic testable without a display server, and `tests/Obfuskation.Gui.Tests` proves that GUI and CLI produce bit-for-bit identical results.

### The Application Icon

Motif: A page where the top lines are clear text and the bottom lines are replaced. Two SVG templates — full detail from 32 px, simplified for 16 and 24 px to prevent thin lines from turning into blurry gray pixels.

```fish
./build/icon-erzeugen.sh              # generates ICO and PNG
./build/icon-erzeugen.sh --behalten   # keep individual sizes for inspection
```

Generated assets are tracked in the repository; the script is only needed when modifying the icon artwork. Requires `rsvg-convert` and ImageMagick.

## Building

```fish
dotnet build
dotnet test

./build-release.sh                     # builds both win-x64 AND linux-x64
./build-release.sh --rid linux-x64     # single runtime only
./build-release.sh --cli-only          # without GUI
./build-release.sh --self-contained    # standalone, runs without .NET installed
```

Build outputs are placed in `publish/<RID>/` containing two binaries per runtime: `obfuskation` and `obfuskation-gui`. Native Avalonia UI dependencies are embedded.

Target framework is `net8.0`. With `RollForward=Major` in `Directory.Build.props`, `dotnet run` and `dotnet test` execute on whatever modern .NET runtime is installed locally.

---

## License

[MIT](LICENSE) — Free for use, modification, and distribution. Copyright notice must be retained. Provided without warranty; anyone processing personal data remains solely responsible for compliance (see *Please Read First*).

Created by **Gregor Stübner** and **Claude (Anthropic)**.
