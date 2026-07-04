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

    public void SetScript(int scriptId, Script script)
    {
        if (scriptId >= 0 && scriptId < Scripts.Count)
            Scripts[scriptId] = script;
    }

    public string TypeString => Type switch
    {
        GrpScriptType.Model => "Model",
        GrpScriptType.Location => "Location",
        GrpScriptType.Animation => "Animation",
        GrpScriptType.Director => "Director",
        _ => "Unknown",
    };

    public byte[] ToByteArray(int scriptId) => Script(scriptId)?.ToByteArray() ?? [];

    public override string ToString() => $"[{TypeString}] {Name} ({Scripts.Count} scripts)";
}
