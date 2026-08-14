using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

/// <summary>
/// Item vault: depositing a held item clears it off the Pokémon and stacks the
/// vault count; withdrawing puts an item onto an empty-handed Pokémon and refuses
/// occupied hands; both snapshot the save first. Same temp data root +
/// <see cref="BlankSaveFile"/> pattern as <see cref="VaultServiceTests"/>.
/// </summary>
public class ItemVaultTests : IDisposable
{
    private const ushort Onix = 95;
    private const ushort Rattata = 19;
    private const int MetalCoat = 233;
    private const int Leftovers = 234;

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}");
    private readonly OpenHomeOptions _options;
    private readonly OpenHomeDbContext _db;
    private readonly SaveLibraryService _library;
    private readonly ItemVaultService _items;

    public ItemVaultTests()
    {
        _options = new OpenHomeOptions(_root);
        _options.EnsureDirectories();
        _db = OpenHomeDbContext.Create(_options.DatabasePath);
        _db.Database.EnsureCreated();
        var backups = new BackupService(_options);
        _library = new SaveLibraryService(_db, new SaveFileService(), backups, _options);
        _items = new ItemVaultService(_db, _library, backups);
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
    public async Task DepositItem_ClearsHeldItem_AndStacksCount()
    {
        var save = await ImportSaveAsync("TEST", (Onix, MetalCoat, 0, 3), (Rattata, MetalCoat, 0, 4));

        var first = await _items.DepositItemAsync(save.Id, 0, 3);
        var second = await _items.DepositItemAsync(save.Id, 0, 4);

        Assert.Equal(MetalCoat, first.ItemId);
        Assert.Equal("Metal Coat", first.Name);
        Assert.Equal(1, first.Count);
        Assert.Equal(2, second.Count); // stacks with the existing count

        var reloaded = Reload(save);
        Assert.Equal(0, reloaded.GetBoxSlotAtIndex(0, 3).HeldItem);
        Assert.Equal(0, reloaded.GetBoxSlotAtIndex(0, 4).HeldItem);
        Assert.Equal(Onix, reloaded.GetBoxSlotAtIndex(0, 3).Species); // the Pokémon stays

        var row = await _db.VaultItems.SingleAsync();
        Assert.Equal(MetalCoat, row.ItemId);
        Assert.Equal(2, row.Count);
    }

    [Fact]
    public async Task DepositItem_NoItemOrEmptySlot_Throws()
    {
        var save = await ImportSaveAsync("TEST", (Rattata, 0, 0, 3));

        await Assert.ThrowsAsync<InvalidOperationException>(() => _items.DepositItemAsync(save.Id, 0, 3));
        await Assert.ThrowsAsync<InvalidOperationException>(() => _items.DepositItemAsync(save.Id, 0, 4));

        Assert.Empty(await _db.VaultItems.ToListAsync());
        Assert.Equal(Rattata, Reload(save).GetBoxSlotAtIndex(0, 3).Species); // nothing persisted
    }

    [Fact]
    public async Task WithdrawItem_OntoEmptyHanded_SetsHeldItem_AndDrainsStack()
    {
        var save = await ImportSaveAsync("TEST", (Onix, MetalCoat, 0, 3), (Rattata, 0, 0, 5));
        await _items.DepositItemAsync(save.Id, 0, 3);

        var result = await _items.WithdrawItemAsync(MetalCoat, save.Id, 0, 5);

        Assert.Equal(0, result.Count);
        Assert.Equal(MetalCoat, Reload(save).GetBoxSlotAtIndex(0, 5).HeldItem);
        Assert.Empty(await _db.VaultItems.ToListAsync()); // row removed at zero
    }

    [Fact]
    public async Task WithdrawItem_OntoOccupiedHands_Refuses()
    {
        var save = await ImportSaveAsync("TEST", (Onix, MetalCoat, 0, 3), (Rattata, Leftovers, 0, 5));
        await _items.DepositItemAsync(save.Id, 0, 3);
        var backupsBefore = BackupCount(save.Id);

        var ex = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _items.WithdrawItemAsync(MetalCoat, save.Id, 0, 5));

        Assert.Contains("already holding", ex.Message);
        Assert.Equal(Leftovers, Reload(save).GetBoxSlotAtIndex(0, 5).HeldItem); // untouched
        Assert.Equal(1, (await _db.VaultItems.SingleAsync()).Count); // stack untouched
        Assert.Equal(backupsBefore, BackupCount(save.Id)); // refused before any snapshot
    }

    [Fact]
    public async Task WithdrawItem_EmptyStack_Throws()
    {
        var save = await ImportSaveAsync("TEST", (Rattata, 0, 0, 5));
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _items.WithdrawItemAsync(MetalCoat, save.Id, 0, 5));
    }

    [Fact]
    public async Task Deposit_And_Withdraw_SnapshotTheSave()
    {
        var save = await ImportSaveAsync("TEST", (Onix, MetalCoat, 0, 3), (Rattata, 0, 0, 5));
        var before = BackupCount(save.Id); // one snapshot at import

        await _items.DepositItemAsync(save.Id, 0, 3);
        Assert.Equal(before + 1, BackupCount(save.Id));

        await _items.WithdrawItemAsync(MetalCoat, save.Id, 0, 5);
        Assert.Equal(before + 2, BackupCount(save.Id));
    }
}
