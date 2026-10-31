using System.Collections.Generic;

namespace MakouReactor.Core.Models;

// ──────────────────────────────────────────────
// GrpScript (from src/core/field/GrpScript.h)
// ──────────────────────────────────────────────

public enum GrpScriptType
{
    NoType = 0,
    Model,
    Location,
    Animation,
    Director,
}

public sealed class GrpScript
{
    public string Name { get; set; } = string.Empty;
    public List<Script> Scripts { get; } = [];
    public GrpScriptType Type { get; set; } = GrpScriptType.NoType;
    public short Character { get; set; }

    public const int MaxScriptsPerGroup = 33;

    public GrpScript()
    {
        for (var i = 0; i < MaxScriptsPerGroup; i++)
            Scripts.Add(new Script());
    }

    public GrpScript(string name) : this()
    {
        Name = name;
    }

    public Script? Script(int scriptId) =>
        scriptId >= 0 && scriptId < Scripts.Count ? Scripts[scriptId] : null;

    /// <summary>
    /// Replaces a script slot. Slot 0 is "S0 - Init", slot 1 "S0 - Main", and slot N + 1 holds
    /// file script N, as in <c>GrpScript</c> in <c>src/core/field/GrpScript.cpp</c>. Replacing
    /// slot 0 re-runs type detection.
    /// </summary>
    /// <param name="scriptId">The slot index (0-32).</param>
    /// <param name="script">The script to store.</param>
    public void SetScript(int scriptId, Script script)
    {
        if (scriptId >= 0 && scriptId < Scripts.Count)
        {
            Scripts[scriptId] = script;
            if (scriptId == 0)
                DetectType();
        }
    }

    /// <summary>
    /// Detects the group type and character from the init script, mirroring
    /// <c>GrpScript::detectType()</c>.
    /// </summary>
    public void DetectType()
    {
        Character = -1;
        Type = GrpScriptType.NoType;

        foreach (var opcode in Scripts[0].RawOpcodes)
        {
            switch (opcode.Name)
            {
                case "PC":
                    Character = opcode.Bytes.Length > 1 ? opcode.Bytes[1] : (short)0;
                    Type = GrpScriptType.Model;
                    return;
                case "CHAR":
                    Character = 0x100;
                    Type = GrpScriptType.Model;
                    break;
                case "LINE":
                    if (Type == GrpScriptType.NoType)
                        Type = GrpScriptType.Location;
                    return;
                case "BGPDH" or "BGSCR" or "BGON" or "BGOFF" or "BGROL" or "BGROL2" or "BGCLR":
                    if (Type == GrpScriptType.NoType)
                        Type = GrpScriptType.Animation;
                    return;
                case "MPNAM":
                    if (Type == GrpScriptType.NoType)
                        Type = GrpScriptType.Director;
                    return;
            }
        }
    }

    public string TypeString => Type switch
    {
        GrpScriptType.Model => "Model",
        GrpScriptType.Location => "Location",
        GrpScriptType.Animation => "Animation",
        GrpScriptType.Director => "Director",
        _ => "Unknown",
    };

    /// <summary>
    /// Serializes file script <paramref name="fileScriptId"/> (0-31) as
    /// <c>GrpScript::toByteArray</c> does: file script 0 is the init slot followed by the main slot
    /// (empty when the init slot is empty), and file script N is slot N + 1.
    /// </summary>
    /// <param name="fileScriptId">The file script index (0-31).</param>
    /// <returns>The script bytes, or an empty array.</returns>
    public byte[] ToByteArray(int fileScriptId)
    {
        if (fileScriptId == 0)
        {
            return Scripts[0].IsEmpty
                ? []
                : [.. Scripts[0].ToByteArray(), .. Scripts[1].ToByteArray()];
        }

        return fileScriptId > 0 && fileScriptId + 1 < MaxScriptsPerGroup
            ? Scripts[fileScriptId + 1].ToByteArray()
            : [];
    }

    public override string ToString() => $"[{TypeString}] {Name} ({Scripts.Count} scripts)";
}
