using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

/// <summary>
/// Bulk operations: multi-slot deposit (one snapshot, one save write, fill-in-order
/// with box overflow), bulk move within the vault, and mass release which reports
/// what was released. Same temp data root + <see cref="BlankSaveFile"/> pattern as
/// <see cref="VaultServiceTests"/>.
/// </summary>
public class VaultBulkTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}");
    private readonly OpenHomeOptions _options;
    private readonly OpenHomeDbContext _db;
    private readonly BackupService _backups;
    private readonly SaveLibraryService _library;
    private readonly VaultService _vault;

    public VaultBulkTests()
    {
        _options = new OpenHomeOptions(_root);
        _options.EnsureDirectories();
        _db = OpenHomeDbContext.Create(_options.DatabasePath);
        _db.Database.EnsureCreated();
        _backups = new BackupService(_options);
        _library = new SaveLibraryService(_db, new SaveFileService(), _backups, _options);
        _vault = new VaultService(_db, _library, _backups, new LegalityService(_db));
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* SQLite may hold the file briefly on Windows */ }
    }

    /// <summary>
    /// Registers a blank Black save holding one Pokémon per requested slot
    /// (species = index + 1 so slots are distinguishable).
    /// </summary>
    private async Task<RegisteredSaveSummary> ImportSaveAsync(string ot, params (int Box, int Slot)[] occupied)
    {
        var path = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.sav");
        var sav = BlankSaveFile.Get(GameVersion.B, ot);
        var i = 0;
        foreach (var (box, slot) in occupied)
        {
            i++;
            var pk = sav.BlankPKM;
            pk.Species = (ushort)i;
            pk.Nickname = $"Mon{i}";
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

    [Fact]
    public async Task DepositMany_FillsVaultInOrder_AndClearsSaveSlots()
    {
        var save = await ImportSaveAsync("TEST", (0, 7), (0, 3), (1, 0));

        var results = await _vault.DepositManyAsync(save.Id,
            [new BoxSlotRef(0, 7), new BoxSlotRef(0, 3), new BoxSlotRef(1, 0)]);

        // Given order is preserved: first ref lands in slot 0, and so on.
        Assert.Equal(3, results.Count);
        Assert.Equal([0, 1, 2], results.Select(r => r.Slot).ToArray());
        Assert.Equal([1, 2, 3], results.Select(r => r.Species).ToArray());
        Assert.All(results, r => Assert.Equal("Vault 1", r.BoxName));

        var reloaded = Reload(save);
        Assert.Equal(0, reloaded.GetBoxSlotAtIndex(0, 7).Species);
        Assert.Equal(0, reloaded.GetBoxSlotAtIndex(0, 3).Species);
        Assert.Equal(0, reloaded.GetBoxSlotAtIndex(1, 0).Species);
    }

    [Fact]
    public async Task DepositMany_OneSnapshot_AndOneWrite_ForTheWholeBatch()
    {
        var save = await ImportSaveAsync("TEST", (0, 3), (0, 4));
        var dir = Path.Combine(_options.BackupsDirectory, save.Id.ToString("N"));
        var before = Directory.GetFiles(dir).Length; // one snapshot at import

        await _vault.DepositManyAsync(save.Id, [new BoxSlotRef(0, 3), new BoxSlotRef(0, 4)]);

        Assert.Equal(before + 1, Directory.GetFiles(dir).Length);
    }

    [Fact]
    public async Task DepositMany_EmptySlot_Throws_AndPersistsNothing()
    {
        var save = await ImportSaveAsync("TEST", (0, 3));

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _vault.DepositManyAsync(save.Id, [new BoxSlotRef(0, 3), new BoxSlotRef(0, 4)]));

        Assert.Empty(await _db.StoredPokemon.ToListAsync());
        Assert.Equal(1, Reload(save).GetBoxSlotAtIndex(0, 3).Species);
    }

    [Fact]
    public async Task DepositMany_DuplicateSlot_Throws()
    {
        var save = await ImportSaveAsync("TEST", (0, 3));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _vault.DepositManyAsync(save.Id, [new BoxSlotRef(0, 3), new BoxSlotRef(0, 3)]));
    }

    [Fact]
    public async Task DepositMany_EmptySelection_ReturnsEmpty()
    {
        var save = await ImportSaveAsync("TEST", (0, 3));
        Assert.Empty(await _vault.DepositManyAsync(save.Id, []));
    }

    [Fact]
    public async Task DepositMany_OverflowsIntoANewVaultBox()
    {
        // One save carrying 31 Pokémon: 30 fill Vault 1, the 31st creates Vault 2.
        var occupied = Enumerable.Range(0, 30).Select(slot => (0, slot)).Append((1, 0)).ToArray();
        var save = await ImportSaveAsync("TEST", occupied);
        var refs = occupied.Select(s => new BoxSlotRef(s.Item1, s.Item2)).ToArray();

        var results = await _vault.DepositManyAsync(save.Id, refs);

        Assert.Equal(31, results.Count);
        var boxes = await _vault.ListVaultBoxesAsync();
        Assert.Equal(2, boxes.Count);
        Assert.Equal(30, boxes[0].Slots.Count(s => !s.IsEmpty));
        Assert.Equal(1, boxes[1].Slots.Count(s => !s.IsEmpty));
        Assert.Equal(results[30].Id, boxes[1].Slots.Single(s => !s.IsEmpty).StoredPokemonId);
    }

    [Fact]
    public async Task MoveMany_FillsTargetBoxFreeSlotsInOrder()
    {
        var save = await ImportSaveAsync("TEST", (0, 3), (0, 4), (0, 5));
        var deposited = await _vault.DepositManyAsync(save.Id,
            [new BoxSlotRef(0, 3), new BoxSlotRef(0, 4), new BoxSlotRef(0, 5)]);
        var box2 = await _vault.CreateVaultBoxAsync("Second");

        // Move two of the three, reversed order — allocation follows the given order.
        var moved = await _vault.MoveManyAsync([deposited[2].Id, deposited[0].Id], box2.Id);

        Assert.Equal([deposited[2].Id, deposited[0].Id], moved.Select(m => m.Id).ToArray());
        Assert.Equal([0, 1], moved.Select(m => m.Slot).ToArray());
        Assert.All(moved, m => Assert.Equal("Second", m.BoxName));

        var remaining = await _db.StoredPokemon.SingleAsync(p => p.Id == deposited[1].Id);
        Assert.NotEqual(box2.Id, remaining.VaultBoxId);
    }

    [Fact]
    public async Task MoveMany_WithinSameBox_FreesCurrentSlotsFirst()
    {
        var save = await ImportSaveAsync("TEST", (0, 3), (0, 4));
        var deposited = await _vault.DepositManyAsync(save.Id, [new BoxSlotRef(0, 3), new BoxSlotRef(0, 4)]);
        var vault1 = (await _vault.ListVaultBoxesAsync())[0];

        var moved = await _vault.MoveManyAsync([deposited[1].Id, deposited[0].Id], vault1.Id);

        // Their old slots were freed before allocating, so they settle back into 0 and 1.
        Assert.Equal([0, 1], moved.Select(m => m.Slot).ToArray());
        Assert.All(moved, m => Assert.Equal(vault1.Id, m.BoxId));
    }

    [Fact]
    public async Task MoveMany_OverflowsPastTheTargetBox()
    {
        // Fill Vault 1 (30) and put one in Vault 2, then move all 30 into Vault 2:
        // 29 free slots there, the last one spills into a new Vault 3.
        var firstSave = await ImportSaveAsync("FIRST", Enumerable.Range(0, 30).Select(slot => (0, slot)).ToArray());
        var first = await _vault.DepositManyAsync(firstSave.Id,
            Enumerable.Range(0, 30).Select(slot => new BoxSlotRef(0, slot)).ToArray());
        var secondSave = await ImportSaveAsync("SECOND", (0, 3));
        await _vault.DepositAsync(secondSave.Id, box: 0, slot: 3);

        var boxes = await _vault.ListVaultBoxesAsync();
        var moved = await _vault.MoveManyAsync(first.Select(p => p.Id).ToArray(), boxes[1].Id);

        Assert.Equal(30, moved.Count);
        Assert.Equal(29, moved.Count(m => m.BoxId == boxes[1].Id));
        var spilled = moved.Single(m => m.BoxId != boxes[1].Id);
        Assert.Equal(0, spilled.Slot);
        var after = await _vault.ListVaultBoxesAsync();
        Assert.Equal(3, after.Count);
        Assert.Equal(spilled.BoxId, after[2].Id);
    }

    [Fact]
    public async Task MoveMany_UnknownBoxOrId_Throws()
    {
        var save = await ImportSaveAsync("TEST", (0, 3));
        var deposited = await _vault.DepositAsync(save.Id, box: 0, slot: 3);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _vault.MoveManyAsync([deposited.Id], Guid.NewGuid()));
        var box = (await _vault.ListVaultBoxesAsync())[0];
        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _vault.MoveManyAsync([Guid.NewGuid()], box.Id));
    }

    [Fact]
    public async Task ReleaseMany_RemovesRows_AndReportsWhatWasReleased()
    {
        var save = await ImportSaveAsync("TEST", (0, 3), (0, 4), (0, 5));
        var deposited = await _vault.DepositManyAsync(save.Id,
            [new BoxSlotRef(0, 3), new BoxSlotRef(0, 4), new BoxSlotRef(0, 5)]);

        var released = await _vault.ReleaseManyAsync([deposited[0].Id, deposited[2].Id]);

        Assert.Equal(2, released.Count);
        Assert.Equal([deposited[0].Id, deposited[2].Id], released.Select(r => r.Id).ToArray());
        Assert.Equal(["Mon1", "Mon3"], released.Select(r => r.Nickname).ToArray());
        Assert.All(released, r => Assert.Equal("Vault 1", r.BoxName));

        var remaining = await _db.StoredPokemon.ToListAsync();
        Assert.Equal([deposited[1].Id], remaining.Select(p => p.Id).ToArray());
    }

    [Fact]
    public async Task ReleaseMany_UnknownId_Throws_AndDeletesNothing()
    {
        var save = await ImportSaveAsync("TEST", (0, 3));
        var deposited = await _vault.DepositAsync(save.Id, box: 0, slot: 3);

        await Assert.ThrowsAsync<KeyNotFoundException>(() =>
            _vault.ReleaseManyAsync([deposited.Id, Guid.NewGuid()]));

        Assert.Single(await _db.StoredPokemon.ToListAsync());
    }

    [Fact]
    public async Task ReleaseMany_EmptySelection_ReturnsEmpty()
    {
        Assert.Empty(await _vault.ReleaseManyAsync([]));
    }
}
