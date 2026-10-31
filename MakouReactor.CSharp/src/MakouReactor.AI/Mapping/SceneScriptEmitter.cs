using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

using MakouReactor.AI.Layout;
using MakouReactor.Core.Data;
using MakouReactor.Core.Models;
using MakouReactor.Core.Text;

namespace MakouReactor.AI.Mapping;

/// <summary>
/// Everything a scene plan would add to a field, computed without touching the field.
/// </summary>
internal sealed class SceneEmission
{
    public string? Error { get; init; }
    public string DirectorName { get; init; } = string.Empty;
    public List<GrpScript> GrpScripts { get; } = [];
    public List<FF7String> TextObjects { get; } = [];
    public List<AppliedGroup> Groups { get; } = [];
    public List<AppliedText> Texts { get; } = [];
    public Dictionary<string, int> OpcodeCounts { get; } = new(StringComparer.Ordinal);
    public List<string> Warnings { get; } = [];
    public List<string> Notes { get; } = [];
}

/// <summary>
/// Turns a <see cref="ScenePlan"/> into new script groups and texts (EventStep to opcode table of
/// work plan phase 1e). Layout of the output:
/// <list type="bullet">
/// <item>One director group (named from <see cref="ApplyOptions.GroupNameOverride"/>, default
/// <c>LLMScene</c>): init = <c>RET</c>; main = the <c>on_enter</c>/<c>auto</c> events (<c>zone</c> events get a LINE group each),
/// guarded by a run-once bit (<c>IFUB</c> bit-off test, <c>BITON</c>) because the main script loops.</item>
/// <item>One model group per plan actor: init = <c>CHAR</c>, <c>XYZI</c> at the plan position
/// (triangle and Z from the walkmesh), optional <c>DIR</c>, <c>RET</c>; main = <c>RET</c> (idle);
/// file script 1 (talk) = its <c>on_interact</c> events then <c>RET</c>.</item>
/// <item><c>move</c>/<c>face</c> steps for an actor other than the script owner become a one-step
/// script in that actor's group (file scripts 3-31) run with <c>REQEW</c>, since field opcodes
/// such as <c>MOVE</c> and <c>DIR</c> act on the entity executing them.</item>
/// </list>
/// New groups are appended after the existing ones so no existing group id shifts.
/// </summary>
internal sealed class SceneScriptEmitter
{
    /// <summary>Default director group name.</summary>
    internal const string DefaultDirectorName = "LLMScene";

    /// <summary>Field group names are 8 bytes (<c>Section1File::save</c> writes 8 Latin-1 bytes).</summary>
    internal const int MaxGroupNameLength = 8;

    /// <summary>
    /// Widest window that stays on screen: <c>FF7Window::realPos()</c> (Opcode.h, after
    /// <c>enum OpcodeKey</c>) keeps windows between x = 8 and x + width = 312.
    /// </summary>
    internal const int MaxWindowWidth = 312 - 8;

    /// <summary>Lines per dialog page before a <c>{NEW PAGE}</c> is inserted (a safe default, not a game limit).</summary>
    internal const int MaxLinesPerPage = 4;

    /// <summary>Window id used for every <c>say</c> step.</summary>
    internal const byte DialogWindowId = 0;

    /// <summary>Top margin of dialog windows (the <c>realPos()</c> minimum).</summary>
    internal const short DialogWindowY = DialogWindowPlacement.MinY;

    /// <summary>Speaker id that always gets the default window placement.</summary>
    internal const string NarratorId = "narrator";

    /// <summary>Priority used for <c>REQEW</c> calls (0-7, packed in bits 5-7).</summary>
    internal const int RequestPriority = 1;

    /// <summary>First file script used for <c>REQEW</c> sub-scripts (0 = init/main, 1 = talk, 2 = contact).</summary>
    internal const int FirstSubScript = 3;

    /// <summary>
    /// 33-slot index of a Location group's "S5 - Go 1x" script (<c>GrpScript::scriptName</c> in
    /// src/core/field/GrpScript.cpp): runs once each time the player steps onto the group's LINE.
    /// </summary>
    internal const int GoOnceSlot = 6;

    /// <summary>Largest group count <c>Section1File.Save</c> accepts.</summary>
    internal const int MaxGroups = byte.MaxValue;

    /// <summary>
    /// N/E/S/W to the <c>DIR</c> byte. A full turn is 256 units (<c>src/3d/WalkmeshWidget.cpp</c>
    /// rotates models by <c>-360 * direction / 256</c> degrees), so quarter turns are 64. The zero
    /// direction is taken as facing south (towards the bottom of the field); the field camera's
    /// orientation is not applied.
    /// </summary>
    internal static byte DirectionByte(char dir) => char.ToUpperInvariant(dir) switch
    {
        'S' => 0,
        'W' => 64,
        'N' => 128,
        'E' => 192,
        _ => 0,
    };

    private readonly ScenePlan _plan;
    private readonly Section1File _section;
    private readonly ApplyOptions _options;
    private readonly IdFile? _walkmesh;
    private readonly FieldCamera? _camera;
    private readonly Dictionary<string, (int X, int Y, int Z)> _actorStart = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (int X, int Y, int Z)> _actorNow = new(StringComparer.Ordinal);
    private readonly int _modelLoaderCount;
    private readonly SceneEmission _out;
    private readonly HashSet<string> _usedNames = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, ActorGroup> _actors = new(StringComparer.Ordinal);
    private readonly List<ActorGroup> _actorGroups = [];
    private readonly List<ActorGroup> _lineGroups = [];
    private readonly Dictionary<string, int> _textIds = new(StringComparer.Ordinal);
    private readonly Dictionary<string, (byte Address, byte Bit)> _flags = new(StringComparer.Ordinal);
    private readonly HashSet<byte> _reservedAddresses = [];
    private readonly HashSet<string> _warned = new(StringComparer.Ordinal);
    private int _flagAddress = -1;
    private int _flagBit = 8;
    private bool _textOverflow;

    private SceneScriptEmitter(ScenePlan plan, Field field, Section1File section, ApplyOptions options, string directorName)
    {
        _plan = plan;
        _section = section;
        _options = options;
        _walkmesh = LayoutGenerator.WalkmeshOf(field);
        _camera = field.Camera is { HasCamera: true } ca ? ca.Cameras[0] : null;
        _modelLoaderCount = field.ModelLoader?.ModelCount ?? 0;
        _out = new SceneEmission { DirectorName = directorName };
    }

    /// <summary>Computes the emission; never modifies <paramref name="field"/>.</summary>
    public static SceneEmission Emit(ScenePlan plan, Field field, Section1File section, ApplyOptions options)
    {
        if (options.FlagBank is < 1 or > 15)
            return new SceneEmission { Error = $"Flag bank {options.FlagBank} is invalid; variable banks are 1-15." };

        var groupCount = section.GrpScriptCount + 1 + plan.Actors.Count + plan.Events.Count(static e => e.Trigger == "zone");
        if (groupCount > MaxGroups)
        {
            return new SceneEmission
            {
                Error = $"Applying this plan would need {groupCount} script groups; Section 1 holds at most {MaxGroups}.",
            };
        }

        var usedNames = section.GrpScripts.Select(static g => g.Name);
        var directorName = UniqueName(
            string.IsNullOrEmpty(options.GroupNameOverride) ? DefaultDirectorName : options.GroupNameOverride,
            DefaultDirectorName,
            new HashSet<string>(usedNames, StringComparer.OrdinalIgnoreCase));

        var emitter = new SceneScriptEmitter(plan, field, section, options, directorName);
        return emitter.Run();
    }

    private SceneEmission Run()
    {
        foreach (var group in _section.GrpScripts)
            _usedNames.Add(group.Name);
        _usedNames.Add(_out.DirectorName);
        ReserveExistingFlagAddresses();

        // Dialogue texts first, in plan order, so their ids are predictable.
        for (var index = 0; index < _plan.Dialog.Count; index++)
        {
            var line = _plan.Dialog[index];
            if (!string.IsNullOrWhiteSpace(line.Text))
                TextId(line.Text, $"dialog[{index}]");
        }

        var directorIndex = _section.GrpScriptCount;
        var director = new ActorGroup(directorIndex, _out.DirectorName, "director");
        CreateActorGroups(directorIndex + 1);

        var directorEvents = new ScriptBuilder();
        var hasDirectorSteps = false;
        var unreferencedDialog = new HashSet<string>(_textIds.Keys, StringComparer.Ordinal);

        foreach (var evt in _plan.Events)
        {
            var path = $"event '{evt.Id}'";
            ActorGroup host;
            ScriptBuilder builder;
            switch (evt.Trigger)
            {
                case "on_interact":
                    var target = FlattenSteps(evt.Steps)
                        .Select(step => _actors.GetValueOrDefault(step.ActorId))
                        .FirstOrDefault(static actor => actor != null);
                    if (target != null)
                    {
                        host = target;
                        builder = target.Talk;
                        break;
                    }

                    Warn($"{path}: on_interact names no plan actor in its steps; it runs when the field is entered instead.");
                    host = director;
                    builder = directorEvents;
                    hasDirectorSteps |= evt.Steps.Count > 0;
                    break;

                case "zone":
                    host = CreateZoneLineGroup(evt, path);
                    builder = host.GoOnce;
                    break;

                default:
                    host = director;
                    builder = directorEvents;
                    hasDirectorSteps |= evt.Steps.Count > 0;
                    break;
            }

            // Speaker positions for window placement: each event starts from the initial layout
            // and follows its own move steps in emission order.
            _actorNow.Clear();
            foreach (var (id, position) in _actorStart)
                _actorNow[id] = position;

            EmitSteps(builder, host, evt.Steps, path, unreferencedDialog);
        }

        // Director: init = RET; main = run-once guarded events (the main script loops).
        director.Init.Ret();
        if (hasDirectorSteps)
        {
            var once = Flag("__llm_scene_entered__", null);
            var end = director.Main.NewLabel();
            director.Main.IfBit(_options.FlagBank, once.Address, once.Bit, bitOn: false, end);
            director.Main.BitOn(_options.FlagBank, once.Address, once.Bit);
            director.Main.Append(directorEvents);
            director.Main.MarkLabel(end);
        }
        director.Main.Ret();

        foreach (var actor in _actorGroups)
        {
            actor.Main.Ret();
            if (!actor.Talk.IsEmpty)
                actor.Talk.Ret();
        }

        foreach (var line in _lineGroups)
        {
            line.Main.Ret();
            line.GoOnce.Ret();
        }

        if (unreferencedDialog.Count > 0)
        {
            Warn($"{unreferencedDialog.Count} dialog line(s) are not used by any say step; their texts were added but no MESSAGE shows them.");
        }

        if (_textOverflow)
        {
            return new SceneEmission
            {
                Error = $"Applying this plan would exceed the Section 1 text limit of {Section1File.MaxTextCount}.",
            };
        }

        Finish(director);
        foreach (var actor in _actorGroups)
            Finish(actor);
        foreach (var line in _lineGroups)
            Finish(line);

        return _out;
    }

    /// <summary>
    /// A <c>zone</c> event becomes a Location group: init places a <c>LINE</c> across the zone's
    /// middle along its longer axis (a line is the trigger shape FF7 field scripts have; the player
    /// walking into the zone crosses it), with Z from the walkmesh, and the event runs from the
    /// group's "S5 - Go 1x" script.
    /// </summary>
    private ActorGroup CreateZoneLineGroup(EventDef evt, string path)
    {
        var name = UniqueName("z_" + evt.Id, "zone", _usedNames);
        _usedNames.Add(name);
        var index = _section.GrpScriptCount + 1 + _actorGroups.Count + _lineGroups.Count;
        var group = new ActorGroup(index, name, $"zone '{evt.Id}'");
        _lineGroups.Add(group);

        var z = evt.TriggerZone;
        int x1, y1, x2, y2;
        if (z.Width >= z.Height)
        {
            y1 = y2 = z.Y + z.Height / 2;
            x1 = z.X;
            x2 = z.X + z.Width;
        }
        else
        {
            x1 = x2 = z.X + z.Width / 2;
            y1 = z.Y;
            y2 = z.Y + z.Height;
        }

        group.Init.Line(ClampShort(x1), ClampShort(y1), ClampShort(FloorZ(x1, y1)),
                        ClampShort(x2), ClampShort(y2), ClampShort(FloorZ(x2, y2)));
        group.Init.Ret();

        if (_walkmesh == null)
            WarnOnce("zone-no-walkmesh", $"{path}: the field has no walkmesh, so the trigger line uses the zone's coordinates unchanged with Z = 0.");
        _out.Notes.Add($"{path}: trigger line ({x1},{y1})-({x2},{y2}) in group \"{name}\"; the event runs on \"S5 - Go 1x\".");
        return group;
    }

    private void CreateActorGroups(int firstIndex)
    {
        var existingModels = _section.GrpScripts.Count(static g => g.Type == GrpScriptType.Model);
        var index = firstIndex;
        var modelId = existingModels;
        foreach (var actor in _plan.Actors)
        {
            var name = UniqueName(actor.Id, "actor", _usedNames);
            _usedNames.Add(name);
            var group = new ActorGroup(index, name, actor.Id);
            _actorGroups.Add(group);
            if (!_actors.TryAdd(actor.Id, group))
                Warn($"Duplicate actor id '{actor.Id}': group \"{name}\" is created but steps address the first one.");

            // Section1File::modelID(): a model group's model is its rank among model groups.
            group.Init.Char((byte)Math.Min(modelId, byte.MaxValue));
            var (x, y, z, triangle) = Place(actor.Position, $"actor '{actor.Id}'");
            group.Init.Xyzi(x, y, z, triangle);
            _actorStart.TryAdd(actor.Id, (x, y, z));
            if (actor.Facing.HasValue)
                group.Init.Dir(DirectionByte(actor.Facing.Value));
            group.Init.Ret();

            index++;
            modelId++;
        }

        if (_plan.Actors.Count > 0 && modelId > _modelLoaderCount)
        {
            Warn($"The field's model loader lists {_modelLoaderCount} model(s) but the new actors use model ids " +
                 $"{existingModels}-{modelId - 1}; add matching models in the Model Loader before running the field in game.");
        }
    }

    private void EmitSteps(ScriptBuilder builder, ActorGroup host, IEnumerable<EventStep> steps, string path, HashSet<string> unreferencedDialog)
    {
        var stepIndex = 0;
        foreach (var step in steps)
        {
            var stepPath = $"{path} step {stepIndex++} ({StepName(step.Type)})";
            switch (step.Type)
            {
                case EventStepType.Say:
                    EmitSay(builder, step, stepPath, unreferencedDialog);
                    break;

                case EventStepType.Move:
                    EmitForActor(builder, host, step, stepPath, (b, target) =>
                    {
                        var (x, y, _, _) = Place(step.To, stepPath, needTriangle: false);
                        b.Move(x, y);
                        _actorNow[step.ActorId] = (x, y, FloorZ(x, y));
                    });
                    if (step.Speed > 0)
                        Warn($"{stepPath}: speed {step.Speed.ToString(CultureInfo.InvariantCulture)} is ignored; MOVE uses the model's current speed.");
                    break;

                case EventStepType.Face:
                    if (!step.Dir.HasValue)
                    {
                        Warn($"{stepPath}: no direction; skipped.");
                        break;
                    }

                    EmitForActor(builder, host, step, stepPath, (b, _) => b.Dir(DirectionByte(step.Dir.Value)));
                    break;

                case EventStepType.Wait:
                    // WAIT counts frames; the field runs at 30 frames per second.
                    var frames = Math.Clamp((long)step.Ms * 30 / 1000, 0, ushort.MaxValue);
                    if ((long)step.Ms * 30 / 1000 > ushort.MaxValue)
                        Warn($"{stepPath}: {step.Ms} ms is longer than WAIT allows; clamped to {ushort.MaxValue} frames.");
                    builder.Wait((ushort)frames);
                    break;

                case EventStepType.PlayMusic:
                    EmitMusic(builder, step, stepPath);
                    break;

                case EventStepType.SetFlag:
                    if (string.IsNullOrWhiteSpace(step.Key))
                    {
                        Warn($"{stepPath}: empty flag key; skipped.");
                        break;
                    }

                    var flag = Flag(step.Key, stepPath);
                    if (step.Value)
                        builder.BitOn(_options.FlagBank, flag.Address, flag.Bit);
                    else
                        builder.BitOff(_options.FlagBank, flag.Address, flag.Bit);
                    break;

                case EventStepType.IfFlag:
                    EmitIfFlag(builder, host, step, stepPath, unreferencedDialog);
                    break;

                case EventStepType.GiveItem:
                    EmitGiveItem(builder, step, stepPath);
                    break;

                case EventStepType.Battle:
                    if (TryParseId(step.EncounterId, ["battle", "encounter"], out var battleId))
                        builder.Battle(battleId);
                    else
                        Warn($"{stepPath}: encounter '{step.EncounterId}' is not a numeric battle id (e.g. \"123\" or \"battle:123\"); BATTLE not emitted.");
                    break;

                case EventStepType.CustomNote:
                    _out.Notes.Add($"{stepPath}: {step.Text}");
                    break;
            }
        }
    }

    private void EmitSay(ScriptBuilder builder, EventStep step, string path, HashSet<string> unreferencedDialog)
    {
        if (string.IsNullOrWhiteSpace(step.Text))
        {
            Warn($"{path}: empty text; skipped.");
            return;
        }

        var textId = TextId(step.Text, path);
        if (textId < 0)
            return;

        var wrapped = _out.TextObjects[textId - _section.TextCount];
        unreferencedDialog.Remove(wrapped.Value);
        // One WINDOW per say step: every page of the text (MaxLinesPerPage, {NEW PAGE}) reuses it,
        // and CalcWindowSize sizes it for the widest line and tallest page, so the placement
        // below covers all pages.
        var (width, height) = FF7TextMetrics.CalcWindowSize(wrapped.RawBytes ?? FF7TextCodec.Encode(wrapped.Value));
        var (x, y) = PlaceWindow(step.ActorId, width, height);
        builder.Window(DialogWindowId, x, y, (ushort)width, (ushort)height);
        builder.Message(DialogWindowId, (byte)textId);
    }

    /// <summary>
    /// Dialog window position for a <c>say</c> step (see <see cref="DialogWindowPlacement"/>): the
    /// opposite vertical half from the speaker. The speaker's screen y comes from the plan position
    /// directly when the field has no walkmesh (positions are then 320x240 screen pixels,
    /// <see cref="LayoutGenerator.DefaultBounds"/>), from the field camera's projection when the
    /// field has a walkmesh and a camera, and otherwise from an approximation over the walkmesh
    /// bounds (with a warning). Narrator and unknown speakers keep the default placement.
    /// </summary>
    private (short X, short Y) PlaceWindow(string speaker, int width, int height)
    {
        if (string.Equals(speaker, NarratorId, StringComparison.OrdinalIgnoreCase)
            || !_actorNow.TryGetValue(speaker, out var position))
        {
            return DialogWindowPlacement.Default(width, height);
        }

        double screenY;
        if (_walkmesh == null)
        {
            screenY = position.Y;
        }
        else if (_camera != null
                 && DialogWindowPlacement.TryProjectToScreen(_camera, position.X, position.Y, position.Z, out _, out var projected))
        {
            screenY = projected;
        }
        else
        {
            WarnOnce("window-placement-approximate",
                (_camera == null ? "The field has no camera data" : "The field camera could not project an actor") +
                ": dialog windows were placed from the speaker's Y within the walkmesh bounds (larger Y taken as higher on screen); placement is approximate.");
            screenY = DialogWindowPlacement.ApproximateScreenY(WalkmeshGeometry.Bounds(_walkmesh)!.Value, position.Y);
        }

        return DialogWindowPlacement.AvoidingActor(screenY, width, height);
    }

    /// <summary>Walkmesh floor height at (x, y), or 0 off the walkmesh or without one.</summary>
    private int FloorZ(int x, int y) =>
        _walkmesh != null && WalkmeshGeometry.TryLocate(_walkmesh, new Point(x, y), out _, out var z) ? z : 0;

    private void EmitForActor(ScriptBuilder builder, ActorGroup host, EventStep step, string path, Action<ScriptBuilder, ActorGroup> emit)
    {
        if (!_actors.TryGetValue(step.ActorId, out var target))
        {
            Warn($"{path}: '{step.ActorId}' is not a plan actor; skipped.");
            return;
        }

        if (ReferenceEquals(target, host))
        {
            emit(builder, target);
            return;
        }

        var fileScript = target.NextSubScript;
        if (fileScript > 31)
        {
            Warn($"{path}: group \"{target.Name}\" has no free script slot left; skipped.");
            return;
        }

        var sub = new ScriptBuilder();
        emit(sub, target);
        sub.Ret();
        target.SubScripts.Add(sub);
        builder.ReqEw((byte)target.Index, fileScript, RequestPriority);
    }

    private void EmitMusic(ScriptBuilder builder, EventStep step, string path)
    {
        var lookup = GameTables.FindMusic(step.Track);
        if (!lookup.Found)
        {
            var hint = lookup.Suggestions.Count > 0
                ? $" (did you mean {string.Join(", ", lookup.Suggestions.Select(static t => $"'{t.Name}'"))}?)"
                : string.Empty;
            Warn($"{path}: unknown music track '{step.Track}'{hint}; MUSIC not emitted.");
            return;
        }

        // MUSIC plays an entry of the field's own AKAO list; each entry's akaoID is the global track
        // (TutFileStandard::akaoID, and FieldArchive.cpp names exported field music by it).
        var track = lookup.Match!;
        var entries = _section.TutorialsAndSounds?.Entries ?? Array.Empty<TutorialEntry>();
        var entry = entries.FirstOrDefault(e => e.Kind == TutorialEntryKind.Music && e.MusicId == track.Id);
        if (entry == null || entry.Id > byte.MaxValue)
        {
            Warn($"{path}: track '{track.Name}' ({track.Title}) is not in this field's music list; MUSIC not emitted.");
            return;
        }

        builder.Music((byte)entry.Id);
    }

    private void EmitIfFlag(ScriptBuilder builder, ActorGroup host, EventStep step, string path, HashSet<string> unreferencedDialog)
    {
        if (string.IsNullOrWhiteSpace(step.Key))
        {
            Warn($"{path}: empty flag key; skipped with its branches.");
            return;
        }

        if (!_flags.ContainsKey(step.Key))
            Warn($"{path}: flag '{step.Key}' is tested before any set_flag in this plan.");
        var flag = Flag(step.Key, path);

        var elseLabel = builder.NewLabel();
        builder.IfBit(_options.FlagBank, flag.Address, flag.Bit, bitOn: true, elseLabel);
        EmitSteps(builder, host, step.ThenSteps, $"{path} then", unreferencedDialog);
        if (step.ElseSteps.Count > 0)
        {
            var endLabel = builder.NewLabel();
            builder.JumpForward(endLabel);
            builder.MarkLabel(elseLabel);
            EmitSteps(builder, host, step.ElseSteps, $"{path} else", unreferencedDialog);
            builder.MarkLabel(endLabel);
        }
        else
        {
            builder.MarkLabel(elseLabel);
        }
    }

    private void EmitGiveItem(ScriptBuilder builder, EventStep step, string path)
    {
        // Opcode::_item (Opcode.cpp line 4953): ids 0-127 items, 128-255 weapons, 256-287 armor,
        // 288-319 accessories. Item names come from kernel data, which is not bundled.
        if (!TryParseId(step.ItemId, ["item"], out var itemId) || itemId >= 320)
        {
            Warn($"{path}: item '{step.ItemId}' is not a numeric item id 0-319 (e.g. \"12\" or \"item:12\"); item names need the game's kernel data; STITM not emitted.");
            return;
        }

        var quantity = step.Qty;
        if (quantity is < 1 or > byte.MaxValue)
        {
            var clamped = Math.Clamp(quantity, 1, byte.MaxValue);
            Warn($"{path}: quantity {quantity} clamped to {clamped}.");
            quantity = clamped;
        }

        builder.StItm(itemId, (byte)quantity);
    }

    private (short X, short Y, short Z, ushort Triangle) Place(Point position, string path, bool needTriangle = true)
    {
        var x = ClampShort(position.X);
        var y = ClampShort(position.Y);
        if (_walkmesh == null)
        {
            WarnOnce("no-walkmesh",
                "The field has no walkmesh: positions are written unchanged as field X/Y, with Z = 0 and triangle 0.");
            return (x, y, 0, 0);
        }

        if (!needTriangle)
            return (x, y, 0, 0);

        var point = new Point(x, y);
        if (!WalkmeshGeometry.TryLocate(_walkmesh, point, out var triangle, out var z))
        {
            var snapped = WalkmeshGeometry.NearestWalkable(_walkmesh, point);
            Warn($"{path}: ({x}, {y}) is off the walkmesh; placed at ({snapped.X}, {snapped.Y}).");
            point = snapped;
            x = ClampShort(snapped.X);
            y = ClampShort(snapped.Y);
            if (!WalkmeshGeometry.TryLocate(_walkmesh, point, out triangle, out z))
                return (x, y, 0, 0);
        }

        return (x, y, z, (ushort)triangle);
    }

    private int TextId(string original, string path)
    {
        var wrapped = WrapText(original, out var overflow);
        if (overflow)
            Warn($"{path}: a word is wider than a dialog window; the window will be clipped.");

        var encoded = FF7TextCodec.Encode(wrapped, includeTerminator: true, japanese: _section.JapaneseText);
        var text = FF7String.FromRaw(encoded, _section.JapaneseText);
        if (_textIds.TryGetValue(text.Value, out var existing))
            return existing;

        var id = _section.TextCount + _out.TextObjects.Count;
        if (id >= Section1File.MaxTextCount)
        {
            _textOverflow = true;
            return -1;
        }

        _textIds[text.Value] = id;
        _out.TextObjects.Add(text);
        _out.Texts.Add(new AppliedText(id, text.Value));
        return id;
    }

    /// <summary>
    /// Inserts line breaks at word boundaries so no line is wider than <see cref="MaxWindowWidth"/>
    /// (measured with <see cref="FF7TextMetrics"/>), and a page break every
    /// <see cref="MaxLinesPerPage"/> lines.
    /// </summary>
    internal static string WrapText(string text, out bool overflow)
    {
        overflow = false;
        var lines = new List<string>();
        foreach (var paragraph in text.Replace("\r\n", "\n", StringComparison.Ordinal).Split('\n'))
        {
            if (LineWidth(paragraph) <= MaxWindowWidth)
            {
                lines.Add(paragraph);
                continue;
            }

            var current = new StringBuilder();
            foreach (var word in paragraph.Split(' ', StringSplitOptions.RemoveEmptyEntries))
            {
                var candidate = current.Length == 0 ? word : $"{current} {word}";
                if (current.Length == 0 || LineWidth(candidate) <= MaxWindowWidth)
                {
                    current.Clear().Append(candidate);
                    if (LineWidth(word) > MaxWindowWidth)
                        overflow = true;
                    continue;
                }

                lines.Add(current.ToString());
                current.Clear().Append(word);
                if (LineWidth(word) > MaxWindowWidth)
                    overflow = true;
            }

            lines.Add(current.ToString());
        }

        var sb = new StringBuilder();
        var lineOnPage = 0;
        for (var index = 0; index < lines.Count; index++)
        {
            if (index > 0)
            {
                if (lineOnPage >= MaxLinesPerPage)
                {
                    sb.Append("{NEW PAGE}");
                    lineOnPage = 0;
                }
                else
                {
                    sb.Append('\n');
                }
            }

            sb.Append(lines[index]);
            lineOnPage = lines[index].Contains("{NEW PAGE", StringComparison.OrdinalIgnoreCase) ? 1 : lineOnPage + 1;
        }

        return sb.ToString();
    }

    /// <summary>Window width needed by one line when it is not the first (the first is 3 px narrower).</summary>
    private static int LineWidth(string line) =>
        FF7TextMetrics.CalcUncappedSize(FF7TextCodec.Encode(line, includeTerminator: false)).Width + 3;

    private (byte Address, byte Bit) Flag(string key, string? path)
    {
        if (_flags.TryGetValue(key, out var existing))
            return existing;

        if (_flagBit > 7)
        {
            do
            {
                _flagAddress = _flagAddress < 0 ? byte.MaxValue : _flagAddress - 1;
            }
            while (_flagAddress >= 0 && _reservedAddresses.Contains((byte)_flagAddress));

            if (_flagAddress < 0)
            {
                Warn($"{path ?? "scene"}: no free byte left in variable bank {_options.FlagBank}; flag '{key}' shares bank {_options.FlagBank}:0xFF.");
                _flagAddress = byte.MaxValue;
            }

            _flagBit = 0;
        }

        var flag = ((byte)_flagAddress, (byte)_flagBit++);
        _flags[key] = flag;
        return flag;
    }

    /// <summary>
    /// Reserves every address of the flag bank already referenced by the field's variable
    /// opcodes (best effort over the common shapes: <c>IF*</c> 0x14-0x19, binary/bit operations
    /// 0x80-0x94 and 0x9A-0x9B, unary operations 0x95-0x99), using <c>B1</c> = high nibble and
    /// <c>B2</c> = low nibble of the banks byte (Opcode.h line 31).
    /// </summary>
    private void ReserveExistingFlagAddresses()
    {
        var bank = _options.FlagBank;
        foreach (var opcode in _section.GrpScripts.SelectMany(static g => g.Scripts).SelectMany(static s => s.RawOpcodes))
        {
            var bytes = opcode.Bytes;
            if (opcode.IsTruncated || bytes.Length < 3)
                continue;

            var bank1 = bytes[1] >> 4;
            var bank2 = bytes[1] & 0x0F;
            switch (opcode.Id)
            {
                case >= 0x14 and <= 0x19:
                    var value2Offset = opcode.Id is 0x14 or 0x15 ? 3 : 4;
                    if (bank1 == bank)
                        Reserve(bytes[2]);
                    if (bank2 == bank && bytes.Length > value2Offset)
                        Reserve(bytes[value2Offset]);
                    break;
                case >= 0x95 and <= 0x99:
                    if (bank2 == bank)
                        Reserve(bytes[2]);
                    break;
                case >= 0x80 and <= 0x9B:
                    if (bank1 == bank)
                        Reserve(bytes[2]);
                    if (bank2 == bank && bytes.Length > 3)
                        Reserve(bytes[3]);
                    break;
            }
        }

        void Reserve(byte address)
        {
            _reservedAddresses.Add(address);
            if (address < byte.MaxValue)
                _reservedAddresses.Add((byte)(address + 1)); // word-sized accesses
        }
    }

    private void Finish(ActorGroup group)
    {
        var grp = new GrpScript(group.Name);
        var scripts = new List<AppliedScript>();

        void Add(int slot, ScriptBuilder builder, string? label = null)
        {
            if (builder.IsEmpty)
                return;

            var bytes = builder.Compile();
            grp.SetScript(slot, new Script(bytes));
            var names = RawOpcodeReader.Read(bytes).Select(static op => op.Name).ToArray();
            foreach (var name in names)
                _out.OpcodeCounts[name] = _out.OpcodeCounts.GetValueOrDefault(name) + 1;
            scripts.Add(new AppliedScript(slot, label ?? SlotLabel(slot), bytes, names));
        }

        Add(0, group.Init);
        Add(1, group.Main);
        Add(2, group.Talk);
        Add(GoOnceSlot, group.GoOnce, "S5 - Go 1x");
        for (var index = 0; index < group.SubScripts.Count; index++)
            Add(FirstSubScript + index + 1, group.SubScripts[index]);

        _out.GrpScripts.Add(grp);
        _out.Groups.Add(new AppliedGroup(group.Index, group.Name, group.Role, scripts));
    }

    /// <summary>
    /// Sanitizes <paramref name="wanted"/> to letters, digits and '_', cuts it to 8 characters and
    /// appends a counter when the name is taken (case-insensitively).
    /// </summary>
    internal static string UniqueName(string wanted, string fallback, ISet<string> used)
    {
        var sb = new StringBuilder();
        foreach (var c in wanted ?? string.Empty)
        {
            if (c is (>= 'a' and <= 'z') or (>= 'A' and <= 'Z') or (>= '0' and <= '9') or '_')
                sb.Append(c);
            else if (c is ' ' or '-' or '.')
                sb.Append('_');
        }

        var baseName = sb.ToString().Trim('_');
        if (baseName.Length == 0)
            baseName = fallback;
        if (baseName.Length > MaxGroupNameLength)
            baseName = baseName[..MaxGroupNameLength];

        if (!used.Contains(baseName))
            return baseName;

        for (var counter = 2; ; counter++)
        {
            var suffix = counter.ToString(CultureInfo.InvariantCulture);
            var candidate = baseName[..Math.Min(baseName.Length, MaxGroupNameLength - suffix.Length)] + suffix;
            if (!used.Contains(candidate))
                return candidate;
        }
    }

    /// <summary>Accepts "12" or "prefix:12" (any of <paramref name="prefixes"/>, case-insensitive).</summary>
    internal static bool TryParseId(string? value, IReadOnlyList<string> prefixes, out ushort id)
    {
        id = 0;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        var text = value.Trim();
        var colon = text.IndexOf(':');
        if (colon >= 0)
        {
            var prefix = text[..colon].Trim();
            if (!prefixes.Any(p => string.Equals(p, prefix, StringComparison.OrdinalIgnoreCase)))
                return false;
            text = text[(colon + 1)..].Trim();
        }

        return ushort.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out id);
    }

    internal static string SlotLabel(int slot) => slot switch
    {
        0 => "S0 - Init",
        1 => "S0 - Main",
        2 => "S1 - Talk",
        3 => "S2 - Contact",
        _ => $"Script {slot - 1}",
    };

    private static string StepName(EventStepType type) => type switch
    {
        EventStepType.Say => "say",
        EventStepType.Move => "move",
        EventStepType.Face => "face",
        EventStepType.Wait => "wait",
        EventStepType.PlayMusic => "play_music",
        EventStepType.SetFlag => "set_flag",
        EventStepType.IfFlag => "if_flag",
        EventStepType.GiveItem => "give_item",
        EventStepType.Battle => "battle",
        _ => "custom_note",
    };

    private static IEnumerable<EventStep> FlattenSteps(IEnumerable<EventStep> steps)
    {
        foreach (var step in steps)
        {
            yield return step;
            foreach (var nested in FlattenSteps(step.ThenSteps).Concat(FlattenSteps(step.ElseSteps)))
                yield return nested;
        }
    }

    private static short ClampShort(int value) => (short)Math.Clamp(value, short.MinValue, short.MaxValue);

    private void Warn(string message) => _out.Warnings.Add(message);

    private void WarnOnce(string key, string message)
    {
        if (_warned.Add(key))
            Warn(message);
    }

    private sealed class ActorGroup(int index, string name, string role)
    {
        public int Index { get; } = index;
        public string Name { get; } = name;
        public string Role { get; } = role;
        public ScriptBuilder Init { get; } = new();
        public ScriptBuilder Main { get; } = new();
        public ScriptBuilder Talk { get; } = new();

        /// <summary>Location groups only: the "S5 - Go 1x" script.</summary>
        public ScriptBuilder GoOnce { get; } = new();
        public List<ScriptBuilder> SubScripts { get; } = [];
        public int NextSubScript => FirstSubScript + SubScripts.Count;
    }
}
