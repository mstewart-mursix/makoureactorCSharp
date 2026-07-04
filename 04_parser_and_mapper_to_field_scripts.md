Role

Convert ScenePlanJSON → internal ScenePlan → Makou Reactor field data (actors, dialogues, events, scripts).

Inputs

Raw JSON (string).

Schema (03).

Outputs

ScenePlan C++ model.

Mapper that:

Creates/updates actors (entities/NPCs).

Converts EventStep[] into field scripts/events using existing Makou Reactor APIs.

Generates message windows from dialog.

Steps

Parser

Use a strict JSON lib (Qt JSON is fine). Validate types/required fields.

Emit detailed errors indicating path, expected vs. actual.

Model

struct EventStep {
  enum Type { Say, Move, Face, Wait, PlayMusic, SetFlag, IfFlag, GiveItem, Battle, CustomNote };
  // union-like fields...
};

struct ScenePlan {
  QString title, model, version;
  QVector<Actor> actors;
  QVector<DialogLine> dialog;
  QVector<EventDef> events;
  Layout layout;
};


Mapper

For each actors entry → ensure actor exists; set pos/facing.

For dialog → create message resources (per speaker).

For events → map triggers to Makou Reactor event hooks (e.g., on enter).

For steps → call helpers:

emitSay(actorId, text)

emitMove(actorId, x, y, speed)

emitWait(ms)

etc.

Idempotency

Write under a new group: LLM_Generated_[timestamp].

Avoid overwriting user scripts unless confirmed.

Acceptance Criteria

Valid plan → field updates visible in UI preview before applying.

Bad plan → parser error with actionable message.