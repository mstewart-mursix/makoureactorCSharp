using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Mapping;

/// <summary>The result of compiling a plan's events into field script bytes.</summary>
public sealed class CompiledScene
{
    /// <summary>
    /// Script bytes for one group's script 0: an empty Init (RET), the main sequence, and a final RET.
    /// Empty when nothing could be compiled.
    /// </summary>
    public byte[] Script { get; init; } = [];

    /// <summary>Number of plan steps turned into opcodes.</summary>
    public int StepsWritten { get; init; }

    /// <summary>One line per step that was left out, with the reason.</summary>
    public IReadOnlyList<string> Skipped { get; init; } = [];

    public bool IsEmpty => Script.Length == 0;
}

/// <summary>
/// Compiles the safe subset of a plan's events into FF7 field script opcodes. Only events that start by
/// themselves ("on_enter", "auto") are compiled, because they need no entity or collision line. Supported
/// steps: <c>say</c> (WINDOW + MESSAGE), <c>wait</c> (WAIT), numeric <c>battle</c> (BATTLE) and numeric
/// <c>give_item</c> (STITM). Everything else is skipped and reported, never guessed.
/// </summary>
public static class SceneScriptCompiler
{
    private const byte OpRet = 0x00;
    private const byte OpWait = 0x24;
    private const byte OpMessage = 0x40;
    private const byte OpWindow = 0x50;
    private const byte OpStitm = 0x58;
    private const byte OpBattle = 0x70;

    private const int FieldFramesPerSecond = 30;
    private const byte DialogueWindowId = 0;

    /// <param name="plan">The plan whose events are compiled.</param>
    /// <param name="textIdOf">Maps a "say" step to the text id it was stored under; null means "not stored".</param>
    public static CompiledScene Compile(ScenePlan plan, Func<EventStep, int?> textIdOf)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(textIdOf);

        var skipped = new List<string>();
        using var body = new MemoryStream();
        var written = 0;

        foreach (var ev in plan.Events)
        {
            if (!IsAutomatic(ev.Trigger))
            {
                skipped.Add($"event '{ev.Id}' ({ev.Trigger}): needs a model or collision line to trigger");
                continue;
            }

            for (var i = 0; i < ev.Steps.Count; i++)
                written += CompileStep(ev.Steps[i], $"event '{ev.Id}' step {i}", body, textIdOf, skipped);
        }

        if (written == 0)
            return new CompiledScene { Skipped = skipped };

        var script = new byte[body.Length + 2];
        script[0] = OpRet; // empty Init; the main sequence follows
        body.ToArray().CopyTo(script, 1);
        script[^1] = OpRet;
        return new CompiledScene { Script = script, StepsWritten = written, Skipped = skipped };
    }

    /// <summary>True for triggers that run on their own when the field loads.</summary>
    public static bool IsAutomatic(string trigger) =>
        trigger.Equals("on_enter", StringComparison.OrdinalIgnoreCase) ||
        trigger.Equals("auto", StringComparison.OrdinalIgnoreCase);

    private static int CompileStep(
        EventStep step, string where, MemoryStream output, Func<EventStep, int?> textIdOf, List<string> skipped)
    {
        switch (step.Type)
        {
            case EventStepType.Say:
            {
                if (textIdOf(step) is not { } textId || textId is < 0 or > byte.MaxValue)
                {
                    skipped.Add($"{where} (say): text could not be stored");
                    return 0;
                }

                var (width, height) = Script.EstimateTextWindowSize(step.Text);
                var (x, y) = Script.ClampWindowPosition(
                    (short)((320 - width) / 2), (short)(216 - height), width, height);
                output.WriteByte(OpWindow);
                output.WriteByte(DialogueWindowId);
                WriteInt16(output, x);
                WriteInt16(output, y);
                WriteUInt16(output, width);
                WriteUInt16(output, height);
                output.WriteByte(OpMessage);
                output.WriteByte(DialogueWindowId);
                output.WriteByte((byte)textId);
                return 1;
            }

            case EventStepType.Wait:
            {
                var frames = Math.Clamp((int)Math.Ceiling(step.Ms * FieldFramesPerSecond / 1000.0), 1, ushort.MaxValue);
                output.WriteByte(OpWait);
                WriteUInt16(output, (ushort)frames);
                return 1;
            }

            case EventStepType.Battle:
                if (!TryParseId(step.EncounterId, ushort.MaxValue, out var battleId))
                {
                    skipped.Add($"{where} (battle): '{step.EncounterId}' is not a numeric battle id");
                    return 0;
                }

                output.WriteByte(OpBattle);
                output.WriteByte(0); // both operands are literals
                WriteUInt16(output, (ushort)battleId);
                return 1;

            case EventStepType.GiveItem:
                if (!TryParseId(step.ItemId, 319, out var itemId) || step.Qty is < 1 or > byte.MaxValue)
                {
                    skipped.Add($"{where} (give_item): needs a numeric item id (0-319) and a quantity of 1-255");
                    return 0;
                }

                output.WriteByte(OpStitm);
                output.WriteByte(0); // both operands are literals
                WriteUInt16(output, (ushort)itemId);
                output.WriteByte((byte)step.Qty);
                return 1;

            case EventStepType.CustomNote:
                return 0; // a note for humans, nothing to run

            case EventStepType.Move:
            case EventStepType.Face:
                skipped.Add($"{where} ({step.Type.ToString().ToLowerInvariant()}): needs a character model entity");
                return 0;

            case EventStepType.PlayMusic:
                skipped.Add($"{where} (play_music): music needs AKAO commands, not generated");
                return 0;

            case EventStepType.SetFlag:
            case EventStepType.IfFlag:
                skipped.Add($"{where} ({(step.Type == EventStepType.SetFlag ? "set_flag" : "if_flag")}): flags need variable bank/address mapping");
                return 0;

            default:
                skipped.Add($"{where}: unsupported step");
                return 0;
        }
    }

    private static bool TryParseId(string text, int max, out int value) =>
        int.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out value) && value <= max;

    private static void WriteUInt16(Stream stream, ushort value)
    {
        stream.WriteByte((byte)(value & 0xFF));
        stream.WriteByte((byte)(value >> 8));
    }

    private static void WriteInt16(Stream stream, short value) => WriteUInt16(stream, (ushort)value);
}
