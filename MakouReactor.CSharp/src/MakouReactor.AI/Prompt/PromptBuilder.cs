using MakouReactor.Core.Models;

namespace MakouReactor.AI.Prompt;

/// <summary>
/// Composes system prompts, JSON schema text, and user prompts
/// for scene plan generation via an LLM.
/// </summary>
public static class PromptBuilder
{
    /// <summary>
    /// The system prompt that instructs the LLM to act as a FFVII field content generator.
    /// </summary>
    public static string SystemPrompt() =>
        """
        You are a Final Fantasy VII field content generator for Makou Reactor.
        Return ONLY a single JSON object (no prose) matching the provided JSON Schema.
        Do not include comments or code fences. Be explicit and deterministic.
        Field scripts should be high-level actions; do not invent engine opcodes.
        Use canonical character IDs where possible (e.g., "cloud", "tifa").
        Positions are in field-local 2D coordinates (pixels), origin at top-left unless specified.
        All text lines <= 100 chars. Keep stage directions minimal.
        """;

    /// <summary>
    /// JSON schema expressed as TypeScript interfaces for LLM readability.
    /// </summary>
    public static string SchemaText() =>
        """
        JSON Schema (TypeScript for readability)

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
            // schema 1.1 (set meta.version to "1.1" when used). Only include walkmesh when asked to
            // design the layout of a new area. Each polygon is a simple outline (>= 3 points,
            // no self-intersections); actors, props and spawnPoint must lie inside the regions.
            walkmesh?: { regions: { id:string; polygon:{x:number;y:number}[] }[] };
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
          | { type:"custom_note"; text:string };
        """;

    /// <summary>
    /// Build the user prompt from a <see cref="ScenePrompt"/> and field size hints.
    /// </summary>
    public static string UserPrompt(ScenePrompt p, int widthPx, int heightPx, string? fieldContext = null)
    {
        var lines = new System.Collections.Generic.List<string>();

        lines.Add("User Description:");
        lines.Add(string.Empty);
        lines.Add(p.UserText.Trim());
        lines.Add(string.Empty);
        lines.Add("Constraints:");
        lines.Add("- Generate sections only if flags are true:");
        lines.Add($"  - genDialog: {(p.GenDialog ? "true" : "false")}");
        lines.Add($"  - genLayout: {(p.GenLayout ? "true" : "false")}");
        lines.Add($"  - genScripts: {(p.GenScripts ? "true" : "false")}");
        lines.Add($"- Field size hint (px): width={widthPx}, height={heightPx}");
        if (p.GenScripts)
        {
            lines.Add("- Events that should run by themselves when the field loads use trigger \"on_enter\".");
            lines.Add("- Only these steps become game script: say, wait, battle (encounterId a plain number) and");
            lines.Add("  give_item (itemId a plain number). Other steps are kept as notes, so prefer these for the core scene.");
        }

        lines.Add("- Avoid overlapping placements; respect basic walkable regions if mentioned.");
        lines.Add("- Keep result coherent and lore-friendly.");
        if (!string.IsNullOrWhiteSpace(fieldContext))
        {
            lines.Add(string.Empty);
            lines.Add("Field Context:");
            lines.Add(fieldContext.Trim());
            lines.Add("Place all positions inside the walkable region described above. " +
                      "Do not reuse existing group names.");
        }

        lines.Add(string.Empty);
        lines.Add(SchemaText());

        return string.Join('\n', lines);
    }

    /// <summary>
    /// Convenience: compose a complete <see cref="LLMRequest"/> from a <see cref="ScenePrompt"/>.
    /// </summary>
    public static LLMRequest BuildRequestFrom(ScenePrompt p, string endpoint, string model,
                                              int widthPx, int heightPx)
    {
        return new LLMRequest
        {
            EndpointUrl = endpoint,
            Model = model,
            Temperature = p.Temperature,
            MaxTokens = p.MaxTokens,
            Stream = false,
            SystemPrompt = SystemPrompt(),
            UserPrompt = UserPrompt(p, widthPx, heightPx)
        };
    }
}
