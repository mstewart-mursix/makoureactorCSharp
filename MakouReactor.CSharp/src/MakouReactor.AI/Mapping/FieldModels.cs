using System;
using System.Collections.Generic;
using System.Linq;

using MakouReactor.Core.Models;

namespace MakouReactor.AI.Mapping;

/// <summary>
/// What a field offers for placing characters: its model groups (in Model Loader order) and its
/// playable characters. A model group's model is its rank among the field's model groups
/// (<c>Section1File::modelID</c>), and in every shipped field the Model Loader has exactly one
/// entry per model group, in the same order.
/// </summary>
internal sealed class FieldModels
{
    /// <summary>FF7 party members by <c>PC</c> opcode character id (<c>Data::char_names</c>).</summary>
    private static readonly (string Name, byte CharId)[] PartyNames =
    [
        ("cloud", 0), ("barret", 1), ("tifa", 2), ("aeris", 3), ("aerith", 3),
        ("red", 4), ("redxiii", 4), ("red13", 4), ("nanaki", 4), ("yuffie", 5),
        ("caitsith", 6), ("cait", 6), ("vincent", 7), ("cid", 8),
    ];

    private FieldModels(IReadOnlyList<ModelGroup> groups) => Groups = groups;

    /// <summary>The field's model groups, in model id order.</summary>
    public IReadOnlyList<ModelGroup> Groups { get; }

    /// <summary>A group that loads a model, with the playable character it controls, if any.</summary>
    /// <param name="GroupIndex">Index of the group in Section 1.</param>
    /// <param name="ModelId">Rank among model groups (the <c>CHAR</c> argument and Model Loader index).</param>
    /// <param name="Name">Group name.</param>
    /// <param name="PartyCharId">The <c>PC</c> opcode character id when the group is a playable character.</param>
    public sealed record ModelGroup(int GroupIndex, int ModelId, string Name, byte? PartyCharId);

    /// <summary>Collect the model groups of <paramref name="section"/>.</summary>
    public static FieldModels Of(Section1File section)
    {
        ArgumentNullException.ThrowIfNull(section);

        var groups = new List<ModelGroup>();
        for (var index = 0; index < section.GrpScripts.Count; index++)
        {
            var group = section.GrpScripts[index];
            if (group.Type != GrpScriptType.Model)
                continue;

            groups.Add(new ModelGroup(index, groups.Count, group.Name, FindPartyCharId(group)));
        }

        return new FieldModels(groups);
    }

    /// <summary>
    /// The party member an actor id or display name refers to ("cloud", "Red XIII", "cait_sith"),
    /// or null.
    /// </summary>
    public static byte? PartyCharIdOf(Actor actor)
    {
        foreach (var candidate in new[] { actor.Id, actor.DisplayName })
        {
            var key = Normalize(candidate);
            if (key.Length == 0)
                continue;

            foreach (var (name, charId) in PartyNames)
            {
                if (key == name)
                    return charId;
            }
        }

        return null;
    }

    /// <summary>The field's group for playable character <paramref name="charId"/>, or null.</summary>
    public ModelGroup? PlayableGroup(byte charId) =>
        Groups.FirstOrDefault(g => g.PartyCharId == charId);

    /// <summary>The model group named <paramref name="name"/> (case-insensitive), or null.</summary>
    public ModelGroup? ByName(string? name) =>
        string.IsNullOrWhiteSpace(name)
            ? null
            : Groups.FirstOrDefault(g => string.Equals(g.Name, name.Trim(), StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// The model to copy when a plan names none: the first NPC model (not a playable character),
    /// else the first model.
    /// </summary>
    public ModelGroup? DefaultTemplate =>
        Groups.FirstOrDefault(static g => g.PartyCharId == null) ?? Groups.FirstOrDefault();

    private static byte? FindPartyCharId(GrpScript group)
    {
        foreach (var script in group.Scripts)
        {
            foreach (var opcode in script.RawOpcodes)
            {
                if (opcode.Name == "PC" && opcode.Bytes.Length >= 2)
                    return opcode.Bytes[1];
            }
        }

        return null;
    }

    private static string Normalize(string? value) =>
        new string((value ?? string.Empty).Where(char.IsLetterOrDigit).Select(char.ToLowerInvariant).ToArray());
}
