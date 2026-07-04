# Repository Guidelines

Two codebases live in this repo:
- **`MakouReactor.CSharp/`** — the C# port and the primary target for new work.
  See "C# Solution" below and `10_end_user_improvements_workplan.md` for the
  phased roadmap and its rules of engagement.
- **`src/` (root)** — the original C++/Qt application, kept as the reference
  implementation for binary formats (`src/core/field/`) and as a working editor.

## C# Solution (MakouReactor.CSharp)
- Projects: `MakouReactor.Core` (models, binary IO), `MakouReactor.AI` (LLM
  pipeline: backends, prompt, parsing, validation, mapping), `MakouReactor.CLI`,
  `MakouReactor.UI.WPF`, `tests/MakouReactor.Tests` (xUnit + FluentAssertions).
- Build: `dotnet build MakouReactor.CSharp\MakouReactor.sln` (must be 0 warnings).
- Test: `dotnet test MakouReactor.CSharp\MakouReactor.sln` (all green before done).
- LLM access is via the Codex CLI on the user's ChatGPT subscription
  (`CodexCliBackend`); never add API keys. Tests must not call the real `codex`
  CLI, any network endpoint, or copyrighted game data — fake `.cmd` scripts and
  `ILLMBackend` stubs are the established patterns; game-file integration tests
  gate on the `MR_TEST_FLEVEL` environment variable and skip when unset.
- Style: C# 12, net8.0, nullable enabled, file-scoped namespaces, XML doc
  comments on public API, 4-space indentation.
- New config keys go to `LLMConfig.Load` (Core) *and* `LLMConfigLoader` (AI)
  *and* the root `llm.config.json` example, with loader tests.

## C++/Qt Application (root `src/`)

## Project Structure & Module Organization
- `src/`: C++/Qt source (core app, AI helpers, UI). Example: `src/qt/...`, `ai/*` headers.
- `tests/`: Qt Test cases and fixtures. Example: `tests/TestScenePlan.cpp`, `tests/fixtures/*.json`.
- `icons/`, `translations/`, `misc/`, `deploy/`: Assets, i18n, packaging, and helper scripts.
- Build config: `CMakeLists.txt`, `CMakePresets.json`, `qt.cmake`, `vcpkg*.json`.

## Build, Test, and Development Commands
- Configure (out-of-source):
  - `cmake -S . -B .dist/build -DCMAKE_INSTALL_PREFIX=.dist/install -DCMAKE_BUILD_TYPE=Release`
- Build:
  - `cmake --build .dist/build --config Release`
- Run tests (Qt Test via CTest):
  - `ctest --test-dir .dist/build -C Release`
- Install (optional):
  - `cmake --build .dist/build --target install`
- IDEs: Works with Qt Creator or Visual Studio 2022 (see `README.md` for setup). Ensure Qt 6.2+ is installed.

## Coding Style & Naming Conventions
- Formatter: clang-format (see `.clang-format`). Run `clang-format -i <files>` before committing.
- Indentation: Tabs for indentation (tab width 4), continuation width 4; column limit 80; Linux brace style.
- C++: Follow Qt conventions (types/classes: `CamelCase`; methods/vars: `lowerCamelCase`; constants/enums: descriptive names). Prefer explicit over implicit, avoid abbreviations.
- Includes: Keep sorted within categories; prefer local headers with quotes.

## Testing Guidelines
- Framework: Qt Test (`#include <QtTest/QtTest>`). Add tests under `tests/` as `Test<Module>.cpp`.
- Test slots: Use descriptive, snake_case names (e.g., `validator_missing_actor`).
- Fixtures: Place JSON and sample data in `tests/fixtures/` and load via paths relative to the test file.
- Running: `ctest ...` as above, or run `mr_tests` binary from the build tree.

## Commit & Pull Request Guidelines
- Commits: Short, imperative subject (≤72 chars) with optional scope (e.g., `CI:`, `Docs:`, `Fix:`). Example: `Fix: crash when opening demo disk`.
- PRs: Provide summary, rationale, and testing notes; link related issues; include before/after screenshots for UI; mention platform(s) tested.
- Checks: Ensure builds pass on all configured profiles; run `ctest` locally.

## Security & Configuration Tips
- Dependencies: Managed via CMake + vcpkg; keep `vcpkg.json` in sync when adding libs.
- Qt Version: Target Qt 6.2+ as per `README.md`. Avoid using private Qt APIs.
