using Dfo.Server.Abstractions.Content;

namespace Dfo.Server.Infrastructure.Content;

internal static class PvfDungeonConnections
{
    internal static IReadOnlyList<DungeonPathDirection> Decode(string text, string path)
    {
        var letters = text.Where(value => !char.IsWhiteSpace(value)).ToArray();
        if (letters.Length % 2 != 0)
            throw new InvalidDataException($"PVF '{path}', field '[greed]': incomplete connection token.");
        var result = new DungeonPathDirection[letters.Length / 2];
        for (var index = 0; index < result.Length; index++)
        {
            var letter = letters[index * 2];
            if (letter is < 'A' or > 'P' || letters[index * 2 + 1] != letter)
                throw new InvalidDataException($"PVF '{path}', field '[greed]': invalid connection token.");
            result[index] = (DungeonPathDirection)(letter - 'A');
        }
        return Array.AsReadOnly(result);
    }
}
