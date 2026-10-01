# AI scene generation - what it does and what it does not

Describe a scene in plain language; the app asks an LLM (Codex CLI on your ChatGPT
subscription by default, or a local OpenAI-compatible server) for a *scene plan*, checks it against
the real field, shows exactly what would change, and applies it with backups.

## Pipeline

`prompt + field context -> model -> extract JSON -> parse -> lay out -> validate -> (repair) -> preview -> apply`

* **Field context** (`FieldContextBuilder`): walkmesh bounds and an ASCII picture of the walkable shape,
  existing group names, existing dialogue, gateways. Plans are placed in walkmesh units.
* **Layout** (`LayoutGenerator`): actors, props and the spawn point that fall off the walkmesh are moved
  onto the nearest walkable point; overlaps are nudged apart on walkable ground.
* **Validation** (`ScenePlanValidator`): errors block apply; warnings (off-mesh placements, long lines)
  do not.
* **Self-repair**: unparseable output or validation errors are sent back to the model with the exact
  messages, up to `max_repair_attempts` times (default 1, `0` disables). `Attempts` on the result shows
  how many calls it took.
* **Refine** (`RefineAsync`): "make the blacksmith angrier" re-prompts with the current plan and your
  instruction, through the same repair loop.
* **History**: every run is saved under `%APPDATA%\MakouReactor\history` (newest first, capped at 50).

## What apply writes to the field

| Plan content | Written? |
|---|---|
| Dialogue lines and `say` texts | Yes - appended to Section 1's text table |
| `on_enter` / `auto` events | Only with **script mode** on (see below) |
| Walkmesh regions (schema 1.1) | Only with walkmesh mode **Merge** or **Replace** |
| Actors, props, spawn point | **No** - they live in the plan JSON (placement is validated and laid out, not written) |
| `on_interact` / `zone` events, `move`, `face`, `play_music`, `set_flag`, `if_flag`, named items/encounters | **No** - skipped and listed in the preview |

The preview ("Changes to the field") always lists what will be written and what will not.

### Script mode (experimental)

`--script-mode group` (CLI) or the dialog checkbox appends **one new script group** (`ai_01`, `ai_02`, ...)
whose main script runs when the field loads. Supported steps:

* `say` -> `WINDOW` (sized to the text) + `MESSAGE`
* `wait` -> `WAIT` (milliseconds converted to 30 fps frames)
* `battle` with a numeric `encounterId` -> `BATTLE`
* `give_item` with a numeric `itemId` (0-319) and quantity 1-255 -> `STITM`

Existing groups are left byte-for-byte untouched. The emitted bytes are unit-tested and parse cleanly,
but **they have not been run in the game engine** - test on a copy, and use Revert / `restore` if
anything looks wrong.

## Safety

* Saves from the editor keep a rotating, timestamped backup (`flevel.lgp.mr-backup-yyyyMMdd-HHmmss`,
  newest 5) in addition to the single `.bak`.
* CLI `apply` writes through `SafeArchiveWriter`: backup, build beside the original, verify it re-opens and
  the field parses back to the same bytes, atomic replace. Any failure leaves the original untouched.
* In the editor, **Revert LLM apply** restores the field from the snapshot taken before apply.

## CLI

```
generate --prompt "..." [--archive flevel.lgp --field md1stin] [--dry-run] [--out plan.json]
validate plan.json
apply    --archive flevel.lgp --field md1stin --plan plan.json
         [--dry-run] [--walkmesh-mode ignore|merge|replace] [--script-mode off|group]
restore  --archive flevel.lgp [--list]
inspect  flevel.lgp [--field md1stin]      # which fields/sections fail to parse, and why
doctor
```

## Config (`llm.config.json` / settings)

`backend` (`codex`|`http`), `codex_executable`, `codex_model`, `timeout_ms`, `max_repair_attempts`,
`endpoint`, `api_key`, `model`. No API keys are required for the Codex backend.

## Known limits

* Plan coordinates are 2D; walkmesh generation produces flat meshes (one Z for the whole mesh).
* Group scripts are stored unsplit (Init and Main in one block), as the loader reads them.
* Removing or reordering script groups is not supported by the Section 1 writer (appending is).
