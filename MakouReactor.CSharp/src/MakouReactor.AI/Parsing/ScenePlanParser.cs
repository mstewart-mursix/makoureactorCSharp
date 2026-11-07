using System;
using System.Collections.Generic;
using System.Text.Json;

using MakouReactor.AI.Layout;
using MakouReactor.Core.Models;

namespace MakouReactor.AI.Parsing;

/// <summary>
/// Strict recursive-descent JSON parser that produces a <see cref="ScenePlan"/>
/// with path-based error reporting.
/// </summary>
public static class ScenePlanParser
{
    /// <summary>
    /// Parse result carrying either a successful plan or a detailed error message.
    /// </summary>
    public struct Result
    {
        public bool Ok;
        public ScenePlan Plan;
        public string Error;

        public static Result Failure(string error) => new() { Ok = false, Error = error };
    }

    // -----------------------------------------------------------------------
    // Public entry
    // -----------------------------------------------------------------------

    public static Result Parse(byte[] jsonBytes)
    {
        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(jsonBytes);
        }
        catch (System.Text.Json.JsonException ex)
        {
            return Result.Failure($"JSON parse error: {ex.Message}");
        }

        using var _ = doc;
        var root = doc.RootElement;

        if (root.ValueKind != JsonValueKind.Object)
            return Result.Failure("Root must be an object");

        var plan = new ScenePlan();

        // --- meta (required) ---
        if (!root.TryGetProperty("meta", out var metaEl) || metaEl.ValueKind != JsonValueKind.Object)
            return Result.Failure("Parse error at meta: expected object");

        var meta = ParseMeta(metaEl);
        if (meta == null)
            return Result.Failure("Parse error at meta: required fields title, model, version (strings)");
        plan.Meta = meta;

        // --- actors (optional) ---
        if (root.TryGetProperty("actors", out var actorsEl))
        {
            var actors = ParseActors(actorsEl);
            if (!actors.ok) return Result.Failure(actors.err);
            plan.Actors = actors.actors;
        }

        // --- dialog (optional) ---
        if (root.TryGetProperty("dialog", out var dialogEl))
        {
            var dialog = ParseDialog(dialogEl);
            if (!dialog.ok) return Result.Failure(dialog.err);
            plan.Dialog = dialog.dialog;
        }

        // --- events (optional) ---
        if (root.TryGetProperty("events", out var eventsEl))
        {
            var events = ParseEvents(eventsEl);
            if (!events.ok) return Result.Failure(events.err);
            plan.Events = events.events;
        }

        // --- layout (optional) ---
        if (root.TryGetProperty("layout", out var layoutEl))
        {
            var layoutResult = ParseLayout(layoutEl);
            if (!layoutResult.ok) return Result.Failure(layoutResult.err);
            plan.Layout = new LayoutDef
            {
                Props = layoutResult.props,
                SpawnPoint = layoutResult.spawn,
                WalkmeshHint = layoutResult.walkmeshHint
            };

            if (layoutEl.TryGetProperty("walkmesh", out var walkmeshEl))
            {
                var walkmesh = ParseWalkmesh(walkmeshEl);
                if (!walkmesh.ok) return Result.Failure(walkmesh.err);
                plan.Layout.Walkmesh = walkmesh.walkmesh;
            }
        }

        return new Result { Ok = true, Plan = plan };
    }

    // -----------------------------------------------------------------------
    // Type guard helpers
    // -----------------------------------------------------------------------

    /// <summary>
    /// Read a JSON number as int, rounding fractional values (models often emit 12.5) and clamping to the int range.
    /// </summary>
    private static int ToInt(JsonElement number)
    {
        if (number.TryGetInt32(out var exact))
            return exact;

        var value = number.GetDouble();
        if (double.IsNaN(value))
            return 0;

        return (int)Math.Clamp(Math.Round(value), int.MinValue, int.MaxValue);
    }

    private static string TypeName(JsonElement el) => el.ValueKind switch
    {
        JsonValueKind.Null => "null",
        JsonValueKind.True or JsonValueKind.False => "bool",
        JsonValueKind.Number => "number",
        JsonValueKind.String => "string",
        JsonValueKind.Array => "array",
        JsonValueKind.Object => "object",
        JsonValueKind.Undefined => "undefined",
        _ => "undefined"
    };

    private static string ErrorAt(string path, string expected, JsonElement got) =>
        $"Parse error at {path}: expected {expected}, got {TypeName(got)}";

    private static bool GetString(JsonElement obj, string key, out string value)
    {
        value = string.Empty;
        if (!obj.TryGetProperty(key, out var el) || el.ValueKind != JsonValueKind.String)
            return false;
        value = el.GetString()!;
        return true;
    }

    private static bool GetObject(JsonElement obj, string key, out JsonElement value)
    {
        value = default;
        if (!obj.TryGetProperty(key, out value) || value.ValueKind != JsonValueKind.Object)
            return false;
        return true;
    }

    private static bool GetArray(JsonElement obj, string key, out JsonElement value)
    {
        value = default;
        if (!obj.TryGetProperty(key, out value) || value.ValueKind != JsonValueKind.Array)
            return false;
        return true;
    }

    // -----------------------------------------------------------------------
    // Point / Rect helpers
    // -----------------------------------------------------------------------

    private static (bool ok, Point point, string err) ParsePoint(JsonElement v, string path)
    {
        if (v.ValueKind != JsonValueKind.Object)
            return (false, default, ErrorAt(path, "object {x:number,y:number}", v));

        if (!v.TryGetProperty("x", out var xEl) || xEl.ValueKind != JsonValueKind.Number ||
            !v.TryGetProperty("y", out var yEl) || yEl.ValueKind != JsonValueKind.Number)
            return (false, default, ErrorAt(path, "{x:number,y:number}", v));

        return (true, new Point(ToInt(xEl), ToInt(yEl)), string.Empty);
    }

    private static (bool ok, Rect rect, string err) ParseRect(JsonElement v, string path)
    {
        if (v.ValueKind != JsonValueKind.Object)
            return (false, default, ErrorAt(path, "object {x,y,w,h:number}", v));

        if (!v.TryGetProperty("x", out var xEl) || xEl.ValueKind != JsonValueKind.Number ||
            !v.TryGetProperty("y", out var yEl) || yEl.ValueKind != JsonValueKind.Number ||
            !v.TryGetProperty("w", out var wEl) || wEl.ValueKind != JsonValueKind.Number ||
            !v.TryGetProperty("h", out var hEl) || hEl.ValueKind != JsonValueKind.Number)
            return (false, default, ErrorAt(path, "{x:number,y:number,w:number,h:number}", v));

        return (true, new Rect(ToInt(xEl), ToInt(yEl), ToInt(wEl), ToInt(hEl)), string.Empty);
    }

    // -----------------------------------------------------------------------
    // Enum validators
    // -----------------------------------------------------------------------

    private static bool IsFaceDir(string s) => s is "N" or "S" or "E" or "W";
    private static bool IsTrigger(string s) => s is "on_enter" or "on_interact" or "auto" or "zone";
    private static bool IsPose(string s) => s is "idle" or "talk" or "walk";

    // -----------------------------------------------------------------------
    // Section parsers
    // -----------------------------------------------------------------------

    private static ScenePlanMeta? ParseMeta(JsonElement meta)
    {
        if (!GetString(meta, "title", out var title) ||
            !GetString(meta, "model", out var model) ||
            !GetString(meta, "version", out var version))
            return null;

        return new ScenePlanMeta { Title = title, Model = model, Version = version };
    }

    private static (bool ok, List<Actor> actors, string err) ParseActors(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            return (false, [], ErrorAt("actors", "array", el));

        var list = new List<Actor>();
        var enumr = el.EnumerateArray();

        for (int i = 0; enumr.MoveNext(); i++)
        {
            var v = enumr.Current;
            var base_ = $"actors[{i}]";

            if (v.ValueKind != JsonValueKind.Object)
                return (false, [], ErrorAt(base_, "object", v));

            if (!GetString(v, "id", out var id))
                return (false, [], ErrorAt($"{base_}.id", "string", v.GetPropertyOrNull("id")));

            var actor = new Actor { Id = id, Position = new Point(0, 0) };

            if (v.TryGetProperty("displayName", out var dn) && dn.ValueKind == JsonValueKind.String)
                actor.DisplayName = dn.GetString()!;

            if (v.TryGetProperty("pose", out var pose))
            {
                var poseStr = pose.GetString()!;
                if (!IsPose(poseStr))
                    return (false, [], ErrorAt($"{base_}.pose", "\"idle\"|\"talk\"|\"walk\"", pose));
                actor.Pose = poseStr;
            }

            if (v.TryGetProperty("position", out var pos))
            {
                var pt = ParsePoint(pos, $"{base_}.position");
                if (!pt.ok) return (false, [], pt.err);
                actor.Position = pt.point;
            }

            if (v.TryGetProperty("facing", out var face))
            {
                var faceStr = face.GetString()!;
                if (!IsFaceDir(faceStr))
                    return (false, [], ErrorAt($"{base_}.facing", "\"N\"|\"S\"|\"E\"|\"W\"", face));
                actor.Facing = faceStr[0];
            }

            list.Add(actor);
        }

        return (true, list, string.Empty);
    }

    private static (bool ok, List<DialogLine> dialog, string err) ParseDialog(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            return (false, [], ErrorAt("dialog", "array", el));

        var list = new List<DialogLine>();
        var enumr = el.EnumerateArray();

        for (int i = 0; enumr.MoveNext(); i++)
        {
            var v = enumr.Current;
            var base_ = $"dialog[{i}]";

            if (v.ValueKind != JsonValueKind.Object)
                return (false, [], ErrorAt(base_, "object", v));

            if (!GetString(v, "speakerId", out var sid))
                return (false, [], ErrorAt($"{base_}.speakerId", "string", v.GetPropertyOrNull("speakerId")));
            if (!GetString(v, "text", out var txt))
                return (false, [], ErrorAt($"{base_}.text", "string", v.GetPropertyOrNull("text")));

            list.Add(new DialogLine { SpeakerId = sid, Text = txt });
        }

        return (true, list, string.Empty);
    }

    private static (bool ok, List<EventDef> events, string err) ParseEvents(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Array)
            return (false, [], ErrorAt("events", "array", el));

        var list = new List<EventDef>();
        var enumr = el.EnumerateArray();

        for (int i = 0; enumr.MoveNext(); i++)
        {
            var v = enumr.Current;
            var base_ = $"events[{i}]";

            if (v.ValueKind != JsonValueKind.Object)
                return (false, [], ErrorAt(base_, "object", v));

            if (!GetString(v, "id", out var id))
                return (false, [], ErrorAt($"{base_}.id", "string", v.GetPropertyOrNull("id")));
            if (!GetString(v, "trigger", out var trigger))
                return (false, [], ErrorAt($"{base_}.trigger", "string", v.GetPropertyOrNull("trigger")));

            if (!IsTrigger(trigger))
                return (false, [], ErrorAt($"{base_}.trigger", "\"on_enter\"|\"on_interact\"|\"auto\"|\"zone\"", v.GetPropertyOrNull("trigger")));

            var evt = new EventDef { Id = id, Trigger = trigger };

            if (v.TryGetProperty("triggerZone", out var tz))
            {
                var r = ParseRect(tz, $"{base_}.triggerZone");
                if (!r.ok) return (false, [], r.err);
                evt.TriggerZone = r.rect;
            }

            if (!GetArray(v, "steps", out var stepsEl))
                return (false, [], ErrorAt($"{base_}.steps", "array", v.GetPropertyOrNull("steps")));

            var steps = ParseStepArray(stepsEl, $"{base_}.steps");
            if (!steps.ok) return (false, [], steps.err);
            evt.Steps = steps.steps;

            list.Add(evt);
        }

        return (true, list, string.Empty);
    }

    private static (bool ok, List<LayoutProp> props, string err, Point spawn, string walkmeshHint) ParseLayout(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object)
            return (false, [], ErrorAt("layout", "object", el), default, string.Empty);

        var props = new List<LayoutProp>();
        var spawnPoint = new Point(0, 0);
        var hint = string.Empty;

        if (el.TryGetProperty("props", out var propsEl))
        {
            if (propsEl.ValueKind != JsonValueKind.Array)
                return (false, [], ErrorAt("layout.props", "array", propsEl), default, string.Empty);

            var enumr = propsEl.EnumerateArray();
            for (int i = 0; enumr.MoveNext(); i++)
            {
                var v = enumr.Current;
                var base_ = $"layout.props[{i}]";

                if (v.ValueKind != JsonValueKind.Object)
                    return (false, [], ErrorAt(base_, "object", v), default, string.Empty);

                if (!GetString(v, "id", out var id))
                    return (false, [], ErrorAt($"{base_}.id", "string", v.GetPropertyOrNull("id")), default, string.Empty);

                var pt = ParsePoint(v.GetProperty("position"), $"{base_}.position");
                if (!pt.ok) return (false, [], pt.err, default, string.Empty);

                props.Add(new LayoutProp { Id = id, Position = pt.point });
            }
        }

        if (el.TryGetProperty("spawnPoint", out var sp))
        {
            var pt = ParsePoint(sp, "layout.spawnPoint");
            if (!pt.ok) return (false, [], pt.err, default, string.Empty);
            spawnPoint = pt.point;
        }

        if (el.TryGetProperty("restrictions", out var rest) && rest.ValueKind == JsonValueKind.Object)
        {
            if (rest.TryGetProperty("walkmeshHint", out var wh) && wh.ValueKind == JsonValueKind.String)
                hint = wh.GetString()!;
        }

        return (true, props, string.Empty, spawnPoint, hint);
    }

    private static (bool ok, WalkmeshPlan walkmesh, string err) ParseWalkmesh(JsonElement el)
    {
        var plan = new WalkmeshPlan();
        if (el.ValueKind != JsonValueKind.Object)
            return (false, plan, ErrorAt("layout.walkmesh", "object", el));

        if (!el.TryGetProperty("regions", out var regionsEl) || regionsEl.ValueKind != JsonValueKind.Array)
            return (false, plan, ErrorAt("layout.walkmesh.regions", "array",
                el.TryGetProperty("regions", out var got) ? got : default));

        var i = 0;
        foreach (var regionEl in regionsEl.EnumerateArray())
        {
            var path = $"layout.walkmesh.regions[{i}]";
            if (regionEl.ValueKind != JsonValueKind.Object)
                return (false, plan, ErrorAt(path, "object", regionEl));

            if (!GetString(regionEl, "id", out var id))
                return (false, plan, ErrorAt($"{path}.id", "string", regionEl.GetPropertyOrNull("id")));

            if (!GetArray(regionEl, "polygon", out var polygonEl))
                return (false, plan, ErrorAt($"{path}.polygon", "array", regionEl.GetPropertyOrNull("polygon")));

            var region = new WalkmeshRegion { Id = id };
            var j = 0;
            foreach (var pointEl in polygonEl.EnumerateArray())
            {
                var pt = ParsePoint(pointEl, $"{path}.polygon[{j}]");
                if (!pt.ok) return (false, plan, pt.err);
                region.Polygon.Add(pt.point);
                j++;
            }

            plan.Regions.Add(region);
            i++;
        }

        return (true, plan, string.Empty);
    }

    // -----------------------------------------------------------------------
    // Step parsing
    // -----------------------------------------------------------------------

    private static (bool ok, List<EventStep> steps, string err) ParseStepArray(JsonElement arr, string path)
    {
        var list = new List<EventStep>();
        var enumr = arr.EnumerateArray();

        for (int i = 0; enumr.MoveNext(); i++)
        {
            var step = ParseStep(enumr.Current, $"{path}[{i}]");
            if (!step.ok) return (false, [], step.err);
            list.Add(step.step);
        }

        return (true, list, string.Empty);
    }

    private static (bool ok, EventStep step, string err) ParseStep(JsonElement v, string path)
    {
        if (v.ValueKind != JsonValueKind.Object)
            return (false, new EventStep(), ErrorAt(path, "EventStep object", v));

        if (!GetString(v, "type", out var typeStr))
            return (false, new EventStep(), ErrorAt($"{path}.type", "string", v.GetPropertyOrNull("type")));

        var s = new EventStep();

        return typeStr switch
        {
            "say" => ParseSay(v, s, path),
            "move" => ParseMove(v, s, path),
            "face" => ParseFace(v, s, path),
            "wait" => ParseWait(v, s, path),
            "play_music" => ParsePlayMusic(v, s, path),
            "set_flag" => ParseSetFlag(v, s, path),
            "if_flag" => ParseIfFlag(v, s, path),
            "give_item" => ParseGiveItem(v, s, path),
            "battle" => ParseBattle(v, s, path),
            "custom_note" => ParseCustomNote(v, s, path),
            _ => (false, s, $"Parse error at {path}.type: unknown step '{typeStr}'")
        };
    }

    private static (bool, EventStep, string) ParseSay(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.Say;
        if (!GetString(o, "actorId", out var aid))
            return (false, s, ErrorAt($"{path}.actorId", "string", o.GetPropertyOrNull("actorId")));
        if (!GetString(o, "text", out var txt))
            return (false, s, ErrorAt($"{path}.text", "string", o.GetPropertyOrNull("text")));
        s.ActorId = aid;
        s.Text = txt;
        return (true, s, string.Empty);
    }

    private static (bool, EventStep, string) ParseMove(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.Move;
        if (!GetString(o, "actorId", out var aid))
            return (false, s, ErrorAt($"{path}.actorId", "string", o.GetPropertyOrNull("actorId")));
        s.ActorId = aid;
        var pt = ParsePoint(o.GetProperty("to"), $"{path}.to");
        if (!pt.ok) return (false, s, pt.err);
        s.To = pt.point;
        if (o.TryGetProperty("speed", out var sp) && sp.ValueKind == JsonValueKind.Number)
            s.Speed = sp.GetDouble();
        return (true, s, string.Empty);
    }

    private static (bool, EventStep, string) ParseFace(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.Face;
        if (!GetString(o, "actorId", out var aid))
            return (false, s, ErrorAt($"{path}.actorId", "string", o.GetPropertyOrNull("actorId")));
        if (!GetString(o, "dir", out var dir))
            return (false, s, ErrorAt($"{path}.dir", "\"N\"|\"S\"|\"E\"|\"W\"", o.GetPropertyOrNull("dir")));
        if (!IsFaceDir(dir))
            return (false, s, ErrorAt($"{path}.dir", "\"N\"|\"S\"|\"E\"|\"W\"", o.GetProperty("dir")));
        s.ActorId = aid;
        s.Dir = dir[0];
        return (true, s, string.Empty);
    }

    private static (bool, EventStep, string) ParseWait(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.Wait;
        var msEl = o.GetPropertyOrNull("ms");
        if (msEl.ValueKind != JsonValueKind.Number)
            return (false, s, ErrorAt($"{path}.ms", "number", msEl));
        s.Ms = ToInt(msEl);
        return (true, s, string.Empty);
    }

    private static (bool, EventStep, string) ParsePlayMusic(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.PlayMusic;
        if (!GetString(o, "track", out var track))
            return (false, s, ErrorAt($"{path}.track", "string", o.GetPropertyOrNull("track")));
        s.Track = track;
        return (true, s, string.Empty);
    }

    private static (bool, EventStep, string) ParseSetFlag(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.SetFlag;
        if (!GetString(o, "key", out var key))
            return (false, s, ErrorAt($"{path}.key", "string", o.GetPropertyOrNull("key")));
        var valEl = o.GetPropertyOrNull("value");
        if (valEl.ValueKind != JsonValueKind.True && valEl.ValueKind != JsonValueKind.False)
            return (false, s, ErrorAt($"{path}.value", "boolean", valEl));
        s.Key = key;
        s.Value = valEl.GetBoolean();
        return (true, s, string.Empty);
    }

    private static (bool, EventStep, string) ParseIfFlag(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.IfFlag;
        if (!GetString(o, "key", out var key))
            return (false, s, ErrorAt($"{path}.key", "string", o.GetPropertyOrNull("key")));
        s.Key = key;

        var thenEl = o.GetPropertyOrNull("then");
        if (thenEl.ValueKind != JsonValueKind.Array)
            return (false, s, ErrorAt($"{path}.then", "EventStep[]", thenEl));

        var thenEnum = thenEl.EnumerateArray();
        int ti = 0;
        while (thenEnum.MoveNext())
        {
            var r = ParseStep(thenEnum.Current, $"{path}.then[{ti}]");
            if (!r.ok) return (false, s, r.err);
            s.ThenSteps.Add(r.step);
            ti++;
        }

        if (o.TryGetProperty("else", out var elseEl))
        {
            if (elseEl.ValueKind != JsonValueKind.Array)
                return (false, s, ErrorAt($"{path}.else", "EventStep[]", elseEl));
            var elseEnum = elseEl.EnumerateArray();
            int ei = 0;
            while (elseEnum.MoveNext())
            {
                var r = ParseStep(elseEnum.Current, $"{path}.else[{ei}]");
                if (!r.ok) return (false, s, r.err);
                s.ElseSteps.Add(r.step);
                ei++;
            }
        }

        return (true, s, string.Empty);
    }

    private static (bool, EventStep, string) ParseGiveItem(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.GiveItem;
        if (!GetString(o, "itemId", out var itemId))
            return (false, s, ErrorAt($"{path}.itemId", "string", o.GetPropertyOrNull("itemId")));
        var qtyEl = o.GetPropertyOrNull("qty");
        if (qtyEl.ValueKind != JsonValueKind.Number)
            return (false, s, ErrorAt($"{path}.qty", "number", qtyEl));
        s.ItemId = itemId;
        s.Qty = ToInt(qtyEl);
        return (true, s, string.Empty);
    }

    private static (bool, EventStep, string) ParseBattle(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.Battle;
        if (!GetString(o, "encounterId", out var eid))
            return (false, s, ErrorAt($"{path}.encounterId", "string", o.GetPropertyOrNull("encounterId")));
        s.EncounterId = eid;
        return (true, s, string.Empty);
    }

    private static (bool, EventStep, string) ParseCustomNote(JsonElement o, EventStep s, string path)
    {
        s.Type = EventStepType.CustomNote;
        if (!GetString(o, "text", out var txt))
            return (false, s, ErrorAt($"{path}.text", "string", o.GetPropertyOrNull("text")));
        s.Text = txt;
        return (true, s, string.Empty);
    }
}

/// <summary>
/// Extension helpers for <see cref="JsonElement"/>.
/// </summary>
internal static class JsonElementExtensions
{
    /// <summary>
    /// Returns the property if it exists, otherwise an empty element (ValueKind = Undefined).
    /// </summary>
    public static JsonElement GetPropertyOrNull(this JsonElement el, string key)
    {
        return el.TryGetProperty(key, out var prop) ? prop : new JsonElement();
    }
}
