#include "PromptBuilder.h"

namespace LLM {

static const char* kSystemPrompt = R"SYS(You are a Final Fantasy VII field content generator for Makou Reactor.
Return ONLY a single JSON object (no prose) matching the provided JSON Schema.
Do not include comments or code fences. Be explicit and deterministic.
Field scripts should be high-level actions; do not invent engine opcodes.
Use canonical character IDs where possible (e.g., "cloud", "tifa").
Positions are in field-local 2D coordinates (pixels), origin at top-left unless specified.
All text lines ≤ 100 chars. Keep stage directions minimal.)SYS";

QString systemPrompt() { return QString::fromUtf8(kSystemPrompt); }

static const char* kSchemaTS = R"SCHEMA(JSON Schema (TypeScript for readability)

interface ScenePlanJSON {
  meta: {
    title: string;
    model: string;
    version: string; // schema version e.g. "1.0"
  };
  actors?: {
    id: string;                   // "cloud", "npc_blacksmith_01"
    displayName?: string;
    pose?: "idle"|"talk"|"walk";
    position?: { x: number; y: number };
    facing?: "N"|"S"|"E"|"W";
  }[];
  dialog?: {
    speakerId: string;            // must match actors.id (or "narrator")
    text: string;
  }[];
  events?: {
    id: string;
    trigger: "on_enter"|"on_interact"|"auto"|"zone";
    triggerZone?: { x:number; y:number; w:number; h:number };
    steps: EventStep[];          // see below
  }[];
  layout?: {
    props?: { id:string; position:{x:number;y:number}; }[];
    spawnPoint?: { x:number; y:number };
    restrictions?: { walkmeshHint?: "open"|"tight"; } // hint only
  };
}

type EventStep =
  | { type:"say"; actorId:string; text:string }
  | { type:"move"; actorId:string; to:{x:number;y:number}; speed?:number }
  | { type:"face"; actorId:string; dir:"N"|"S"|"E"|"W" }
  | { type:"wait"; ms:number }
  | { type:"play_music"; track:string }
  | { type:"set_flag"; key:string; value:boolean }
  | { type:"if_flag"; key:string; then:EventStep[]; else?:EventStep[] }
  | { type:"give_item"; itemId:string; qty:number }
  | { type:"battle"; encounterId:string }
  | { type:"custom_note"; text:string };)SCHEMA";

QString schemaText() { return QString::fromUtf8(kSchemaTS); }

QString userPrompt(const ScenePrompt& p, int widthPx, int heightPx)
{
    QString tpl;
    tpl += QStringLiteral("User Description:\n\n%1\n\n").arg(p.userText.trimmed());
    tpl += QStringLiteral("Constraints:\n");
    tpl += QStringLiteral("- Generate sections only if flags are true:\n");
    tpl += QStringLiteral("  - genDialog: %1\n").arg(p.genDialog ? "true" : "false");
    tpl += QStringLiteral("  - genLayout: %1\n").arg(p.genLayout ? "true" : "false");
    tpl += QStringLiteral("  - genScripts: %1\n").arg(p.genScripts ? "true" : "false");
    tpl += QStringLiteral("- Field size hint (px): width=%1, height=%2\n").arg(widthPx).arg(heightPx);
    tpl += QStringLiteral("- Avoid overlapping placements; respect basic walkable regions if mentioned.\n");
    tpl += QStringLiteral("- Keep result coherent and lore-friendly.\n\n");
    tpl += QStringLiteral("%1\n").arg(schemaText());
    return tpl;
}

LLMRequest buildRequestFrom(const ScenePrompt& p,
                            const QString& endpoint,
                            const QString& model,
                            int widthPx,
                            int heightPx)
{
    LLMRequest req;
    req.endpointUrl = endpoint;
    req.model = model;
    req.temperature = p.temperature;
    req.maxTokens = p.maxTokens;
    req.stream = false;
    req.systemPrompt = systemPrompt();
    req.userPrompt = userPrompt(p, widthPx, heightPx);
    return req;
}

} // namespace LLM

