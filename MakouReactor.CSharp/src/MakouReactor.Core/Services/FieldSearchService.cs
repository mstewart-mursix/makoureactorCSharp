using MakouReactor.Core.Models;

namespace MakouReactor.Core.Services;

public sealed record FieldSearchResult(
    string Kind,
    string FieldName,
    int? GroupIndex,
    string? GroupName,
    int? ScriptIndex,
    int? OpcodeOffset,
    int? TextIndex,
    string Match,
    string Location);

public sealed class FieldSearchService
{
    public IReadOnlyList<FieldSearchResult> SearchCurrentField(
        string fieldName,
        Section1File section,
        string query)
    {
        ArgumentNullException.ThrowIfNull(section);

        if (string.IsNullOrWhiteSpace(query))
            return [];

        var normalizedQuery = query.Trim();
        var results = new List<FieldSearchResult>();
        SearchTexts(fieldName, section, normalizedQuery, results);
        SearchGroupsAndScripts(fieldName, section, normalizedQuery, results);
        SearchOpcodes(fieldName, section, normalizedQuery, results);
        SearchVariables(fieldName, section, normalizedQuery, results);
        return results;
    }

    private static void SearchTexts(
        string fieldName,
        Section1File section,
        string query,
        List<FieldSearchResult> results)
    {
        for (var index = 0; index < section.Texts.Count; index++)
        {
            var value = section.Texts[index].Value;
            if (!Contains(value, query))
                continue;

            results.Add(new FieldSearchResult(
                "Text",
                fieldName,
                null,
                null,
                null,
                null,
                index,
                value,
                $"Text {index}"));
        }
    }

    private static void SearchGroupsAndScripts(
        string fieldName,
        Section1File section,
        string query,
        List<FieldSearchResult> results)
    {
        for (var groupIndex = 0; groupIndex < section.GrpScripts.Count; groupIndex++)
        {
            var group = section.GrpScripts[groupIndex];
            if (Contains(group.Name, query))
            {
                results.Add(new FieldSearchResult(
                    "Group",
                    fieldName,
                    groupIndex,
                    group.Name,
                    null,
                    null,
                    null,
                    group.Name,
                    $"Group {groupIndex}: {group.Name}"));
            }

            for (var scriptIndex = 0; scriptIndex < group.Scripts.Count; scriptIndex++)
            {
                var scriptName = ScriptName(scriptIndex);
                if (!Contains(scriptName, query) && !scriptIndex.ToString().Equals(query, StringComparison.OrdinalIgnoreCase))
                    continue;

                results.Add(new FieldSearchResult(
                    "Script",
                    fieldName,
                    groupIndex,
                    group.Name,
                    scriptIndex,
                    null,
                    null,
                    scriptName,
                    $"Group {groupIndex}: {group.Name} / {scriptName}"));
            }
        }
    }

    private static void SearchOpcodes(
        string fieldName,
        Section1File section,
        string query,
        List<FieldSearchResult> results)
    {
        var queryAsByte = TryParseOpcodeId(query);

        for (var groupIndex = 0; groupIndex < section.GrpScripts.Count; groupIndex++)
        {
            var group = section.GrpScripts[groupIndex];
            for (var scriptIndex = 0; scriptIndex < group.Scripts.Count; scriptIndex++)
            {
                var script = group.Scripts[scriptIndex];
                foreach (var opcode in script.RawOpcodes)
                {
                    if (!Contains(opcode.Name, query) && queryAsByte != opcode.Id)
                        continue;

                    results.Add(new FieldSearchResult(
                        "Opcode",
                        fieldName,
                        groupIndex,
                        group.Name,
                        scriptIndex,
                        opcode.Offset,
                        null,
                        $"{opcode.Name} {opcode.RawBytesHex}",
                        $"Group {groupIndex}: {group.Name} / {ScriptName(scriptIndex)} / {opcode.OffsetHex}"));
                }
            }
        }
    }

    private static void SearchVariables(
        string fieldName,
        Section1File section,
        string query,
        List<FieldSearchResult> results)
    {
        foreach (var reference in new VariableReferenceScanner().Scan(section))
        {
            var match = BuildVariableMatch(reference);
            if (!Contains(match, query) &&
                !Contains(reference.Location, query) &&
                !Contains(reference.OpcodeName, query))
            {
                continue;
            }

            results.Add(new FieldSearchResult(
                "Variable",
                fieldName,
                reference.GroupIndex,
                reference.GroupName,
                reference.ScriptIndex,
                reference.OpcodeOffset,
                null,
                match,
                reference.Location));
        }
    }

    private static bool Contains(string value, string query) =>
        value.Contains(query, StringComparison.OrdinalIgnoreCase);

    private static byte? TryParseOpcodeId(string query)
    {
        if (byte.TryParse(query, out var decimalValue))
            return decimalValue;

        if (query.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            byte.TryParse(query[2..], System.Globalization.NumberStyles.HexNumber, null, out var hexValue))
            return hexValue;

        return null;
    }

    private static string BuildVariableMatch(VariableReference reference) =>
        $"{reference.OpcodeName} bank {reference.BankHex} address {reference.AddressHex} " +
        $"{reference.Size} {(reference.Writable ? "write" : "read")}";

    private static string ScriptName(int index) => index switch
    {
        0 => "S0 - Init",
        1 => "S0 - Main",
        2 => "S1 - Talk",
        3 => "S2 - Contact",
        _ => $"Script {index - 1}",
    };
}
