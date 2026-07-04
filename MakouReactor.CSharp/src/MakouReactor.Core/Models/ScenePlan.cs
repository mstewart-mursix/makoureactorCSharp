namespace MakouReactor.Core.Models;

/// <summary>
/// Actor placed on the field.
/// Maps to <c>struct Actor</c> in <c>ai/ScenePlan.h</c>.
/// </summary>
public sealed class Actor
{
    public string Id { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public string Pose { get; set; } = string.Empty;
    public Point Position { get; set; }
    public char? Facing { get; set; }

    public Actor() { }

    public Actor(string id, string displayName, string pose = "idle", int x = 0, int y = 0, char? facing = null)
    {
        Id = id;
        DisplayName = displayName;
        Pose = pose;
        Position = new Point(x, y);
        Facing = facing;
    }

    public override string ToString() => $"Actor({Id} \"{DisplayName}\" at {Position})";
}

/// <summary>
/// A single line of dialog spoken by an actor or narrator.
/// Maps to <c>struct DialogLine</c> in <c>ai/ScenePlan.h</c>.
/// </summary>
public sealed class DialogLine
{
    public string SpeakerId { get; init; } = string.Empty;
    public string Text { get; init; } = string.Empty;

    public DialogLine() { }

    public DialogLine(string speakerId, string text)
    {
        SpeakerId = speakerId;
        Text = text;
    }

    public override string ToString() => $"[{SpeakerId}]: {Text}";
}

/// <summary>
/// Type of an event step in a scene event.
/// Maps to <c>EventStep::Type</c> enum in <c>ai/ScenePlan.h</c>.
/// </summary>
public enum EventStepType
{
    Say = 0,
    Move,
    Face,
    Wait,
    PlayMusic,
    SetFlag,
    IfFlag,
    GiveItem,
    Battle,
    CustomNote
}

/// <summary>
/// A single step within a scene event (dialog, movement, condition, etc.).
/// Maps to <c>struct EventStep</c> in <c>ai/ScenePlan.h</c>.
/// </summary>
public sealed class EventStep
{
    public EventStepType Type { get; set; } = EventStepType.CustomNote;

    // Common
    public string ActorId { get; set; } = string.Empty;

    // Say
    public string Text { get; set; } = string.Empty;

    // Move
    public Point To { get; set; }
    public double Speed { get; set; }

    // Face
    public char? Dir { get; set; }

    // Wait
    public int Ms { get; set; }

    // PlayMusic
    public string Track { get; set; } = string.Empty;

    // SetFlag / IfFlag
    public string Key { get; set; } = string.Empty;
    public bool Value { get; set; }
    public List<EventStep> ThenSteps { get; set; } = new();
    public List<EventStep> ElseSteps { get; set; } = new();

    // GiveItem
    public string ItemId { get; set; } = string.Empty;
    public int Qty { get; set; }

    // Battle
    public string EncounterId { get; set; } = string.Empty;

    public EventStep() { }

    public override string ToString() => $"EventStep({Type}: {ActorId})";
}

/// <summary>
/// A named event with a trigger and a sequence of steps.
/// Maps to <c>struct EventDef</c> in <c>ai/ScenePlan.h</c>.
/// </summary>
public sealed class EventDef
{
    public string Id { get; set; } = string.Empty;
    public string Trigger { get; set; } = string.Empty;
    public Rectangle TriggerZone { get; set; }
    public List<EventStep> Steps { get; set; } = new();

    public EventDef() { }

    public EventDef(string id, string trigger, Rectangle triggerZone = default, List<EventStep>? steps = null)
    {
        Id = id;
        Trigger = trigger;
        TriggerZone = triggerZone;
        Steps = steps ?? new List<EventStep>();
    }

    public override string ToString() => $"EventDef({Id} trigger={Trigger})";
}

/// <summary>
/// A single prop placed on the scene layout.
/// Maps to <c>struct LayoutProp</c> in <c>ai/ScenePlan.h</c>.
/// </summary>
public sealed class LayoutProp
{
    public string Id { get; set; } = string.Empty;
    public Point Position { get; set; }

    public LayoutProp() { }

    public LayoutProp(string id, int x = 0, int y = 0)
    {
        Id = id;
        Position = new Point(x, y);
    }

    public override string ToString() => $"LayoutProp({Id} at {Position})";
}

/// <summary>
/// Layout definition: props, spawn point, and walkmesh hint.
/// Maps to <c>struct LayoutDef</c> in <c>ai/ScenePlan.h</c>.
/// </summary>
public sealed class LayoutDef
{
    public List<LayoutProp> Props { get; set; } = new();
    public Point SpawnPoint { get; set; }
    public string WalkmeshHint { get; set; } = string.Empty;

    public override string ToString() => $"LayoutDef({Props.Count} props, spawn={SpawnPoint})";
}

/// <summary>
/// Metadata about the scene plan (title, model, version).
/// Maps to <c>struct ScenePlanMeta</c> in <c>ai/ScenePlan.h</c>.
/// </summary>
public sealed class ScenePlanMeta
{
    public string Title { get; set; } = string.Empty;
    public string Model { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;

    public override string ToString() => $"ScenePlanMeta(\"{Title}\" v{Version})";
}

/// <summary>
/// Top-level scene plan produced by the LLM parser and consumed by the field mapper.
/// Maps to <c>struct ScenePlan</c> in <c>ai/ScenePlan.h</c>.
/// </summary>
public sealed class ScenePlan
{
    public ScenePlanMeta Meta { get; set; } = new();
    public List<Actor> Actors { get; set; } = new();
    public List<DialogLine> Dialog { get; set; } = new();
    public List<EventDef> Events { get; set; } = new();
    public LayoutDef Layout { get; set; } = new();

    public override string ToString() =>
        $"ScenePlan(\"{Meta.Title}\", {Actors.Count} actors, {Dialog.Count} dialog lines, {Events.Count} events)";
}
