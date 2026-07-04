using System;
using System.Collections.Generic;

namespace MakouReactor.Core.Models;

// ──────────────────────────────────────────────
// Field (from src/core/field/Field.h)
// ──────────────────────────────────────────────

[Flags]
public enum FieldSection
{
    Scripts = 1 << 0,
    Akaos = 1 << 1,
    Camera = 1 << 2,
    Walkmesh = 1 << 3,
    ModelLoader = 1 << 4,
    Encounter = 1 << 5,
    Inf = 1 << 6,
    Background = 1 << 7,
    PalettePC = 1 << 8,
    Tiles = 1 << 9,
}

public abstract class Field
{
    public string Name { get; protected set; } = string.Empty;
    public string OldName { get; protected set; } = string.Empty;
    public bool IsOpen { get; protected set; }
    public bool IsModified { get; protected set; }
    public string ErrorString { get; protected set; } = string.Empty;

    // Section data holders
    public Section1File? ScriptsAndTexts { get; protected set; }
    public FieldModelLoaderPC? ModelLoader { get; protected set; }
    public IdFile? Walkmesh { get; protected set; }
    public EncounterFile? Encounters { get; protected set; }
    public BackgroundFilePC? Background { get; protected set; }
    public InfFile? Inf { get; protected set; }

    // Dictionary of section type to data
    private readonly Dictionary<FieldSection, object> _parts = new();

    protected Field(string name)
    {
        Name = name;
        OldName = name;
    }

    public virtual void Open(bool dontOptimize = false) { IsOpen = true; }
    public virtual void InitEmpty() { IsOpen = true; }
    public virtual void SetModified() { IsModified = true; }
    public virtual void SetSaved() { IsModified = false; }
    public virtual void SetName(string name) { Name = name; }
    public virtual bool IsPC() => false;

    public override string ToString() => Name;
}
