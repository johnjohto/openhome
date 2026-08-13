using PKHeX.Core;

namespace OpenHome.Formats.Essentials;

/// <summary>
/// PKHeX <see cref="ISaveReader"/> for Pokémon Essentials Game.rxdata files.
/// Recognizes the Marshal 4.8 magic and a v21-style top-level Hash containing a
/// player with a party and a PokemonStorage; anything else defers to the built-in readers.
/// </summary>
public sealed class EssentialsSaveReader : ISaveReader
{
    public bool IsRecognized(long size) => size is >= 16 and <= 64 * 1024 * 1024;

    public bool TryRead(Memory<byte> data, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SaveFile? sav, string? path)
    {
        sav = null;
        if (data.Length < 4 || data.Span[0] != 0x04 || data.Span[1] != 0x08)
            return false;
        try
        {
            var parsed = RubyMarshalReader.Read(data.Span);
            if (!LooksLikeEssentialsSave(parsed))
                return false;
            sav = new EssentialsSaveFile(data);
            return true;
        }
        catch (Exception)
        {
            return false; // not Marshal, or not a subset we understand
        }
    }

    internal static bool LooksLikeEssentialsSave(RubyValue parsed)
    {
        if (parsed is not RubyHash root)
            return false;
        var hasParty = root["player"] is RubyObject p && p.GetArray("@party") is not null;
        var hasStorage = root["storage_system"] is RubyObject { ClassName: "PokemonStorage" } s
                         && s.GetArray("@boxes") is { };
        return hasParty || hasStorage;
    }
}
