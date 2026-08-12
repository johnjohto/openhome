using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

/// <summary>
/// End-to-end vault tests. Each test gets a temp data root (SQLite db, saves,
/// backups) and saves generated via <see cref="BlankSaveFile"/> — only B, B2 and
/// BD reliably round-trip through <see cref="SaveUtil"/> detection.
/// </summary>
public class VaultServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}");
    private readonly OpenHomeOptions _options;
    private readonly OpenHomeDbContext _db;
    private readonly BackupService _backups;
    private readonly SaveLibraryService _library;
    private readonly VaultService _vault;

    public VaultServiceTests()
    {
        _options = new OpenHomeOptions(_root);
        _options.EnsureDirectories();
        _db = OpenHomeDbContext.Create(_options.DatabasePath);
        _db.Database.EnsureCreated();
        _backups = new BackupService(_options);
        _library = new SaveLibraryService(_db, new SaveFileService(), _backups, _options);
        _vault = new VaultService(_db, _library, _backups);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* SQLite may hold the file briefly on Windows */ }
    }

    private async Task<RegisteredSaveSummary> ImportSaveAsync(GameVersion version, string ot, int box = 0, int slot = 3, ushort species = 25, string nickname = "Pika")
    {
        var path = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.sav");
        var sav = BlankSaveFile.Get(version, ot);
        var pk = sav.BlankPKM;
        pk.Species = species;
        pk.Nickname = nickname;
        sav.SetBoxSlotAtIndex(pk, box, slot);
        sav.State.Edited = true;
        await File.WriteAllBytesAsync(path, sav.Write().ToArray());
        try
        {
            return await _library.RegisterAsync(path, $"{version}.sav");
        }
        finally
        {
            File.Delete(path);
        }
    }

    private SaveFile Reload(RegisteredSaveSummary save) =>
        new SaveFileService().Load(_db.SaveFiles.Find(save.Id)!.FilePath);

    [Fact]
    public async Task Deposit_MovesPokemonToVault_AndEmptiesSaveSlot()
    {
        var save = await ImportSaveAsync(GameVersion.B, "TEST");

        var stored = await _vault.DepositAsync(save.Id, box: 0, slot: 3);

        Assert.Equal(25, stored.Species);
        Assert.Equal("Pika", stored.Nickname);
        Assert.Equal("Vault 1", stored.BoxName);
        Assert.Equal(0, stored.Slot);
        Assert.NotEqual(0ul, stored.HomeTracker);

        // Reload the save from disk to prove the slot was cleared and persisted.
        var reloaded = Reload(save);
        Assert.Equal(0, reloaded.GetBoxSlotAtIndex(0, 3).Species);

        // Vault shows the deposit; row round-trips through SQLite.
        var boxes = await _vault.ListVaultBoxesAsync();
        var occupied = boxes[0].Slots.Single(s => !s.IsEmpty);
        Assert.Equal(25, occupied.Species);
        Assert.Equal(stored.Id, occupied.StoredPokemonId);
    }

    [Fact]
    public async Task Deposit_EmptySlot_Throws()
    {
        var save = await ImportSaveAsync(GameVersion.B, "TEST");
        await Assert.ThrowsAsync<InvalidOperationException>(() => _vault.DepositAsync(save.Id, box: 0, slot: 4));
    }

    [Fact]
    public async Task Withdraw_RoundTrip_BetweenTwoSaves_PreservesSpecies()
    {
        var saveA = await ImportSaveAsync(GameVersion.BD, "FIRST", species: 133, nickname: "Eevee");
        var saveB = await ImportSaveAsync(GameVersion.BD, "SECOND");

        var stored = await _vault.DepositAsync(saveA.Id, box: 0, slot: 3);
        var withdrawn = await _vault.WithdrawAsync(stored.Id, saveB.Id, box: 1, slot: 5);

        Assert.Equal(stored.Id, withdrawn.Id);

        // Reload save B from disk: the Pokémon survived PKH -> PB8 conversion + save write.
        var reloaded = Reload(saveB);
        var pk = reloaded.GetBoxSlotAtIndex(1, 5);
        Assert.Equal(133, pk.Species);
        Assert.IsType<PB8>(pk);

        // Gone from the vault, still in the origin save? No — origin slot was cleared at deposit.
        Assert.Empty(await _db.StoredPokemon.ToListAsync());
        Assert.Equal(0, Reload(saveA).GetBoxSlotAtIndex(0, 3).Species);
    }

    [Fact]
    public async Task Withdraw_ToOlderGeneration_ThrowsUnsupported()
    {
        // HOME semantics: no transfer back to a generation older than the origin.
        var saveA = await ImportSaveAsync(GameVersion.BD, "FIRST", species: 25, nickname: "Pika");
        var saveB = await ImportSaveAsync(GameVersion.B, "SECOND");

        var stored = await _vault.DepositAsync(saveA.Id, box: 0, slot: 3);
        await Assert.ThrowsAsync<UnsupportedConversionException>(() => _vault.WithdrawAsync(stored.Id, saveB.Id, box: 0, slot: 0));

        // Still in the vault, save B untouched.
        Assert.Single(await _db.StoredPokemon.ToListAsync());
        Assert.Equal(0, Reload(saveB).GetBoxSlotAtIndex(0, 0).Species);
    }

    [Fact]
    public async Task Deposit_CreatesBackup_BeforeWritingSave()
    {
        var save = await ImportSaveAsync(GameVersion.B, "TEST");
        var record = _db.SaveFiles.Find(save.Id)!;
        var bytesBeforeDeposit = await File.ReadAllBytesAsync(record.FilePath);

        await _vault.DepositAsync(save.Id, box: 0, slot: 3);

        var dir = Path.Combine(_options.BackupsDirectory, save.Id.ToString("N"));
        // One snapshot at import, one before the deposit write.
        var snapshots = Directory.GetFiles(dir);
        Assert.Equal(2, snapshots.Length);

        // The pre-deposit snapshots hold the save with the Pokémon still in the slot.
        // (Import and pre-deposit snapshots are byte-identical — no write happens in between.)
        var preDeposit = snapshots.First(p => File.ReadAllBytes(p).SequenceEqual(bytesBeforeDeposit));
        var restored = SaveUtil.GetSaveFile(preDeposit)!;
        Assert.Equal(25, restored.GetBoxSlotAtIndex(0, 3).Species);
    }

    [Fact]
    public async Task Withdraw_CreatesBackup_BeforeWritingSave()
    {
        var saveA = await ImportSaveAsync(GameVersion.BD, "FIRST");
        var saveB = await ImportSaveAsync(GameVersion.BD, "SECOND");
        var stored = await _vault.DepositAsync(saveA.Id, box: 0, slot: 3);
        var backupsBefore = Directory.GetFiles(Path.Combine(_options.BackupsDirectory, saveB.Id.ToString("N"))).Length;

        await _vault.WithdrawAsync(stored.Id, saveB.Id, box: 1, slot: 5);

        var backupsAfter = Directory.GetFiles(Path.Combine(_options.BackupsDirectory, saveB.Id.ToString("N"))).Length;
        Assert.Equal(backupsBefore + 1, backupsAfter);
    }

    [Fact]
    public async Task MovePokemon_BetweenBoxes_AndRejectsOccupiedSlot()
    {
        var save = await ImportSaveAsync(GameVersion.B, "TEST");
        var stored = await _vault.DepositAsync(save.Id, box: 0, slot: 3);

        var box2 = await _vault.CreateVaultBoxAsync("Second");
        var moved = await _vault.MovePokemonAsync(stored.Id, box2.Id, 7);
        Assert.Equal(box2.Id, moved.BoxId);
        Assert.Equal(7, moved.Slot);

        var save2 = await ImportSaveAsync(GameVersion.B, "TEST2");
        var stored2 = await _vault.DepositAsync(save2.Id, box: 0, slot: 3); // lands in Vault 1
        await Assert.ThrowsAsync<InvalidOperationException>(() => _vault.MovePokemonAsync(stored2.Id, box2.Id, 7));
    }

    [Fact]
    public async Task Deposit_CreatesNewVaultBox_WhenAllFull()
    {
        // Fill Vault 1 with 30 deposits, then one more must overflow into a new box.
        var saves = new List<RegisteredSaveSummary>();
        for (var i = 0; i < 31; i++)
            saves.Add(await ImportSaveAsync(GameVersion.B, $"T{i}", species: (ushort)(i + 1)));

        Guid? lastId = null;
        foreach (var save in saves)
            lastId = (await _vault.DepositAsync(save.Id, box: 0, slot: 3)).Id;

        var boxes = await _vault.ListVaultBoxesAsync();
        Assert.Equal(2, boxes.Count);
        Assert.Equal(30, boxes[0].Slots.Count(s => !s.IsEmpty));
        Assert.Equal(1, boxes[1].Slots.Count(s => !s.IsEmpty));
    }
}
