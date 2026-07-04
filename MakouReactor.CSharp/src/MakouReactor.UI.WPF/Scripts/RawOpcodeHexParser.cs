using System.Globalization;

namespace MakouReactor.UI.WPF.Scripts;

public static class RawOpcodeHexParser
{
    public static bool TryParse(string text, int expectedLength, out byte[] bytes, out string error)
    {
        bytes = [];
        error = string.Empty;

        if (expectedLength <= 0)
        {
            error = "Opcode size must be positive.";
            return false;
        }

        var parts = text
            .Split([' ', '\t', '\r', '\n', ',', ';'], StringSplitOptions.RemoveEmptyEntries)
            .ToArray();
        if (parts.Length != expectedLength)
        {
            error = $"Expected {expectedLength} byte(s), got {parts.Length}.";
            return false;
        }

        var parsed = new byte[parts.Length];
        for (var index = 0; index < parts.Length; index++)
        {
            if (parts[index].Length > 2 ||
                !byte.TryParse(parts[index], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out parsed[index]))
            {
                error = $"'{parts[index]}' is not a valid hex byte.";
                return false;
            }
        }

        bytes = parsed;
        return true;
    }
}
