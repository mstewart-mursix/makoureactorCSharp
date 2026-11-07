# AI scene generation - what it does and what it does not

Describe a scene in plain language; the app asks an LLM (Codex CLI on your ChatGPT
subscription by default, or a local OpenAI-compatible server) for a *scene plan*, checks it against
the real field, shows exactly what would change, and applies it with backups.

See `10_end_user_improvements_workplan.md` (repo root) for the per-phase status.

## Pipeline

`prompt + field context -> model -> extract JSON -> parse -> lay out -> validate -> (repair) -> preview -> apply`

* **Field context** (`FieldContextBuilder`): walkmesh bounds and an ASCII occupancy grid, existing
  groups, existing dialogue and exits. With a walkmesh, plan positions are field X/Y.
* **Layout** (`LayoutGenerator`): actors, props and the spawn point that fall off the walkmesh are moved
  onto the nearest walkable point; overlaps are nudged apart on walkable ground. When the plan proposes
  its own walkmesh (schema 1.1, `layout.walkmesh.regions`), placement uses that instead.
* **Validation** (`ScenePlanValidator`): errors block apply. Off-mesh placements and invalid walkmesh
  regions are errors, so they feed the repair loop.
* **Self-repair**: unparseable output or validation errors are sent back to the model with the exact
  messages, up to `max_repair_attempts` times (default 2, `0` disables). `Attempts` on the result lists
  each call.
* **Refine** (`RefineAsync`): "make the blacksmith angrier" re-prompts with the current plan and your
  instruction, through the same repair loop.
* **Progress**: `GenerateAsync`/`RefineAsync` take an `IProgress<LLMProgressEvent>` (starting, waiting,
  backend output lines, parsing, validating, repairing, done).
* **History** (`GenerationHistoryStore`): every run in the WPF dialog is saved under
  `%APPDATA%\MakouReactor\history` (newest first, capped at 50) and can be restored or refined.

## What apply writes to the field

Apply emits real field opcodes (`SceneScriptEmitter`): a director group plus one model group per actor
(CHAR, XYZI, DIR, talk script), `say` as WINDOW + MESSAGE, WAIT, MUSIC, flags, STITM, BATTLE, and a LINE
group per zone event. Dialogue is appended to the Section 1 text table. The preview lists every group,
script and opcode count before anything changes.

**Models.** A character's `CHAR` id is its rank among the field's model groups, and the field's Model
Loader holds one entry per model group. Each new actor therefore gets a Model Loader entry copied from a
model the field already loads: the one named by `actors[].model` (the field context lists the reusable
groups), otherwise the field's first NPC model. Party members the field already has as playable
characters (`cloud`, `barret`, ...) are not duplicated: their dialogue uses the existing entity, and
`move`/`face` steps for them are skipped with a warning.

Walkmesh regions proposed by the plan are written only with walkmesh mode **Merge** or **Replace**
(default **Ignore**). Scripts are emitted against the walkmesh the field will have afterwards. Replace
drops the existing triangles, so existing gateways, triggers and entity positions may no longer line up;
Merge is refused when the field's own walkmesh failed to parse.

## Safety

* Editor saves and CLI `apply` go through `SafeArchiveWriter`: timestamped backup, write a staging copy,
  require a byte-identical read-back, then replace atomically. The oldest backup (normally the untouched
  original) is never pruned.
* In the editor, **Revert LLM apply** restores the field from the snapshot taken before apply.

## CLI

```
generate --prompt "..." [--archive flevel.lgp --field md1stin] [--dry-run] [--out plan.json]
validate plan.json
apply    --archive flevel.lgp --field md1stin --plan plan.json [--dry-run] [--walkmesh-mode ignore|merge|replace]
revert   --archive flevel.lgp [--list]
batch    --archive flevel.lgp --script batch.json [--out dir] [--apply]
inspect  flevel.lgp [--field md1stin]      # which fields/sections fail to parse, and why
doctor
```

## Config (`llm.config.json` / settings)

`backend` (`codex`|`http`), `codex_executable`, `codex_model`, `timeout_ms`, `max_repair_attempts`,
`endpoint`, `api_key`, `model`. No API keys are required for the Codex backend.

## Known limits

* Plan coordinates are 2D; generated walkmeshes are flat (one Z for the whole mesh).
* Nothing here has been run in the game engine yet - test on a copy of `flevel.lgp`.
