using OpenHome.Formats.Essentials;

namespace OpenHome.Formats.Tests;

/// <summary>
/// Builds a synthetic Game.rxdata entirely in code (RubyMarshalWriter) in the Essentials v21
/// shape: a top-level Hash with :player (party) and :storage_system (PokemonStorage/PokemonBox).
/// No real fangame data involved.
/// </summary>
public static class EssentialsFixture
{
    public static byte[] BuildSave()
    {
        var partyPikachu = MakePokemon(
            species: "PIKACHU", level: 20, exp: 8000, nickname: "Sparky",
            moves: ["THUNDERSHOCK", "QUICKATTACK", "GROWL", "TAILWHIP"],
            ivs: [31, 30, 29, 28, 27, 26], evs: [4, 252, 0, 252, 0, 0],
            otId: 0x12345678, otName: "RED", personalId: 0xABCD1234);

        var boxedBulbasaur = MakePokemon(
            species: "BULBASAUR", level: 5, exp: 135, nickname: null,
            moves: ["TACKLE", "GROWL"],
            ivs: [10, 11, 12, 13, 14, 15], evs: [1, 2, 3, 4, 5, 6],
            otId: 0x12345678, otName: "RED", personalId: 0x00010002);
        // A fangame-specific ivar our mapper does not know; must survive a write untouched.
        boxedBulbasaur.Ivars["@custom_fangame_flag"] = new RubyInt(99);

        var player = new RubyObject("Player");
        player.Ivars["@name"] = RubyString.FromText("RED");
        player.Ivars["@id"] = new RubyInt(0x12345678);
        player.Ivars["@trainer_type"] = new RubySymbol("POKEMONTRAINER_RED");
        player.Ivars["@language"] = new RubyInt(2);
        player.Ivars["@gender"] = new RubyInt(0);
        player.Ivars["@party"] = new RubyArray([partyPikachu]);

        var storage = new RubyObject("PokemonStorage");
        storage.Ivars["@currentBox"] = new RubyInt(0);
        storage.Ivars["@boxes"] = new RubyArray([MakeBox("Box 1", (0, partyPikachu)), MakeBox("Box 2", (5, boxedBulbasaur))]);

        var root = new RubyHash([]);
        root.Entries.Add(new(new RubySymbol("essentials_version"), RubyString.FromText("21.1")));
        root.Entries.Add(new(new RubySymbol("game_version"), RubyString.FromText("1.0.0")));
        root.Entries.Add(new(new RubySymbol("player"), player));
        root.Entries.Add(new(new RubySymbol("storage_system"), storage));
        // An unrelated game object our reader must carry along untouched.
        var switches = new RubyObject("Game_Switches");
        switches.Ivars["@data"] = new RubyArray([RubyBool.True, RubyBool.False]);
        root.Entries.Add(new(new RubySymbol("switches"), switches));

        return RubyMarshalWriter.Write(root);
    }

    public static RubyObject MakePokemon(
        string species, int level, int exp, string? nickname,
        string[] moves, int[] ivs, int[] evs, long otId, string otName, long personalId)
    {
        var moveArray = new RubyArray(moves.Select(id =>
        {
            var m = new RubyObject("Pokemon::Move");
            m.Ivars["@id"] = new RubySymbol(id);
            m.Ivars["@pp"] = new RubyInt(20);
            m.Ivars["@ppup"] = new RubyInt(0);
            return (RubyValue)m;
        }).ToList());

        var iv = new RubyHash([]);
        var ev = new RubyHash([]);
        string[] keys = ["HP", "ATTACK", "DEFENSE", "SPEED", "SPECIALATTACK", "SPECIALDEFENSE"];
        for (var i = 0; i < 6; i++)
        {
            iv.Entries.Add(new(new RubySymbol(keys[i]), new RubyInt(ivs[i])));
            ev.Entries.Add(new(new RubySymbol(keys[i]), new RubyInt(evs[i])));
        }

        var owner = new RubyObject("Pokemon::Owner");
        owner.Ivars["@id"] = new RubyInt(otId);
        owner.Ivars["@name"] = RubyString.FromText(otName);
        owner.Ivars["@gender"] = new RubyInt(0);
        owner.Ivars["@language"] = new RubyInt(2);

        var p = new RubyObject("Pokemon");
        p.Ivars["@species"] = new RubySymbol(species);
        p.Ivars["@form"] = new RubyInt(0);
        p.Ivars["@level"] = new RubyInt(level);
        p.Ivars["@exp"] = new RubyInt(exp);
        p.Ivars["@steps_to_hatch"] = new RubyInt(0);
        p.Ivars["@status"] = new RubyInt(0);
        p.Ivars["@statusCount"] = new RubyInt(0);
        p.Ivars["@gender"] = RubyBool.True;
        p.Ivars["@shiny"] = RubyBool.False;
        p.Ivars["@ability"] = RubyNil.Instance;
        p.Ivars["@ability_index"] = new RubyInt(0);
        p.Ivars["@nature"] = new RubySymbol("ADAMANT");
        p.Ivars["@item"] = RubyNil.Instance;
        p.Ivars["@moves"] = moveArray;
        p.Ivars["@first_moves"] = new RubyArray([]);
        p.Ivars["@iv"] = iv;
        p.Ivars["@ivMaxed"] = new RubyHash([]);
        p.Ivars["@ev"] = ev;
        p.Ivars["@name"] = nickname is null ? RubyNil.Instance : RubyString.FromText(nickname);
        p.Ivars["@happiness"] = new RubyInt(70);
        p.Ivars["@poke_ball"] = new RubySymbol("POKEBALL");
        p.Ivars["@markings"] = new RubyArray([]);
        p.Ivars["@ribbons"] = new RubyArray([]);
        p.Ivars["@owner"] = owner;
        p.Ivars["@obtain_method"] = new RubyInt(0);
        p.Ivars["@obtain_level"] = new RubyInt(level);
        p.Ivars["@timeReceived"] = new RubyInt(1_700_000_000);
        p.Ivars["@personalID"] = new RubyInt(personalId);
        p.Ivars["@hp"] = new RubyInt(40);
        p.Ivars["@totalhp"] = new RubyInt(40);
        return p;
    }

    private static RubyObject MakeBox(string name, params (int Slot, RubyObject Pokemon)[] entries)
    {
        var slots = new RubyArray(Enumerable.Repeat(RubyNil.Instance, 30).Cast<RubyValue>().ToList());
        foreach (var (slot, pokemon) in entries)
            slots.Items[slot] = pokemon;
        var box = new RubyObject("PokemonBox");
        box.Ivars["@name"] = RubyString.FromText(name);
        box.Ivars["@background"] = new RubyInt(0);
        box.Ivars["@pokemon"] = slots;
        return box;
    }
}
