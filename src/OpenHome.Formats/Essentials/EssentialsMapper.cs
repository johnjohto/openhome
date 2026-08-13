using PKHeX.Core;

namespace OpenHome.Formats.Essentials;

/// <summary>
/// Converts between an Essentials <c>Pokemon</c> object (a <see cref="RubyObject"/> ivar bag,
/// Essentials v21 layout) and a <see cref="PK8"/> entity. PK8 is the neutral in-memory
/// representation: it covers every species and move Essentials v21 can contain, needs no
/// per-slot encryption, and feeds straight into the vault's PKH deposit path.
/// </summary>
public static class EssentialsMapper
{
    // Essentials GameData::Stat ids used as @iv/@ev hash keys.
    private static readonly string[] StatKeys = ["HP", "ATTACK", "DEFENSE", "SPEED", "SPECIALATTACK", "SPECIALDEFENSE"];

    /// <summary>True when the value is a marshaled Essentials Pokemon object.</summary>
    public static bool IsPokemon(RubyValue? value) =>
        value is RubyObject { ClassName: "Pokemon" };

    /// <summary>Builds a PK8 from a marshaled Essentials Pokemon. Unresolvable symbols degrade to 0/defaults.</summary>
    public static PK8 ToPK8(RubyObject pokemon)
    {
        var pk = new PK8();

        var species = NameResolver.ResolveSpecies(pokemon.GetSymbol("@species")?.Name);
        pk.Species = species;
        pk.Form = (byte)(pokemon.GetInt("@form") ?? 0);

        var owner = pokemon.GetObject("@owner");
        var otName = owner?.GetString("@name") ?? "";
        var otId = (uint)(owner?.GetInt("@id") ?? 0);
        pk.TID16 = (ushort)(otId & 0xFFFF);
        pk.SID16 = (ushort)(otId >> 16);
        pk.OriginalTrainerName = otName;
        pk.OriginalTrainerGender = (byte)(owner?.GetInt("@gender") ?? 0);
        pk.Language = (int)(owner?.GetInt("@language") ?? (int)LanguageID.English);

        var nickname = pokemon.GetString("@name");
        var speciesName = species < GameInfo.Strings.specieslist.Length
            ? GameInfo.Strings.specieslist[species]
            : "";
        pk.Nickname = nickname ?? speciesName;
        pk.IsNicknamed = nickname is not null && nickname != speciesName;

        var pid = (uint)(pokemon.GetInt("@personalID") ?? 0);
        pk.PID = ForceShinyState(pid, pk.TID16, pk.SID16, pokemon.Get("@shiny") as RubyBool);

        var level = pokemon.GetInt("@level");
        if (level is > 0 and <= 100)
            pk.CurrentLevel = (byte)level.Value;
        if (pokemon.GetInt("@exp") is { } exp && exp >= 0)
            pk.EXP = (uint)Math.Min(exp, uint.MaxValue);

        pk.Ball = (byte)NameResolver.ResolveBall(pokemon.GetSymbol("@poke_ball")?.Name);
        pk.HeldItem = NameResolver.ResolveItem(pokemon.GetSymbol("@item")?.Name);
        pk.Nature = (Nature)NameResolver.ResolveNature(pokemon.GetSymbol("@nature")?.Name);

        if (NameResolver.ResolveAbility(pokemon.GetSymbol("@ability")?.Name) is > 0 and var ability)
            pk.Ability = ability;
        pk.AbilityNumber = 1 << (int)(pokemon.GetInt("@ability_index") ?? 0);

        // Essentials stores @gender as true=male, false=female, nil=derived at runtime.
        pk.Gender = pokemon.Get("@gender") switch
        {
            RubyBool { Value: true } => 0,
            RubyBool { Value: false } => 1,
            _ => 2,
        };

        if (pokemon.Get("@iv") is RubyHash ivs)
        {
            pk.IV_HP = ReadStat(ivs, StatKeys[0], 31);
            pk.IV_ATK = ReadStat(ivs, StatKeys[1], 31);
            pk.IV_DEF = ReadStat(ivs, StatKeys[2], 31);
            pk.IV_SPE = ReadStat(ivs, StatKeys[3], 31);
            pk.IV_SPA = ReadStat(ivs, StatKeys[4], 31);
            pk.IV_SPD = ReadStat(ivs, StatKeys[5], 31);
        }
        if (pokemon.Get("@ev") is RubyHash evs)
        {
            pk.EV_HP = ReadStat(evs, StatKeys[0], 0);
            pk.EV_ATK = ReadStat(evs, StatKeys[1], 0);
            pk.EV_DEF = ReadStat(evs, StatKeys[2], 0);
            pk.EV_SPE = ReadStat(evs, StatKeys[3], 0);
            pk.EV_SPA = ReadStat(evs, StatKeys[4], 0);
            pk.EV_SPD = ReadStat(evs, StatKeys[5], 0);
        }

        if (pokemon.GetArray("@moves") is { } moves)
        {
            var ids = moves.Items
                .OfType<RubyObject>()
                .Where(m => m.ClassName is "Pokemon::Move" or "PBMove")
                .Select(m => NameResolver.ResolveMove(m.GetSymbol("@id")?.Name))
                .Where(id => id != 0)
                .ToArray();
            if (ids.Length > 0) pk.Move1 = ids[0];
            if (ids.Length > 1) pk.Move2 = ids[1];
            if (ids.Length > 2) pk.Move3 = ids[2];
            if (ids.Length > 3) pk.Move4 = ids[3];
        }
        pk.FixMoves();

        pk.MetLevel = (byte)Math.Clamp(pokemon.GetInt("@obtain_level") ?? 0, 0, 100);
        pk.RefreshChecksum();
        pk.ResetPartyStats();
        return pk;
    }

    /// <summary>Rebuilds a marshaled Essentials Pokemon from a PK8 (used when a slot changed or was filled).</summary>
    public static RubyObject FromPK8(PK8 pk)
    {
        var pokemon = new RubyObject("Pokemon");
        pokemon.Ivars["@species"] = new RubySymbol(NameResolver.SpeciesSymbol(pk.Species));
        pokemon.Ivars["@form"] = new RubyInt(pk.Form);
        pokemon.Ivars["@forced_form"] = RubyNil.Instance;
        pokemon.Ivars["@time_form_set"] = RubyNil.Instance;
        pokemon.Ivars["@level"] = new RubyInt(pk.CurrentLevel);
        pokemon.Ivars["@exp"] = new RubyInt(pk.EXP);
        pokemon.Ivars["@steps_to_hatch"] = new RubyInt(0);
        pokemon.Ivars["@status"] = new RubyInt(0);
        pokemon.Ivars["@statusCount"] = new RubyInt(0);
        pokemon.Ivars["@gender"] = pk.Gender switch { 0 => RubyBool.True, 1 => RubyBool.False, _ => RubyNil.Instance };
        pokemon.Ivars["@shiny"] = RubyBool.FromBool(pk.IsShiny);
        pokemon.Ivars["@ability"] = new RubySymbol(pk.Ability.ToString()); // fangames vary; numeric fallback is harmless on load failure
        pokemon.Ivars["@ability_index"] = new RubyInt(pk.AbilityNumber is 4 ? 2 : pk.AbilityNumber >> 1);
        pokemon.Ivars["@nature"] = new RubySymbol(NameResolver.NatureSymbol((int)pk.Nature));
        pokemon.Ivars["@nature_for_stats"] = RubyNil.Instance;
        pokemon.Ivars["@item"] = pk.HeldItem > 0 ? new RubySymbol(NameResolver.ItemSymbol(pk.HeldItem)) : RubyNil.Instance;
        pokemon.Ivars["@mail"] = RubyNil.Instance;

        var moves = new RubyArray([]);
        foreach (var move in new[] { pk.Move1, pk.Move2, pk.Move3, pk.Move4 }.Where(m => m != 0))
        {
            var mv = new RubyObject("Pokemon::Move");
            mv.Ivars["@id"] = new RubySymbol(NameResolver.MoveSymbol(move));
            mv.Ivars["@ppup"] = new RubyInt(0);
            mv.Ivars["@pp"] = new RubyInt(1);
            moves.Items.Add(mv);
        }
        pokemon.Ivars["@moves"] = moves;
        pokemon.Ivars["@first_moves"] = new RubyArray([]);

        pokemon.Ivars["@iv"] = StatHash([pk.IV_HP, pk.IV_ATK, pk.IV_DEF, pk.IV_SPE, pk.IV_SPA, pk.IV_SPD]);
        pokemon.Ivars["@ivMaxed"] = new RubyHash([]);
        pokemon.Ivars["@ev"] = StatHash([pk.EV_HP, pk.EV_ATK, pk.EV_DEF, pk.EV_SPE, pk.EV_SPA, pk.EV_SPD]);

        pokemon.Ivars["@name"] = pk.IsNicknamed ? RubyString.FromText(pk.Nickname) : RubyNil.Instance;
        pokemon.Ivars["@happiness"] = new RubyInt(70);
        pokemon.Ivars["@poke_ball"] = new RubySymbol(NameResolver.BallSymbol((int)pk.Ball));
        pokemon.Ivars["@markings"] = new RubyArray([]);
        pokemon.Ivars["@ribbons"] = new RubyArray([]);

        var owner = new RubyObject("Pokemon::Owner");
        owner.Ivars["@id"] = new RubyInt(pk.ID32);
        owner.Ivars["@name"] = RubyString.FromText(pk.OriginalTrainerName);
        owner.Ivars["@gender"] = new RubyInt(pk.OriginalTrainerGender);
        owner.Ivars["@language"] = new RubyInt(pk.Language);
        pokemon.Ivars["@owner"] = owner;

        pokemon.Ivars["@obtain_method"] = new RubyInt(0);
        pokemon.Ivars["@obtain_map"] = new RubyInt(0);
        pokemon.Ivars["@obtain_text"] = RubyNil.Instance;
        pokemon.Ivars["@obtain_level"] = new RubyInt(pk.CurrentLevel);
        pokemon.Ivars["@hatched_map"] = new RubyInt(0);
        pokemon.Ivars["@timeReceived"] = new RubyInt(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        pokemon.Ivars["@timeEggHatched"] = RubyNil.Instance;
        pokemon.Ivars["@fused"] = RubyNil.Instance;
        pokemon.Ivars["@personalID"] = new RubyInt(pk.PID);
        pokemon.Ivars["@hp"] = new RubyInt(pk.Stat_HPCurrent);
        pokemon.Ivars["@totalhp"] = new RubyInt(pk.Stat_HPMax);
        return pokemon;
    }

    private static RubyHash StatHash(int[] values)
    {
        var hash = new RubyHash([]);
        for (var i = 0; i < StatKeys.Length; i++)
            hash.Entries.Add(new KeyValuePair<RubyValue, RubyValue>(new RubySymbol(StatKeys[i]), new RubyInt(values[i])));
        return hash;
    }

    private static int ReadStat(RubyHash hash, string key, int fallback) =>
        hash[key] is RubyInt i ? (int)Math.Clamp(i.Value, 0, 252) : fallback;

    /// <summary>
    /// Adjusts the PID so the gen-6+ shiny rule (TID ^ SID ^ PID.hi ^ PID.lo &lt; 16)
    /// matches the Essentials flag: xor 0 for shiny, 16 for not shiny.
    /// </summary>
    private static uint ForceShinyState(uint pid, ushort tid, ushort sid, RubyBool? shiny)
    {
        if (shiny is null)
            return pid;
        var lo = pid & 0xFFFF;
        var hi = shiny.Value
            ? lo ^ tid ^ sid        // xor == 0  -> shiny
            : lo ^ tid ^ sid ^ 16u; // xor == 16 -> not shiny
        return (hi << 16) | lo;
    }
}
