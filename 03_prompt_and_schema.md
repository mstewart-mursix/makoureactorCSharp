Role



Define the prompt template and strict JSON schema so the LLM returns structured, deterministic data that can be consumed safely.



Inputs



ScenePrompt.userText



Options (generate dialog/layout/scripts)



Outputs



System Prompt (role/instructions).



User Prompt (user story \& constraints).



JSON schema (response).



Example inputs/outputs.



System Prompt (template)

You are a Final Fantasy VII field content generator for Makou Reactor.

Return ONLY a single JSON object (no prose) matching the provided JSON Schema.

Do not include comments or code fences. Be explicit and deterministic.

Field scripts should be high-level actions; do not invent engine opcodes.

Use canonical character IDs where possible (e.g., "cloud", "tifa").

Positions are in field-local 2D coordinates (pixels), origin at top-left unless specified.

All text lines ≤ 100 chars. Keep stage directions minimal.



User Prompt (template)

User Description:

{{USER\_TEXT}}



Constraints:

\- Generate sections only if flags are true:

&nbsp; - genDialog: {{genDialog}}

&nbsp; - genLayout: {{genLayout}}

&nbsp; - genScripts: {{genScripts}}

\- Field size hint (px): width={{W}}, height={{H}}

\- Avoid overlapping placements; respect basic walkable regions if mentioned.

\- Keep result coherent and lore-friendly.



JSON Schema (TypeScript for readability)

interface ScenePlanJSON {

&nbsp; meta: {

&nbsp;   title: string;

&nbsp;   model: string;

&nbsp;   version: string; // schema version e.g. "1.0"

&nbsp; };

&nbsp; actors?: {

&nbsp;   id: string;                   // "cloud", "npc\_blacksmith\_01"

&nbsp;   displayName?: string;

&nbsp;   pose?: "idle"|"talk"|"walk";

&nbsp;   position?: { x: number; y: number };

&nbsp;   facing?: "N"|"S"|"E"|"W";

&nbsp; }\[];

&nbsp; dialog?: {

&nbsp;   speakerId: string;            // must match actors.id (or "narrator")

&nbsp;   text: string;

&nbsp; }\[];

&nbsp; events?: {

&nbsp;   id: string;

&nbsp;   trigger: "on\_enter"|"on\_interact"|"auto"|"zone";

&nbsp;   triggerZone?: { x:number; y:number; w:number; h:number };

&nbsp;   steps: EventStep\[];          // see below

&nbsp; }\[];

&nbsp; layout?: {

&nbsp;   props?: { id:string; position:{x:number;y:number}; }\[];

&nbsp;   spawnPoint?: { x:number; y:number };

&nbsp;   restrictions?: { walkmeshHint?: "open"|"tight"; } // hint only

&nbsp; };

}



type EventStep =

&nbsp; | { type:"say"; actorId:string; text:string }

&nbsp; | { type:"move"; actorId:string; to:{x:number;y:number}; speed?:number }

&nbsp; | { type:"face"; actorId:string; dir:"N"|"S"|"E"|"W" }

&nbsp; | { type:"wait"; ms:number }

&nbsp; | { type:"play\_music"; track:string }

&nbsp; | { type:"set\_flag"; key:string; value:boolean }

&nbsp; | { type:"if\_flag"; key:string; then:EventStep\[]; else?:EventStep\[] }

&nbsp; | { type:"give\_item"; itemId:string; qty:number }

&nbsp; | { type:"battle"; encounterId:string }

&nbsp; | { type:"custom\_note"; text:string };



Example Response (abbrev)

{

&nbsp; "meta": {"title":"Blacksmith’s Favor","model":"qwen-2.5","version":"1.0"},

&nbsp; "actors":\[

&nbsp;   {"id":"cloud","position":{"x":220,"y":360},"facing":"N"},

&nbsp;   {"id":"npc\_blacksmith\_01","displayName":"Rurik","position":{"x":260,"y":200},"facing":"S"}

&nbsp; ],

&nbsp; "dialog":\[

&nbsp;   {"speakerId":"npc\_blacksmith\_01","text":"Need help fetching coal from the storehouse?"}

&nbsp; ],

&nbsp; "events":\[

&nbsp;   {

&nbsp;     "id":"intro\_auto",

&nbsp;     "trigger":"auto",

&nbsp;     "steps":\[

&nbsp;       {"type":"face","actorId":"cloud","dir":"N"},

&nbsp;       {"type":"say","actorId":"npc\_blacksmith\_01","text":"Over here!"}

&nbsp;     ]

&nbsp;   }

&nbsp; ],

&nbsp; "layout":{

&nbsp;   "props":\[{"id":"anvil","position":{"x":280,"y":210}}],

&nbsp;   "spawnPoint":{"x":220,"y":380}

&nbsp; }

}



Acceptance Criteria



The LLM returns only JSON matching the schema.



Invalid/missing segments are handled upstream (file 06).

