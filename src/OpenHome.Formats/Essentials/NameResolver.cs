using System.Globalization;
using System.Text;
using PKHeX.Core;

namespace OpenHome.Formats.Essentials;

/// <summary>
/// Maps Essentials internal constants (symbols like <c>:THUNDERSHOCK</c>) to the numeric
/// IDs PKHeX uses. Essentials constants are the English display name in upper case with no
/// spaces or punctuation, so both sides are normalized the same way and matched by name.
/// A tiny override table covers the names that do not survive normalization (the Nidorans).
/// </summary>
public static class NameResolver
{
    private static readonly IReadOnlyDictionary<string, ushort> SpeciesOverrides = new Dictionary<string, ushort>
    {
        ["NIDORANFEMALE"] = 29,
        ["NIDORANMALE"] = 32,
    };

    private static readonly IReadOnlyDictionary<ushort, string> SpeciesReverseOverrides = new Dictionary<ushort, string>
    {
        [29] = "NIDORAN_FEMALE",
        [32] = "NIDORAN_MALE",
    };

    private static readonly Lazy<IReadOnlyDictionary<string, int>> Species = new(() => Build(GameInfo.Strings.specieslist));
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Moves = new(() => Build(GameInfo.Strings.movelist));
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Items = new(() => Build(GameInfo.Strings.itemlist));
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Balls = new(() => Build(GameInfo.Strings.balllist));
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Natures = new(() => Build(GameInfo.Strings.Natures));
    private static readonly Lazy<IReadOnlyDictionary<string, int>> Abilities = new(() => Build(GameInfo.Strings.abilitylist));

    /// <summary>Removes diacritics and every non-alphanumeric character, then upper-cases.</summary>
    public static string Normalize(string name)
    {
        var decomposed = name.Normalize(NormalizationForm.FormD);
        var sb = new StringBuilder(decomposed.Length);
        foreach (var c in decomposed)
        {
            if (CharUnicodeInfo.GetUnicodeCategory(c) == UnicodeCategory.NonSpacingMark)
                continue;
            if (char.IsLetterOrDigit(c))
                sb.Append(char.ToUpperInvariant(c));
        }
        return sb.ToString();
    }

    private static IReadOnlyDictionary<string, int> Build(IReadOnlyList<string> names)
    {
        var map = new Dictionary<string, int>(names.Count);
        for (var i = 0; i < names.Count; i++)
        {
            var key = Normalize(names[i]);
            if (key.Length != 0)
                map.TryAdd(key, i); // first occurrence wins (later entries are duplicates/forms)
        }
        return map;
    }

    private static int Resolve(IReadOnlyDictionary<string, int> map, string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return 0;
        return map.TryGetValue(Normalize(symbol), out var id) ? id : 0;
    }

    /// <summary>Resolves a species symbol (e.g. <c>PIKACHU</c>) to a national dex number; 0 when unknown.</summary>
    public static ushort ResolveSpecies(string? symbol)
    {
        if (string.IsNullOrEmpty(symbol))
            return 0;
        var key = Normalize(symbol);
        if (SpeciesOverrides.TryGetValue(key, out var overridden))
            return overridden;
        return Species.Value.TryGetValue(key, out var id) ? (ushort)id : (ushort)0;
    }

    public static ushort ResolveMove(string? symbol) => (ushort)Resolve(Moves.Value, symbol);
    public static int ResolveItem(string? symbol) => Resolve(Items.Value, symbol);
    public static int ResolveBall(string? symbol) => Resolve(Balls.Value, symbol) is var id && id > 0 ? id : 4; // default: Poké Ball
    public static int ResolveNature(string? symbol) => Resolve(Natures.Value, symbol);
    public static int ResolveAbility(string? symbol) => Resolve(Abilities.Value, symbol);

    /// <summary>Turns a national dex number back into an Essentials species constant.</summary>
    public static string SpeciesSymbol(ushort species)
    {
        if (SpeciesReverseOverrides.TryGetValue(species, out var overridden))
            return overridden;
        var list = GameInfo.Strings.specieslist;
        return species < list.Length ? Normalize(list[species]) : $"SPECIES_{species}";
    }

    public static string MoveSymbol(ushort move) =>
        move < GameInfo.Strings.movelist.Length ? Normalize(GameInfo.Strings.movelist[move]) : $"MOVE_{move}";

    public static string ItemSymbol(int item) =>
        item < GameInfo.Strings.itemlist.Length ? Normalize(GameInfo.Strings.itemlist[item]) : $"ITEM_{item}";

    public static string BallSymbol(int ball) =>
        ball < GameInfo.Strings.balllist.Length ? Normalize(GameInfo.Strings.balllist[ball]) : "POKEBALL";

    public static string NatureSymbol(int nature) =>
        nature < GameInfo.Strings.Natures.Count ? Normalize(GameInfo.Strings.Natures[nature]) : "HARDY";
}
