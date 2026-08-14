using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

/// <summary>
/// Strict/free transfer mode: the two HOME-parity checks (species presence in the
/// target game's Personal table, no backwards-generation transfer). Strict mode
/// refuses a failing withdraw with <see cref="TransferRefusedException"/>; free
/// mode performs it and returns the warnings. Same temp data root +
/// <see cref="BlankSaveFile"/> pattern as <see cref="VaultServiceTests"/>.
/// </summary>
public class TransferModeTests : IDisposable
{
    private const ushort Pikachu = 25;
    private const ushort Victini = 494; // absent from BDSP's Personal table

    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}");
    private readonly OpenHomeDbContext _db;
    private readonly BackupService _backups;
    private readonly SaveLibraryService _library;

    public TransferModeTests()
    {
        var options = new OpenHomeOptions(_root);
        options.EnsureDirectories();
        _db = OpenHomeDbContext.Create(options.DatabasePath);
        _db.Database.EnsureCreated();
        _backups = new BackupService(options);
        _library = new SaveLibraryService(_db, new SaveFileService(), _backups, options);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* SQLite may hold the file briefly on Windows */ }
    }

    /// <summary>Builds a vault service with the given transfer mode.</summary>
    private VaultService Vault(bool strict) => new(
        _db, _library, _backups, new LegalityService(_db), new OpenHomeOptions(_root, strictTransfers: strict));

    /// <summary>Registers a blank BDSP save holding one Pokémon per requested slot.</summary>
    private async Task<RegisteredSaveSummary> ImportSaveAsync(string ot, params (ushort Species, int Box, int Slot)[] occupied)
    {
        var path = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.sav");
        var sav = BlankSaveFile.Get(GameVersion.BD, ot);
        var i = 0;
        foreach (var (species, box, slot) in occupied)
        {
            i++;
            var pk = sav.BlankPKM;
            pk.Species = species;
            pk.Nickname = $"Mon{i}";
            pk.OriginalTrainerName = ot;
            pk.Language = (int)LanguageID.English;
            pk.PID = 0x12345678 + (uint)i;
            sav.SetBoxSlotAtIndex(pk, box, slot);
        }
        sav.State.Edited = true;
        await File.WriteAllBytesAsync(path, sav.Write().ToArray());
        try
        {
            return await _library.RegisterAsync(path, $"BD-{ot}.sav");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>
    /// Stores a gen-9 (Scarlet) Pikachu directly in the vault — no gen-9 blank save
    /// round-trips through SaveUtil, so the row is fabricated with real PKH bytes.
    /// </summary>
    private async Task<StoredPokemon> StoreGen9PikachuAsync()
    {
        var box = new VaultBox { Id = Guid.NewGuid(), Name = "Vault 1", Order = 0 };
        _db.VaultBoxes.Add(box);
        var pk9 = new PK9
        {
            Species = Pikachu,
            Nickname = "Pika9",
            OriginalTrainerName = "TEST",
            Language = (int)LanguageID.English,
            PID = 0x12345678,
            Version = GameVersion.SL, // required so LatestGameData stays gen 9 through Rebuild()
        };
        var stored = new StoredPokemon
        {
            Id = Guid.NewGuid(),
            VaultBoxId = box.Id,
            Slot = 0,
            Data = PKH.ConvertFromPKM(pk9).Rebuild(),
            Species = Pikachu,
            Nickname = "Pika9",
            OTName = "TEST",
            OriginGame = "Scarlet",
            HomeTracker = 424242,
            DepositedAt = DateTime.UtcNow,
        };
        _db.StoredPokemon.Add(stored);
        await _db.SaveChangesAsync();
        return stored;
    }

    private SaveFile Reload(RegisteredSaveSummary save) =>
        new SaveFileService().Load(_db.SaveFiles.Find(save.Id)!.FilePath);

    [Fact]
    public async Task Strict_Refuses_BackwardsGeneration_Withdraw_WithReason()
    {
        var save = await ImportSaveAsync("TEST");
        var stored = await StoreGen9PikachuAsync();

        var ex = await Assert.ThrowsAsync<TransferRefusedException>(() =>
            Vault(strict: true).WithdrawAsync(stored.Id, save.Id, 0, 0));

        Assert.Contains("backwards", ex.Message);
        Assert.Contains("generation 9", ex.Message);
        Assert.Single(await _db.StoredPokemon.ToListAsync()); // still in the vault
        Assert.Equal(0, Reload(save).GetBoxSlotAtIndex(0, 0).Species); // save untouched
    }

    [Fact]
    public async Task Free_Allows_BackwardsGeneration_Withdraw_WithWarnings()
    {
        var save = await ImportSaveAsync("TEST");
        var stored = await StoreGen9PikachuAsync();

        // PKHeX does have a PKH(gen 9) -> PB8 route, so free mode can perform it.
        var result = await Vault(strict: false).WithdrawAsync(stored.Id, save.Id, 0, 0);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("backwards", warning);
        Assert.Equal(stored.Id, result.Pokemon.Id);

        var placed = Reload(save).GetBoxSlotAtIndex(0, 0);
        Assert.Equal(Pikachu, placed.Species);
        Assert.IsType<PB8>(placed);
        Assert.Empty(await _db.StoredPokemon.ToListAsync()); // out of the vault
    }

    [Fact]
    public async Task Strict_Refuses_SpeciesAbsentFromTargetGame()
    {
        // Victini converts into BDSP's entity format fine; it just isn't in the game.
        var saveA = await ImportSaveAsync("FIRST", (Victini, 0, 3));
        var saveB = await ImportSaveAsync("SECOND");
        var stored = await Vault(strict: false).DepositAsync(saveA.Id, 0, 3);

        var ex = await Assert.ThrowsAsync<TransferRefusedException>(() =>
            Vault(strict: true).WithdrawAsync(stored.Id, saveB.Id, 0, 0));

        Assert.Contains("not present", ex.Message);
        Assert.Single(await _db.StoredPokemon.ToListAsync());
        Assert.Equal(0, Reload(saveB).GetBoxSlotAtIndex(0, 0).Species);
    }

    [Fact]
    public async Task Free_Allows_SpeciesAbsentFromTargetGame_WithWarning()
    {
        var saveA = await ImportSaveAsync("FIRST", (Victini, 0, 3));
        var saveB = await ImportSaveAsync("SECOND");
        var stored = await Vault(strict: false).DepositAsync(saveA.Id, 0, 3);

        var result = await Vault(strict: false).WithdrawAsync(stored.Id, saveB.Id, 0, 0);

        var warning = Assert.Single(result.Warnings);
        Assert.Contains("not present", warning);
        Assert.Equal(Victini, Reload(saveB).GetBoxSlotAtIndex(0, 0).Species);
    }

    [Fact]
    public async Task Strict_Allows_LegalTransfer_WithoutWarnings()
    {
        var saveA = await ImportSaveAsync("FIRST", (Pikachu, 0, 3));
        var saveB = await ImportSaveAsync("SECOND");
        var stored = await Vault(strict: true).DepositAsync(saveA.Id, 0, 3);

        var result = await Vault(strict: true).WithdrawAsync(stored.Id, saveB.Id, 0, 0);

        Assert.Empty(result.Warnings);
        Assert.Equal(Pikachu, Reload(saveB).GetBoxSlotAtIndex(0, 0).Species);
    }
}
