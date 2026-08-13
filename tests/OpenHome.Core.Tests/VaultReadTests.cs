using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

/// <summary>
/// Vault read endpoints: the denormalized index and the per-Pokémon detail read
/// (IVs/EVs/moves deserialized back out of the stored PKH bytes). Same temp data
/// root + <see cref="BlankSaveFile"/> pattern as <see cref="VaultServiceTests"/>.
/// </summary>
public class VaultReadTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}");
    private readonly OpenHomeOptions _options;
    private readonly OpenHomeDbContext _db;
    private readonly BackupService _backups;
    private readonly SaveLibraryService _library;
    private readonly VaultService _vault;

    public VaultReadTests()
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

    private async Task<RegisteredSaveSummary> ImportSaveAsync(string ot, ushort species, string nickname, Action<PKM>? customize = null)
    {
        var path = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.sav");
        var sav = BlankSaveFile.Get(GameVersion.B, ot);
        var pk = sav.BlankPKM;
        pk.Species = species;
        pk.Nickname = nickname;
        // BlankPKM starts with empty OT, Language 0 and PID 0 (which reads shiny against a blank trainer) — give it all three.
        pk.OriginalTrainerName = ot;
        pk.Language = (int)LanguageID.English;
        pk.PID = 0x12345678;
        customize?.Invoke(pk);
        sav.SetBoxSlotAtIndex(pk, 0, 3);
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

    [Fact]
    public async Task ListStoredPokemon_ReturnsAll_Denormalized_InBoxOrder()
    {
        var saveA = await ImportSaveAsync("FIRST", 25, "Pika");
        var saveB = await ImportSaveAsync("SECOND", 133, "Eevee");
        var first = await _vault.DepositAsync(saveA.Id, box: 0, slot: 3);
        var second = await _vault.DepositAsync(saveB.Id, box: 0, slot: 3);

        var list = await _vault.ListStoredPokemonAsync();

        Assert.Equal(2, list.Count);
        Assert.Equal(first.Id, list[0].Id);
        Assert.Equal(second.Id, list[1].Id);

        var pika = list[0];
        Assert.Equal("Vault 1", pika.BoxName);
        Assert.Equal(first.BoxId, pika.BoxId);
        Assert.Equal(0, pika.Slot);
        Assert.Equal(25, pika.Species);
        Assert.Equal(0, pika.Form);
        Assert.Equal("Pika", pika.Nickname);
        Assert.False(pika.IsShiny);
        Assert.Equal("FIRST", pika.OTName);
        Assert.Equal(GameInfo.GetVersionName(GameVersion.B), pika.OriginGame);
        Assert.NotEqual(0ul, pika.HomeTracker);
        Assert.True(pika.DepositedAt > DateTime.MinValue);
    }

    [Fact]
    public async Task GetStoredPokemon_RoundTrips_IvsEvsMoves_FromPkhBytes()
    {
        var save = await ImportSaveAsync("TEST", 25, "Pika", pk =>
        {
            pk.IV_HP = 31; pk.IV_ATK = 30; pk.IV_DEF = 29; pk.IV_SPA = 28; pk.IV_SPD = 27; pk.IV_SPE = 26;
            pk.EV_HP = 4; pk.EV_ATK = 252; pk.EV_DEF = 0; pk.EV_SPA = 0; pk.EV_SPD = 0; pk.EV_SPE = 252;
            pk.Move1 = 85; // Thunderbolt
            pk.Move2 = 129; // Swift
            pk.Move3 = 98; // Quick Attack
            pk.Move4 = 0;
        });
        var stored = await _vault.DepositAsync(save.Id, box: 0, slot: 3);

        var detail = await _vault.GetStoredPokemonAsync(stored.Id);

        Assert.Equal(stored.Id, detail.Id);
        Assert.Equal(stored.BoxId, detail.BoxId);
        Assert.Equal("Vault 1", detail.BoxName);
        Assert.Equal(stored.Slot, detail.Slot);
        Assert.Equal(25, detail.Species);
        Assert.Equal("Pika", detail.Nickname);
        Assert.Equal("TEST", detail.OTName);
        Assert.Equal(GameInfo.GetVersionName(GameVersion.B), detail.OriginGame);
        Assert.Equal(stored.HomeTracker, detail.HomeTracker);
        Assert.Equal(stored.DepositedAt, detail.DepositedAt);

        Assert.Equal(new StatSet(31, 30, 29, 28, 27, 26), detail.IVs);
        Assert.Equal(new StatSet(4, 252, 0, 0, 0, 252), detail.EVs);

        Assert.Equal(4, detail.Moves.Count);
        Assert.Equal([85, 129, 98, 0], detail.Moves.Select(m => m.Id).ToArray());
        var names = GameInfo.Strings.movelist;
        Assert.Equal(names[85], detail.Moves[0].Name);
        Assert.Equal(names[129], detail.Moves[1].Name);
        Assert.Equal(names[98], detail.Moves[2].Name);
    }

    [Fact]
    public async Task GetStoredPokemon_UnknownId_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _vault.GetStoredPokemonAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListStoredPokemon_EmptyVault_ReturnsEmpty()
    {
        Assert.Empty(await _vault.ListStoredPokemonAsync());
    }
}
