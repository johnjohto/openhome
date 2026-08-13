using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

/// <summary>
/// Local trades: the swap between two registered saves, trade evolution on receipt
/// (Kadabra → Alakazam, Onix + Metal Coat → Steelix) verified by reloading the save
/// from disk, and the pre-write backup snapshots. Same temp data root +
/// <see cref="BlankSaveFile"/> pattern as <see cref="VaultServiceTests"/>.
/// </summary>
public class TradeTests : IDisposable
{
    private const ushort Kadabra = 64;
    private const ushort Alakazam = 65;
    private const ushort Onix = 95;
    private const ushort Steelix = 208;
    private const ushort Pikachu = 25;
    private const ushort Rattata = 19;
    private const int MetalCoat = 233;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}");
    private readonly OpenHomeOptions _options;
    private readonly OpenHomeDbContext _db;
    private readonly SaveLibraryService _library;
    private readonly TradeService _trades;

    public TradeTests()
    {
        _options = new OpenHomeOptions(_root);
        _options.EnsureDirectories();
        _db = OpenHomeDbContext.Create(_options.DatabasePath);
        _db.Database.EnsureCreated();
        var backups = new BackupService(_options);
        _library = new SaveLibraryService(_db, new SaveFileService(), backups, _options);
        _trades = new TradeService(_library, backups);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* SQLite may hold the file briefly on Windows */ }
    }

    /// <summary>Registers a blank Black save holding one Pokémon per requested slot.</summary>
    private async Task<RegisteredSaveSummary> ImportSaveAsync(string ot, params (ushort Species, int HeldItem, int Box, int Slot)[] occupied)
    {
        var path = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.sav");
        var sav = BlankSaveFile.Get(GameVersion.B, ot);
        var i = 0;
        foreach (var (species, heldItem, box, slot) in occupied)
        {
            i++;
            var pk = sav.BlankPKM;
            pk.Species = species;
            pk.Nickname = $"Mon{i}";
            pk.HeldItem = heldItem;
            // BlankPKM has Language 0 (JP) and PID 0 — set both, see VaultReadTests.
            pk.OriginalTrainerName = ot;
            pk.Language = (int)LanguageID.English;
            pk.PID = 0x12345678 + (uint)i;
            sav.SetBoxSlotAtIndex(pk, box, slot);
        }
        sav.State.Edited = true;
        await File.WriteAllBytesAsync(path, sav.Write().ToArray());
        try
        {
            return await _library.RegisterAsync(path, $"B-{ot}.sav");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private SaveFile Reload(RegisteredSaveSummary save) =>
        new SaveFileService().Load(_db.SaveFiles.Find(save.Id)!.FilePath);

    private int BackupCount(Guid saveId)
    {
        var dir = Path.Combine(_options.BackupsDirectory, saveId.ToString("N"));
        return Directory.Exists(dir) ? Directory.GetFiles(dir).Length : 0;
    }

    [Fact]
    public async Task Trade_KadabraBetweenTwoSaves_EvolvesIntoAlakazam_AndPersistsToDisk()
    {
        var saveA = await ImportSaveAsync("FIRST", (Kadabra, 0, 0, 3));
        var saveB = await ImportSaveAsync("SECOND", (Rattata, 0, 0, 5));

        var report = await _trades.TradeAsync(saveA.Id, 0, 3, saveB.Id, 0, 5);

        // Save B received the Kadabra and it evolved on receipt.
        Assert.True(report.SideB.Evolved);
        Assert.Equal(Kadabra, report.SideB.EvolvedFromSpecies);
        Assert.Equal(Alakazam, report.SideB.Species);
        Assert.Equal("Alakazam", report.SideB.SpeciesName);
        Assert.Equal(saveB.Id, report.SideB.SaveId);

        // Save A received the Rattata, which has no trade evolution.
        Assert.False(report.SideA.Evolved);
        Assert.Equal(Rattata, report.SideA.Species);
        Assert.Equal(saveA.Id, report.SideA.SaveId);

        // Verify against what actually landed on disk.
        var reloadedB = Reload(saveB);
        var received = reloadedB.GetBoxSlotAtIndex(0, 5);
        Assert.Equal(Alakazam, received.Species);
        Assert.Equal("Mon1", received.Nickname); // nickname survives the evolution

        var reloadedA = Reload(saveA);
        Assert.Equal(Rattata, reloadedA.GetBoxSlotAtIndex(0, 3).Species);
        Assert.Equal(0, reloadedA.GetBoxSlotAtIndex(0, 5).Species); // A's other slots untouched
    }

    [Fact]
    public async Task Trade_OnixWithoutMetalCoat_DoesNotEvolve()
    {
        var saveA = await ImportSaveAsync("FIRST", (Onix, 0, 0, 3));
        var saveB = await ImportSaveAsync("SECOND", (Rattata, 0, 0, 5));

        var report = await _trades.TradeAsync(saveA.Id, 0, 3, saveB.Id, 0, 5);

        Assert.False(report.SideB.Evolved);
        Assert.Equal(Onix, Reload(saveB).GetBoxSlotAtIndex(0, 5).Species);
    }

    [Fact]
    public async Task Trade_OnixHoldingMetalCoat_EvolvesIntoSteelix()
    {
        var saveA = await ImportSaveAsync("FIRST", (Onix, MetalCoat, 0, 3));
        var saveB = await ImportSaveAsync("SECOND", (Rattata, 0, 0, 5));

        var report = await _trades.TradeAsync(saveA.Id, 0, 3, saveB.Id, 0, 5);

        Assert.True(report.SideB.Evolved);
        Assert.Equal(Onix, report.SideB.EvolvedFromSpecies);
        Assert.Equal(Steelix, report.SideB.Species);

        var evolved = Reload(saveB).GetBoxSlotAtIndex(0, 5);
        Assert.Equal(Steelix, evolved.Species);
        Assert.Equal(MetalCoat, evolved.HeldItem); // the item is not consumed by trade evolution
    }

    [Fact]
    public async Task Trade_NonTradeSpecies_Pikachu_Unchanged()
    {
        var saveA = await ImportSaveAsync("FIRST", (Pikachu, 0, 0, 3));
        var saveB = await ImportSaveAsync("SECOND", (Rattata, 0, 0, 5));

        var report = await _trades.TradeAsync(saveA.Id, 0, 3, saveB.Id, 0, 5);

        Assert.False(report.SideB.Evolved);
        Assert.Equal(Pikachu, Reload(saveB).GetBoxSlotAtIndex(0, 5).Species);
        Assert.Equal(Rattata, Reload(saveA).GetBoxSlotAtIndex(0, 3).Species);
    }

    [Fact]
    public async Task Trade_SnapshotsBothSaves_BeforeWriting()
    {
        var saveA = await ImportSaveAsync("FIRST", (Kadabra, 0, 0, 3));
        var saveB = await ImportSaveAsync("SECOND", (Rattata, 0, 0, 5));
        var beforeA = BackupCount(saveA.Id); // one snapshot each at import
        var beforeB = BackupCount(saveB.Id);

        await _trades.TradeAsync(saveA.Id, 0, 3, saveB.Id, 0, 5);

        Assert.Equal(beforeA + 1, BackupCount(saveA.Id));
        Assert.Equal(beforeB + 1, BackupCount(saveB.Id));
    }

    [Fact]
    public async Task Trade_WithinSameSave_AlsoEvolves_AndSnapshotsOnce()
    {
        // Self-trade evolution is a core fan request: box-to-box within one save.
        var save = await ImportSaveAsync("TEST", (Kadabra, 0, 0, 3), (Rattata, 0, 0, 7));
        var before = BackupCount(save.Id);

        var report = await _trades.TradeAsync(save.Id, 0, 3, save.Id, 0, 7);

        // Slot (0,3) received the Rattata (no evolution); slot (0,7) received the
        // Kadabra, which evolved on receipt — self-trades count as trades.
        Assert.False(report.SideA.Evolved);
        Assert.Equal(Rattata, report.SideA.Species);
        Assert.True(report.SideB.Evolved);
        Assert.Equal(Alakazam, report.SideB.Species);

        var reloaded = Reload(save);
        Assert.Equal(Rattata, reloaded.GetBoxSlotAtIndex(0, 3).Species);
        Assert.Equal(Alakazam, reloaded.GetBoxSlotAtIndex(0, 7).Species);
        Assert.Equal(before + 1, BackupCount(save.Id)); // one save, one snapshot
    }

    [Fact]
    public async Task Trade_EmptySlot_Throws_AndPersistsNothing()
    {
        var saveA = await ImportSaveAsync("FIRST", (Kadabra, 0, 0, 3));
        var saveB = await ImportSaveAsync("SECOND", (Rattata, 0, 0, 5));
        var beforeB = BackupCount(saveB.Id);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _trades.TradeAsync(saveA.Id, 0, 4, saveB.Id, 0, 5));

        Assert.Equal(beforeB, BackupCount(saveB.Id)); // no snapshot, no write
        Assert.Equal(Rattata, Reload(saveB).GetBoxSlotAtIndex(0, 5).Species);
        Assert.Equal(Kadabra, Reload(saveA).GetBoxSlotAtIndex(0, 3).Species);
    }

    [Fact]
    public async Task Trade_SameSlot_Throws()
    {
        var save = await ImportSaveAsync("TEST", (Kadabra, 0, 0, 3));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _trades.TradeAsync(save.Id, 0, 3, save.Id, 0, 3));
    }
}
