namespace MakouReactor.UI.WPF.Scripts;

public static class ScriptOpcodeTreeBuilder
{
    public static ScriptTreeItem[] Build<T>(
        string scriptName,
        IReadOnlyList<T> opcodes,
        Func<T, string> offsetSelector,
        Func<T, string> nameSelector,
        Func<T, string> argumentsSelector)
    {
        ArgumentNullException.ThrowIfNull(opcodes);
        ArgumentNullException.ThrowIfNull(offsetSelector);
        ArgumentNullException.ThrowIfNull(nameSelector);
        ArgumentNullException.ThrowIfNull(argumentsSelector);

        var children = opcodes.Select(opcode =>
        {
            var name = nameSelector(opcode);
            var prefix = Prefix(name);
            var arguments = argumentsSelector(opcode);
            var argumentText = string.IsNullOrWhiteSpace(arguments)
                ? string.Empty
                : $" - {arguments}";
            return new ScriptTreeItem(
                $"{prefix}{offsetSelector(opcode)}: {name}{argumentText}",
                opcode);
        });

        return [new ScriptTreeItem($"{scriptName} ({opcodes.Count} opcode(s))", children: children)];
    }

    public static string Prefix(string opcodeName)
    {
        if (opcodeName.StartsWith("IF", StringComparison.OrdinalIgnoreCase))
            return "Condition - ";

        if (opcodeName.StartsWith("JMP", StringComparison.OrdinalIgnoreCase) ||
            opcodeName.Equals("MAPJUMP", StringComparison.OrdinalIgnoreCase) ||
            opcodeName.StartsWith("REQ", StringComparison.OrdinalIgnoreCase))
        {
            return "Branch - ";
        }

        if (opcodeName.Equals("RET", StringComparison.OrdinalIgnoreCase) ||
            opcodeName.Equals("RETTO", StringComparison.OrdinalIgnoreCase))
        {
            return "Return - ";
        }

        return "Opcode - ";
    }
}
