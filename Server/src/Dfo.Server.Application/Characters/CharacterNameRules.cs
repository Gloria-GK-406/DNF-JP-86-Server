using System.Text;
using Dfo.Server.Abstractions.Characters;

namespace Dfo.Server.Application.Characters;

/// <summary>
/// Decides whether a proposed character name is well formed, before any uniqueness question.
/// </summary>
/// <remarks>
/// Two protocols ask this and they ask it with different bounds: the name check accepts up to 30
/// bytes while creation accepts only 18. The bounds are therefore parameters, not constants -- a
/// name can legitimately pass the check and then be refused by creation, and the client is expected
/// to cope with that because the legacy server behaved the same way.
///
/// Length is measured in UTF-8 bytes, not characters, because the wire carries a byte count and the
/// client counts the same way. A three-byte CJK character costs three.
/// </remarks>
internal static class CharacterNameRules
{
    private static readonly UTF8Encoding StrictUtf8 = new(
        encoderShouldEmitUTF8Identifier: false,
        throwOnInvalidBytes: true);

    public static CharacterNameShape Inspect(byte[]? rawName, int minBytes, int maxBytes)
    {
        if (rawName is null || rawName.Length < minBytes)
        {
            return CharacterNameShape.TooShort;
        }

        if (rawName.Length > maxBytes)
        {
            return CharacterNameShape.TooLong;
        }

        string text;
        try
        {
            text = StrictUtf8.GetString(rawName);
        }
        catch (DecoderFallbackException)
        {
            return CharacterNameShape.Malformed;
        }

        // A control character would render as a gap the player cannot type or report, so a name
        // containing one is unusable even though it decodes cleanly.
        foreach (var character in text)
        {
            if (char.IsControl(character))
            {
                return CharacterNameShape.Malformed;
            }
        }

        return string.IsNullOrWhiteSpace(text)
            ? CharacterNameShape.Malformed
            : CharacterNameShape.WellFormed;
    }
}
