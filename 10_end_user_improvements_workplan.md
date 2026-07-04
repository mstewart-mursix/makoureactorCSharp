# 10 — End-User Improvements Work Plan (Codex Work Order)

Role

You are Codex, implementing the next generation of features for Makou Reactor's C# port
(`MakouReactor.CSharp/`). The goal: a Final Fantasy VII modder types a high-level scene
description, and the app generates dialogue, event scripts, placements, and walkmesh data —
then **applies them to a real field file** safely. This document is the complete work order:
current state, phases, file-level implementation guidance, and acceptance criteria.

Work the phases in order. Each phase must leave the solution green before the next begins.

---

## 0. Current state (do not re-do this)

Already implemented and tested (101 passing tests):

- `MakouReactor.AI/Backends/` — `ILLMBackend`, `CodexCliBackend` (spawns `codex exec`,
  prompt via stdin, `--sandbox read-only --output-last-message`, kills process tree on
  timeout), `LLMBackendFactory`, `LLMResponseExtractor`.
- `MakouReactor.AI/SceneGenerationService.cs` — pipeline: prompt → backend → JSON extract →
  `ScenePlanParser` → `LayoutGenerator.Adjust` → `ScenePlanValidator`.
- `MakouReactor.CLI` — `generate` (with `--dry-run`, `--out`), `validate <plan.json>`, `doctor`.
- `MakouReactor.UI.WPF` — minimal generator window (prompt in, plan JSON + validation out).
- `llm.config.json` — `"backend": "codex"` default; LM Studio HTTP fallback via `"http"`.

The critical gap: **`MakouReactor.Core/Models/Field.cs` is a 45-line stub.** There is no
flevel IO, so generated plans never reach a real field. `ScenePlanMapper.ApplyToField` only
sets a modified flag.

Reference implementation: the original C++ tree at `src/core/field/` in this same repo.
Port from it; it is the source of truth for all binary formats.

### Rules of engagement

- Build/test from repo root:
  `dotnet build MakouReactor.CSharp\MakouReactor.sln` and
  `dotnet test MakouReactor.CSharp\MakouReactor.sln`.
- All 101 existing tests keep passing. Every phase adds tests for its own code.
- Tests must never call the real `codex` CLI or any network endpoint. Fake backends
  (`ILLMBackend` stubs) and fake `.cmd` scripts are the established patterns — see
  `tests/MakouReactor.Tests/Backends/CodexCliBackendTests.cs`.
- Never store API keys anywhere. LLM access is Codex-CLI-subscription only (plus the
  local-HTTP fallback which uses a dummy key).
- **Never commit copyrighted game data** (flevel.lgp, field files). Integration tests that
  need real game files read a path from the `MR_TEST_FLEVEL` environment variable and
  skip when unset.
- Target: PC version (flevel.lgp) first. PS-version support is explicitly out of scope
  until Phase 1 is complete and stable. Image generation is out of scope entirely.
- C# 12 / net8.0, nullable enabled, match the existing code style (file-scoped namespaces,
  XML doc comments on public API, xUnit + FluentAssertions).

---

## Phase 1 — Field IO: read and write real FF7 fields (the critical path)

Everything else multiplies off this. Port order matters; each sub-phase is independently
testable.

### 1a. Binary foundations: LZS + LGP

FF7 PC fields live inside `flevel.lgp` (an LGP archive); each field file is LZS-compressed.

Create `MakouReactor.Core/IO/`:

- `LzsCompression.cs` — FF7's LZS (LZSS variant: 4096-byte ring buffer, 18-byte max match,
  offset/length encoding). Port from the C++ tree's LZS usage (search `src/core/` for the
  LZS implementation; the format is also documented on the Qhimm wiki "LZS format").
  API: `static byte[] Decompress(ReadOnlySpan<byte> data)`,
  `static byte[] Compress(ReadOnlySpan<byte> data)`.
  Note: compressed output does NOT need to be byte-identical to the original compressor —
  it only needs to decompress to identical bytes. Round-trip test: `Decompress(Compress(x)) == x`
  for random and structured payloads.
- `LgpArchive.cs` — LGP read/update. Port from the LGP handling referenced by
  `src/core/field/FieldArchiveIOPC.cpp` (the C++ uses an external Lgp library; the format:
  12-byte header `\0\0SQUARESOFT`, TOC of 27-byte entries (20-byte name, 4-byte offset,
  ...), lookup table, then file data blocks each prefixed with name + size).
  API: open, list entries, `byte[] ReadFile(string name)`, `WriteFile(string name, byte[] data)`
  with safe rewrite (write to temp file, then replace; see Phase 5 backups).
  Round-trip test: build a small synthetic LGP in-memory, write, re-open, verify contents.

### 1b. FF7 text encoding

Field dialogue is not ASCII; it uses FF7's character table plus control codes (names,
colors, new page, variable slots).

- `MakouReactor.Core/Text/FF7Text.cs` — bidirectional conversion
  `string ToUnicode(byte[] ff7Bytes)` / `byte[] FromUnicode(string text)`.
  The C++ side uses the external `FF7Text` class (`#include <FF7Text>` in `src/Window.cpp`,
  library pulled in via `qt.cmake`). Port the character table from that library's source
  (fetch the table from the myst6re FF7Text library if vendored, otherwise the Qhimm wiki
  "FF7 Text" table). Include control codes: `{CLOUD}`..`{CHOCOBO}` name placeholders,
  `{NEW PAGE}`, `{CHOICE}`, color tags, `{EOL}`, `0xFF` terminator. Unknown bytes map to
  `{xNN}` escapes and must round-trip.
- Tests: known byte sequences ↔ known strings, full round-trip on every table entry.

### 1c. Field script model: Opcode / Script / GrpScript / Section1File

This is the largest port. C++ sources and sizes, for scoping:

| C++ source (`src/core/field/`) | Lines | C# target (`MakouReactor.Core/Field/`) |
|---|---|---|
| `Opcode.cpp/.h` | ~5,300 | `Opcode.cs` + `Opcodes/` partials |
| `Section1File.cpp/.h` | ~1,000 | `Section1File.cs` |
| `GrpScript.cpp/.h` | ~500 | `GrpScript.cs` |
| `Script.cpp/.h` | | `Script.cs` |
| `IdFile.cpp/.h` (walkmesh) | ~200 | `IdFile.cs` |
| `InfFile.cpp/.h` (gateways/triggers) | | `InfFile.cs` |
| `Field.cpp`, `FieldPC.cpp`, `FieldIO.cpp` | | `Field.cs` (replace stub), `FieldPC.cs` |

Strategy for `Opcode`:

- Do NOT port all ~256 opcode classes by hand up front. Implement:
  1. A generic `Opcode` base: opcode id byte + raw parameter bytes, with byte-exact
     `Read(BinaryReader)` / `Write(BinaryWriter)` and the **length table** (each opcode's
     fixed size, special-cased for variable-length ones like `KAWAI`). The length table is
     in `Opcode.cpp`; port it verbatim as a `static readonly int[]`.
  2. Typed subclasses ONLY for opcodes the mapper and analyzers need (list in 1e).
     Everything else stays a `RawOpcode` that round-trips untouched.
- This keeps the port at ~10% of the C++ line count while preserving byte fidelity.

`Section1File` parse order (see `Section1File::open` in C++): header (scale, creator,
name), akao/tuto offsets, grpScript names + offsets, script entry offsets per group
(32 scripts per group), then the text section (dialog strings, FF7-encoded, offset table).
Implement `Open(byte[] section1Data)` and `Save() → byte[]`.

**The acceptance test that gates this phase:** open a real field's section 1, call
`Save()` with zero modifications, and get **byte-identical** output. Wire this as an
integration test over every field in `MR_TEST_FLEVEL` (skipped when unset) plus unit
tests over small synthetic fixtures built in code.

### 1d. Field + archive assembly

- `Field.cs` (replace the stub, keep the existing public surface —
  `ScriptsAndTexts`, `Walkmesh`, `IsModified`, `SetModified()` — so `ScenePlanMapper`
  and `ScenePlanValidator` keep compiling): lazy-open sections from the field file bytes.
  PC field file = header with 9 section pointers; section 1 = scripts/texts,
  walkmesh = section 5 (`IdFile`), triggers = section 8 (`InfFile`). Port the section
  table from `FieldPC.cpp`.
- `FieldArchive.cs`: wraps `LgpArchive`, enumerates fields by name (skip non-field
  entries like `maplist`), `Field OpenField(string name)`, `SaveField(Field)` →
  LZS-compress → `LgpArchive.WriteFile`.
- CLI: add `fields --archive <flevel.lgp>` (list) and `dump --archive <path> --field <name>`
  (print dialog text + group/script summary) commands. These double as manual
  verification tools.

### 1e. Real `ScenePlanMapper` opcode emission

Rewrite `MakouReactor.AI/Mapping/ScenePlanMapper.ApplyToField` to emit actual script data.
Verify every opcode id against the enum in `src/core/field/Opcode.h` — do not trust
memory or wikis for hex values.

Mapping table (EventStep → opcodes; names per `Opcode.h`):

| EventStep | Emission |
|---|---|
| `say` | Add text to `Section1File` texts; emit `WINDOW` (size from text length via Phase 8 width rules, safe defaults for now) + `MESSAGE` (window id, text id) |
| `move` | `MOVE` (X, Y in field units — scale by the field's `scale` header value from section 1) |
| `face` | `DIR` (map N/E/S/W to the 0–255 direction byte using the field's orientation) |
| `wait` | `WAIT` (frames = ms × 30 / 1000) |
| `play_music` | `MUSIC` (track index; resolve from a small name→index map, warn if unknown) |
| `set_flag` | `BITON`/`BITOFF` on a reserved var bank+bit; maintain a per-plan flag registry so `if_flag` matches |
| `if_flag` | `IFSW`/bit-test with forward jump labels; emit then/else blocks, patch jump offsets after emission (port the label/jump-fixup approach from the C++ `Script` editing code) |
| `give_item` | `STITM` (item id from a name→id table; `potion` etc.; warn if unknown) |
| `battle` | `BATTLE` (encounter id) |
| `custom_note` | No runtime opcode — record in the apply summary only |

Group/actor emission per plan actor: new `GrpScript` named from the actor id (8-char
limit, uniquified), script 0 = init (`CHAR`, `PC`/`SPLIT` for placement, `XYZI` at the
plan position), script 1 = main (idle `RET`), talk script for `on_interact` events.
`zone`/`on_enter` triggers: emit into the field's main group; true trigger-zone wiring via
`InfFile` can be a follow-up marked with a `custom_note`-style summary warning.

`ApplyOptions.PreviewOnly` must return the full summary (groups, scripts, opcode counts,
text additions) **without mutating the field** — Phase 5's preview depends on this.

Tests: build a synthetic empty field in memory, apply a plan with every step type, assert
the emitted byte structure decodes back (via the 1c reader) to the same opcodes; then
`Save()`/re-open round-trip.

---

## Phase 2 — Field context in the prompt

Generation currently runs blind (`field: null`, width/height guesses). Feed the model
reality; this is the biggest output-quality lever.

- `MakouReactor.AI/Prompt/FieldContextBuilder.cs`:
  `static string Describe(Field field, int maxChars = 4000)` producing a compact plain-text
  block: field name; walkmesh bounding box and walkable-region summary (from `IdFile`
  triangles — bounds, centroid, a coarse ASCII occupancy grid ~16×12 so the model "sees"
  the walkable shape); existing group names and which are character/NPC groups; up to N
  existing dialog lines (to match tone and avoid duplicate names); gateway destinations
  from `InfFile`.
- `PromptBuilder.UserPrompt` gains an optional `fieldContext` parameter, inserted under a
  `Field Context:` heading with the instruction: *"Place all positions inside the walkable
  region described below. Do not reuse existing group names."*
- `SceneGenerationService.GenerateAsync` already takes `Field?` — build and pass the
  context when non-null. CLI `generate` gains `--archive <flevel.lgp> --field <name>`.
- Validator upgrade: `ScenePlanValidator` + `LayoutGenerator` currently use rectangle
  bounds; when a real walkmesh exists, check point-in-walkmesh (point-in-triangle over
  `IdFile` triangles, 2D projection) and have `LayoutGenerator.Adjust` nudge onto the
  nearest walkable point instead of just de-overlapping.
- Tests: context builder output for a synthetic field (stable, size-capped); validator
  point-in-walkmesh cases; prompt includes context iff field provided.

## Phase 3 — Self-repair loop (auto-retry on bad output)

Turn model sloppiness into reliability. Nearly free on the subscription.

- In `SceneGenerationService.GenerateAsync`: when JSON extraction, parsing, or validation
  *errors* (not warnings) fail, don't return failure immediately. Compose a repair prompt:
  original system+user prompt, the model's previous output, and a `Fix instructions:`
  block listing the exact errors (`ScenePlanParser` path-based messages and
  `ValidationResult` issues are already precise — reuse them verbatim), ending with
  *"Return the corrected complete JSON object only."*
- Config: `"max_repair_attempts": 2` in `llm.config.json` (`LLMConfig.MaxRepairAttempts`,
  default 2, `0` disables). Loader + both config classes + defaults tests.
- Result transparency: add `SceneGenerationResult.Attempts` (list of per-attempt error
  summaries) so the UI/CLI can show "succeeded after 1 repair".
- Tests: fake backend scripted to return bad-then-good responses → succeeds with
  `Attempts.Count == 2`; always-bad → fails after max attempts with the last error;
  repair prompt contains the parser error text.

## Phase 4 — Streaming progress, cancellation UX, and refine loop

Codex runs take 1–5 minutes; the UI must not look frozen, and users iterate.

- **Streaming:** add `--json` to the `codex exec` invocation in `CodexCliBackend` and read
  stdout line-by-line as JSONL events instead of `ReadToEnd`. First, run
  `codex exec --json "say hi"` manually against the installed CLI (0.142.5) and inspect
  the actual event shapes — code against what you observe, tolerate unknown event types
  silently, and keep the `--output-last-message` file as the authoritative final answer.
  Surface progress via `IProgress<LLMProgressEvent>` (`Phase` enum: Starting, Thinking,
  Responding, Done + optional detail text) threaded through
  `ILLMBackend.RequestScenePlanAsync(req, IProgress<LLMProgressEvent>? progress = null)`
  (default-null overload keeps the interface source-compatible).
- **WPF:** indeterminate progress bar + live phase/status line during generation; elapsed
  timer; Cancel already works (`CancelAll`).
- **Refine loop:** after a successful generation, allow a follow-up instruction box
  ("make the blacksmith angrier", "add a battle at the end"). Implementation: capture the
  codex session id from the JSONL events (or use `codex exec resume --last` — verify
  which the installed version supports via `codex exec resume --help`) and send the
  follow-up in that session so the model keeps context. Fallback if resume is unavailable:
  compose a new prompt embedding the previous JSON + the refine instruction. Expose as
  `SceneGenerationService.RefineAsync(previousResult, instruction, ...)` reusing the
  Phase 3 repair machinery for validation.
- WPF history panel: keep each generation (prompt, JSON, validation) of the session in a
  list; clicking restores it. Persist to `%APPDATA%\MakouReactor\history\*.json` (cap 50).
- Tests: fake `.cmd` emitting JSONL lines → progress events observed in order; refine
  fallback path (no real codex).

## Phase 5 — Preview, diff, and safe apply

The "never corrupt flevel data" promise, made visible.

- **Preview canvas** (WPF): a `Canvas`/custom control drawing, in field coordinates:
  walkmesh triangles (from `IdFile`, light fill), existing entity positions (gray),
  planned actors (colored dots + id labels), props (squares), spawn point (star),
  trigger zones (dashed rects). Pure 2D top-down; no background art needed. Data comes
  from `Field` + `ScenePlan` — no new model types.
- **Diff summary panel:** from `ApplyToField(PreviewOnly: true)`: groups to be added,
  script/opcode counts, text entries to be added, warnings (unknown items/tracks,
  out-of-walkmesh placements). Apply button stays disabled while validation has errors.
- **Safe apply pipeline** in a new `MakouReactor.Core/IO/SafeArchiveWriter.cs`:
  1. Copy `flevel.lgp` → `flevel.lgp.mr-backup-yyyyMMdd-HHmmss` (keep newest 5, prune rest).
  2. Write the modified archive to `flevel.lgp.tmp`, verify it re-opens and the modified
     field parses, then atomically replace (`File.Replace`).
  3. On any exception: delete tmp, original untouched, surface the error.
- **Undo:** "Revert last apply" button = restore newest backup (with confirmation showing
  the backup timestamp).
- CLI: `apply --archive <flevel.lgp> --field <name> --plan <plan.json> [--dry-run]` using
  the same pipeline; `--dry-run` prints the diff summary.
- Tests: synthetic archive apply → backup exists, content updated, re-openable; failure
  injected mid-write → original bytes untouched; prune keeps 5.

## Phase 6 — Walkmesh generation and editing

Currently the plan schema only carries a `walkmeshHint` string. Make walkmesh a real output.

- Schema addition (bump `meta.version` to `1.1`, parser accepts both): optional
  `layout.walkmesh`: `{ "regions": [ { "id": string, "polygon": [{x,y}, ...] } ] }` —
  the LLM proposes 2D walkable polygons; we triangulate.
- `MakouReactor.AI/Layout/WalkmeshBuilder.cs`: ear-clipping triangulation of each simple
  polygon → `IdFile` triangles (z = field default / existing ground height); merge with or
  replace the field's existing walkmesh per an `ApplyOptions.WalkmeshMode`
  (Merge | Replace | Ignore, default Ignore for safety). Compute triangle adjacency
  (`access` data in `IdFile`) by shared edges.
- Validation: polygons must be simple (no self-intersection), ≥ 3 points, within field
  bounds; actors/props/spawn must fall inside the resulting walkmesh (reuse Phase 2
  point-in-triangle).
- `PromptBuilder.SchemaText()`: document the new block with the constraint *"only include
  walkmesh when asked to design the layout of a new area."*
- Preview canvas (Phase 5) renders proposed walkmesh in a distinct color before apply.
- Tests: triangulation of convex/concave/degenerate polygons; adjacency correctness;
  `IdFile` round-trip with generated triangles; parser accepts 1.0 (no walkmesh) and 1.1.

## Phase 7 — Onboarding, settings UI, packaging

First-run experience decides whether a modder ever reaches the features above.

- **Startup doctor** (WPF): on launch run the existing CLI doctor logic in-process
  (`CodexCliBackend.ResolveExecutable`, then `codex login status` via the backend's
  process plumbing with a 10s timeout). Three states → one dismissible banner:
  not installed (link + copyable `npm i -g @openai/codex`), not logged in (button that
  launches `codex login` in a terminal window via `UseShellExecute = true`), OK (hidden).
  Never block the window on the check — run it async.
- **Settings dialog:** edit backend (codex/http), codex executable, codex model, timeout,
  repair attempts, HTTP fallback fields; Save writes `llm.config.json` next to the exe
  (respecting the loader's search order); Test button = doctor re-run. No hand-editing
  JSON required anymore.
- **Packaging:** `dotnet publish -c Release -r win-x64 --self-contained -p:PublishSingleFile=true`
  profiles for WPF and CLI; a `publish.bat` at `MakouReactor.CSharp/`; GitHub Actions
  workflow `.github/workflows/csharp.yml` running build + tests on push and attaching
  publish artifacts on tags. Keep it separate from the existing Qt workflows.
- Tests: settings round-trip (save → loader reads back identical config).

## Phase 8 — Fidelity and power-user extras

- **FF7-accurate text metrics:** port character width tables from `src/core/FF7Font.cpp` /
  `src/widgets/TextPreview.cpp` into `MakouReactor.Core/Text/FF7TextMetrics.cs`.
  Replace the validator's 100-char rule with pixel-width-per-line checks against the
  window size `say` will emit (and auto-insert line breaks at word boundaries in the
  mapper when a line overflows). This is the difference between "validates" and
  "displays correctly in game".
- **Batch generation (CLI):** `batch --archive <flevel.lgp> --script <batch.json>` where
  batch.json = `[ { "field": name, "prompt": text, "seed"?: int }, ... ]`; runs
  sequentially (one codex process at a time), writes per-field plan JSONs + a summary
  report; `--apply` flag to also apply each passing plan via the Phase 5 pipeline.
- **Name/id tables:** centralize item name→id and music track name→index maps
  (`MakouReactor.Core/Data/GameTables.cs`) used by the mapper and validator, so `give_item`
  / `play_music` warnings become suggestions ("did you mean 'hi-potion'?").
- **Dialog window auto-placement:** compute WINDOW x/y so boxes don't cover the speaking
  actor (simple rule: opposite half of the screen from the actor's position).

---

## Cross-cutting: definition of done (every phase)

1. `dotnet build MakouReactor.CSharp\MakouReactor.sln` — 0 warnings.
2. `dotnet test` — all green, including the phase's new tests; no test touches network,
   the real codex CLI, or copyrighted game data (env-var-gated integration tests excepted).
3. Public APIs documented with XML comments; new config keys added to *both*
   `LLMConfig.Load` (Core) and `LLMConfigLoader` (AI) plus `llm.config.json` example
   plus loader tests.
4. The relevant numbered doc (00–09) updated if behavior it describes changed.
5. CLI remains scriptable: new capabilities get a CLI verb, exit codes stay meaningful
   (0 ok, 1 failure, 2 validation errors).

## Suggested execution order and why

`1a → 1b → 1c → 1d → 1e → 2 → 3 → 5 → 4 → 6 → 7 → 8`

Phase 1 unlocks everything. Phase 2 then multiplies output quality, Phase 3 multiplies
reliability, and Phase 5 (safe apply + preview) is required before encouraging users to
write to their game files — it comes before the streaming polish of Phase 4 deliberately.
Walkmesh (6) builds on the preview and validator work. Onboarding and packaging (7) ship
when there is something worth installing. Phase 8 items are independent and can be
interleaved as breathers.
