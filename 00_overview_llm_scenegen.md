Role



You are implementing an LLM-powered Scene/Script/Map generator inside Makou Reactor (C# port: MakouReactor.CSharp). The feature adds an “LLM” button on a field/map that opens a dialog. The user enters a high-level prompt (characters, motivations, setting). The app calls the OpenAI Codex CLI (authenticated via the user's ChatGPT subscription — no API keys or per-token billing) and receives structured JSON describing dialogue, events, actors, placements, and FF7 field script instructions. The app converts this JSON into Makou Reactor’s internal structures and writes to the active field. An OpenAI-compatible HTTP endpoint (e.g. LM Studio) remains available as an offline fallback backend.



Goals



Ship a minimum viable “Generate Scene via LLM” workflow:



Button → Dialog → POST to local LLM URL → JSON result → Validate/preview → Apply to field.



Make work modular so multiple devs can contribute in parallel.



Deliverables



A working end-to-end feature behind a feature flag: MR\_ENABLE\_LLM\_GENERATOR.



Docs in /docs/llm/.



Unit tests for parsing/validation; smoke tests for UI.



Parallel Work Breakdown



01: UI button + dialog (Qt).



02: LLM transport — Codex CLI (subscription) primary, HTTP/LM Studio fallback.



03: Prompt template + response JSON schema.



04: Parser + mapper → Makou Reactor field script/events.



05: Layout generator → coordinates, walkmesh, entity placement.



06: Validation + content safety + guardrails.



07: Integration points + command wiring.



08: Tests \& fixtures.



09: Build config \& flags.



10: Contribution playbook.



Acceptance Criteria



Pressing “LLM Generate” on a field allows prompt entry and produces a preview of proposed changes (actors, dialog, triggers, placements).



“Apply” writes to the current field without corrupting flevel data.



Network failures/timeouts show actionable UI messages; nothing is applied on failure.

