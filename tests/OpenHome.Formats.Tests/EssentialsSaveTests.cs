using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using OpenHome.Formats;
using OpenHome.Formats.Essentials;
using PKHeX.Core;

namespace OpenHome.Formats.Tests;

/// <summary>
/// Ticket #11: a synthetic Essentials Game.rxdata loads through the save library like any
/// other save, its boxes list, and deposit into the vault preserves species, moves, IVs,
/// EVs and OT. The fixture is written programmatically (no real fangame data).
/// </summary>
public class EssentialsSaveTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-ess-{Guid.NewGuid():N}");
    private readonly OpenHomeDbContext _db;
    private readonly SaveLibraryService _library;
    private readonly VaultService _vault;

    public EssentialsSaveTests()
    {
        var options = new OpenHomeOptions(_root);
        options.EnsureDirectories();
        _db = OpenHomeDbContext.Create(options.DatabasePath);
        _db.Database.EnsureCreated();
        var backups = new BackupService(options);
        _library = new SaveLibraryService(_db, new SaveFileService(), backups, options);
        _vault = new VaultService(_db, _library, backups, new LegalityService(_db));
        FormatsRegistration.RegisterAll();
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* SQLite may hold the file briefly on Windows */ }
    }

    [Fact]
    public void Save_loads_through_SaveUtil_with_boxes_and_party()
    {
        var bytes = EssentialsFixture.BuildSave();

        var sav = SaveUtil.GetSaveFile(bytes, "Game.rxdata");

        var ess = Assert.IsType<EssentialsSaveFile>(sav);
        Assert.Equal("21.1", ess.EssentialsVersion);
        Assert.Equal("RED", ess.OT);
        Assert.Equal(0x5678, ess.TID16);
        Assert.Equal(0x1234, ess.SID16);
        Assert.Equal(2, ess.BoxCount);
        Assert.Equal(30, ess.BoxSlotCount);
        Assert.Equal("Box 2", ((IBoxDetailName)ess).GetBoxName(1));

        var party = ess.GetPartySlotAtIndex(0);
        Assert.Equal((ushort)25, party.Species);
        Assert.Equal("Sparky", party.Nickname);

        var bulbasaur = ess.GetBoxSlotAtIndex(1, 5);
        Assert.Equal((ushort)1, bulbasaur.Species);
        Assert.False(bulbasaur.IsNicknamed);
        Assert.Equal(10, bulbasaur.IV_HP);
        Assert.Equal(15, bulbasaur.IV_SPD);
        Assert.Equal(6, bulbasaur.EV_SPD);
        Assert.Equal(45, bulbasaur.Move2); // Growl
    }

    [Fact]
    public void Random_binary_is_not_claimed()
    {
        var bytes = new byte[256];
        new Random(42).NextBytes(bytes);
        Assert.False(new EssentialsSaveReader().TryRead(bytes, out _, null));
    }

    [Fact]
    public async Task Deposit_preserves_species_moves_ivs_evs_and_ot()
    {
        var path = Path.Combine(_root, "Game.rxdata");
        await File.WriteAllBytesAsync(path, EssentialsFixture.BuildSave());
        var record = await _library.RegisterAsync(path, "Game.rxdata");

        var boxes = await _vault.ListSaveBoxesAsync(record.Id);
        Assert.False(boxes[0].Slots[0].IsEmpty);  // Pikachu
        Assert.False(boxes[1].Slots[5].IsEmpty);  // Bulbasaur

        var deposited = await _vault.DepositManyAsync(record.Id, [new BoxSlotRef(0, 0), new BoxSlotRef(1, 5)]);
        Assert.Equal(2, deposited.Count);

        var stored = await _db.StoredPokemon.OrderBy(p => p.Slot).ToListAsync();
        var pika = new PKH(stored[0].Data);
        Assert.Equal((ushort)25, pika.Species);
        Assert.Equal("Sparky", pika.Nickname);
        Assert.Equal("RED", pika.OriginalTrainerName);
        Assert.Equal(0x5678, pika.TID16);
        Assert.Equal(0x1234, pika.SID16);
        Assert.Equal((ushort)84, pika.Move1); // Thunder Shock
        Assert.Equal((ushort)98, pika.Move2); // Quick Attack
        Assert.Equal((ushort)45, pika.Move3); // Growl
        Assert.Equal((ushort)39, pika.Move4); // Tail Whip
        Assert.Equal(31, pika.IV_HP);
        Assert.Equal(30, pika.IV_ATK);
        Assert.Equal(29, pika.IV_DEF);
        Assert.Equal(28, pika.IV_SPE);
        Assert.Equal(27, pika.IV_SPA);
        Assert.Equal(26, pika.IV_SPD);
        Assert.Equal(4, pika.EV_HP);
        Assert.Equal(252, pika.EV_ATK);
        Assert.Equal(252, pika.EV_SPE);

        var bulb = new PKH(stored[1].Data);
        Assert.Equal((ushort)1, bulb.Species);
        Assert.Equal(11, bulb.IV_ATK);
        Assert.Equal(6, bulb.EV_SPD);

        // Deposit cleared the slots, persisted the rxdata, and the result still parses.
        var saveRecord = await _library.GetAsync(record.Id);
        var persisted = await File.ReadAllBytesAsync(saveRecord.FilePath);
        var reloaded = Assert.IsType<EssentialsSaveFile>(SaveUtil.GetSaveFile(persisted, saveRecord.FilePath));
        Assert.Equal(0, reloaded.GetBoxSlotAtIndex(0, 0).Species);
        Assert.Equal(0, reloaded.GetBoxSlotAtIndex(1, 5).Species);
        Assert.Equal((ushort)25, reloaded.GetPartySlotAtIndex(0).Species); // party untouched
    }

    [Fact]
    public async Task Write_preserves_unknown_objects_and_fangame_ivars()
    {
        var path = Path.Combine(_root, "Game.rxdata");
        await File.WriteAllBytesAsync(path, EssentialsFixture.BuildSave());
        var record = await _library.RegisterAsync(path, "Game.rxdata");
        await _vault.DepositAsync(record.Id, 0, 0); // clears only the Pikachu slot

        var saveRecord = await _library.GetAsync(record.Id);
        var persisted = await File.ReadAllBytesAsync(saveRecord.FilePath);
        var root = Assert.IsType<RubyHash>(RubyMarshalReader.Read(persisted));

        // Untouched game state round-tripped.
        var switches = Assert.IsType<RubyObject>(root["switches"]);
        Assert.Equal("Game_Switches", switches.ClassName);
        Assert.Equal("1.0.0", (root["game_version"] as RubyString)?.AsText());

        // The non-deposited Bulbasaur kept its original object, custom ivar included.
        var storage = Assert.IsType<RubyObject>(root["storage_system"]);
        var boxes = storage.GetArray("@boxes")!;
        var box2 = Assert.IsType<RubyObject>(boxes.Items[1]);
        var bulbasaur = Assert.IsType<RubyObject>(box2.GetArray("@pokemon")!.Items[5]);
        Assert.Equal(new RubyInt(99), bulbasaur.Get("@custom_fangame_flag"));
    }
}
