Role

Implement the LLM transport layer. The primary backend is the **OpenAI Codex CLI** running against the user's **ChatGPT subscription** — no API keys, no per-token billing. An OpenAI-compatible HTTP backend (e.g. LM Studio) is kept as an offline fallback.

Status: **implemented** in `MakouReactor.CSharp/src/MakouReactor.AI/Backends/`.

Architecture

- `ILLMBackend` — transport interface: `RequestScenePlanAsync(LLMRequest) → Task<LLMRawResult>`, plus `CancelAll()`.
- `CodexCliBackend` — default. Spawns `codex exec` as a subprocess:
  - Prompt (system + user + schema) is written to **stdin** (avoids command-line length/quoting limits).
  - Flags: `--skip-git-repo-check --color never --sandbox read-only --output-last-message <tempfile>`, optional `--model <m>`.
  - Working directory is the temp dir and the sandbox is read-only, so Codex cannot touch the repo or flevel data.
  - The final message is read from the temp file (stdout fallback), markdown fences / surrounding prose are stripped, and the bare scene-plan JSON is returned.
  - Timeout kills the whole process tree; errors (not logged in, CLI missing, non-JSON output) surface as `LLMRawResult.Error`.
  - On Windows the npm shim (`codex.cmd`) is resolved via PATH and run under `cmd.exe`.
- `LLMClient` (in `Http/`) — fallback `ILLMBackend` for OpenAI-style chat-completions endpoints.
- `LLMBackendFactory` — picks the backend from `LLMConfig.Backend` (`"codex"` default, `"http"` fallback).
- `LLMResponseExtractor` — normalizes either backend's output to bare scene-plan JSON for the parser (file 04).
- `SceneGenerationService` — end-to-end pipeline: prompt → backend → extract → parse → layout adjust → validate.

Auth

Handled entirely outside the app: `codex login` (sign in with the ChatGPT plan). `codex login status` should report "Logged in using ChatGPT". Nothing secret is stored in `llm.config.json`.

Configuration (`llm.config.json`)

```json
{
  "backend": "codex",          // "codex" (default) or "http"
  "codex_executable": "codex", // path or PATH-resolved command name
  "codex_model": "",           // empty = Codex CLI's configured default
  "timeout_ms": 0,             // 0 = backend default (300s codex, 60s http)

  "endpoint": "http://localhost:1234/v1/chat/completions",
  "api_key": "lm-studio",      // http fallback only — never used by codex
  "model": "qwen/qwen3-coder-30b"
}
```

Acceptance Criteria

- Prompt is delivered via stdin; no user content on the command line. ✅
- Codex failures (missing CLI, not logged in, timeout, non-JSON output) surface as user-friendly messages; nothing is applied on failure. ✅
- Timeout/cancel kills the codex process tree. ✅
- HTTP fallback still works with LM Studio's default OpenAI-style API. ✅
- Covered by `tests/MakouReactor.Tests/Backends/` (fake codex .cmd scripts) — no network or subscription needed to run tests. ✅
