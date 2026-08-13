using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

/// <summary>
/// Vault query endpoint seam: filters over the denormalized columns, the lazily
/// computed legality filter, and sorting. Same temp data root +
/// <see cref="BlankSaveFile"/> pattern as <see cref="VaultReadTests"/>.
/// </summary>
public class VaultQueryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}");
    private readonly OpenHomeOptions _options;
    private readonly OpenHomeDbContext _db;
    private readonly BackupService _backups;
    private readonly SaveLibraryService _library;
    private readonly VaultService _vault;

    public VaultQueryTests()
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

    private async Task<RegisteredSaveSummary> ImportSaveAsync(GameVersion version, string ot, ushort species, string nickname, Action<PKM>? customize = null)
    {
        var path = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.sav");
        var sav = BlankSaveFile.Get(version, ot);
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
            return await _library.RegisterAsync(path, $"{version}-{ot}.sav");
        }
        finally
        {
            File.Delete(path);
        }
    }

    /// <summary>Registers a blank Black save holding <paramref name="pk"/> at box 0 slot 3.</summary>
    private async Task<RegisteredSaveSummary> ImportPokemonAsync(string ot, PKM pk)
    {
        var path = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.sav");
        var sav = BlankSaveFile.Get(GameVersion.B, ot);
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

    /// <summary>Generates a genuinely legal entity from a real egg encounter template.</summary>
    private static PKM GenerateLegal(SaveFile sav, Species species)
    {
        var seed = sav.BlankPKM;
        seed.Species = (ushort)species;
        var encounter = EncounterMovesetGenerator.GenerateEncounters(seed, seed.Moves, [GameVersion.B]).First();
        return encounter.ConvertToPKM(sav);
    }

    private async Task<StoredPokemonSummary> DepositAsync(GameVersion version, string ot, ushort species, string nickname, Action<PKM>? customize = null)
    {
        var save = await ImportSaveAsync(version, ot, species, nickname, customize);
        return await _vault.DepositAsync(save.Id, box: 0, slot: 3);
    }

    [Fact]
    public async Task Query_NoFilters_ReturnsAll_InBoxOrder()
    {
        var first = await DepositAsync(GameVersion.B, "FIRST", 25, "Pika");
        var second = await DepositAsync(GameVersion.B, "SECOND", 133, "Eevee");

        var results = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter());

        Assert.Equal([first.Id, second.Id], results.Select(r => r.Id).ToArray());
        Assert.All(results, r => Assert.NotNull(r.LegalityValid));
    }

    [Fact]
    public async Task Query_FiltersBySpeciesLevelAndShiny()
    {
        await DepositAsync(GameVersion.B, "ONE", 25, "Pika", pk => pk.CurrentLevel = 5);
        var target = await DepositAsync(GameVersion.B, "TWO", 133, "Eevee", pk =>
        {
            pk.CurrentLevel = 42;
            pk.SetShiny();
        });
        await DepositAsync(GameVersion.B, "THREE", 133, "Vapo", pk => pk.CurrentLevel = 100);

        var bySpecies = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(Species: 133));
        Assert.Equal(2, bySpecies.Count);
        Assert.All(bySpecies, r => Assert.Equal(133, r.Species));

        var byLevel = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(MinLevel: 10, MaxLevel: 50));
        Assert.Equal([target.Id], byLevel.Select(r => r.Id).ToArray());

        var byShiny = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(Shiny: true));
        Assert.Equal([target.Id], byShiny.Select(r => r.Id).ToArray());
        var notShiny = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(Shiny: false));
        Assert.Equal(2, notShiny.Count);
    }

    [Fact]
    public async Task Query_FiltersByOriginGame_CaseInsensitive()
    {
        var black = await DepositAsync(GameVersion.B, "ONE", 25, "Pika");
        await DepositAsync(GameVersion.BD, "TWO", 133, "Eevee");

        var results = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(OriginGame: "black"));

        Assert.Equal([black.Id], results.Select(r => r.Id).ToArray());
        Assert.Equal(GameInfo.GetVersionName(GameVersion.B), results[0].OriginGame);
    }

    [Fact]
    public async Task Query_Search_MatchesNicknameAndOt_CaseInsensitive()
    {
        var pika = await DepositAsync(GameVersion.B, "FIRST", 25, "Pika");
        var eevee = await DepositAsync(GameVersion.B, "SECOND", 133, "Eevee");

        var byNickname = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(Search: "pik"));
        Assert.Equal([pika.Id], byNickname.Select(r => r.Id).ToArray());

        var byOt = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(Search: "econd"));
        Assert.Equal([eevee.Id], byOt.Select(r => r.Id).ToArray());
    }

    [Fact]
    public async Task Query_FiltersByLegality_ValidAndInvalid()
    {
        var savLegal = BlankSaveFile.Get(GameVersion.B, "CLEAN");
        var legalSave = await ImportPokemonAsync("CLEAN", GenerateLegal(savLegal, Species.Pikachu));
        var savBad = BlankSaveFile.Get(GameVersion.B, "HACKD");
        var corrupted = GenerateLegal(savBad, Species.Pikachu);
        corrupted.Ball = (byte)Ball.Master; // an egg cannot hatch into a Master Ball
        var badSave = await ImportPokemonAsync("HACKD", corrupted);
        var clean = await _vault.DepositAsync(legalSave.Id, box: 0, slot: 3);
        var bad = await _vault.DepositAsync(badSave.Id, box: 0, slot: 3);

        var valid = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(Legality: "valid"));
        Assert.Equal([clean.Id], valid.Select(r => r.Id).ToArray());
        Assert.True(valid[0].LegalityValid);

        var invalid = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(Legality: "INVALID"));
        Assert.Equal([bad.Id], invalid.Select(r => r.Id).ToArray());
        Assert.False(invalid[0].LegalityValid);
    }

    [Fact]
    public async Task Query_Sorts_ByAnyDenormalizedColumn()
    {
        await DepositAsync(GameVersion.B, "ONE", 25, "Charlie", pk => pk.CurrentLevel = 50);
        await DepositAsync(GameVersion.B, "TWO", 133, "Alpha", pk => pk.CurrentLevel = 5);
        await DepositAsync(GameVersion.B, "THREE", 1, "Bravo", pk => pk.CurrentLevel = 25);

        var byLevelDesc = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(SortBy: "level", SortDescending: true));
        Assert.Equal([50, 25, 5], byLevelDesc.Select(r => r.Level).ToArray());

        var byLevelAsc = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(SortBy: "level"));
        Assert.Equal([5, 25, 50], byLevelAsc.Select(r => r.Level).ToArray());

        var byNickname = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(SortBy: "nickname"));
        Assert.Equal(["Alpha", "Bravo", "Charlie"], byNickname.Select(r => r.Nickname).ToArray());

        var bySpecies = await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(SortBy: "species"));
        Assert.Equal([1, 25, 133], bySpecies.Select(r => r.Species).ToArray());
    }

    [Fact]
    public async Task Query_UnknownSortOrLegality_Throws()
    {
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _vault.QueryStoredPokemonAsync(new VaultQueryFilter(SortBy: "height")));
        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            _vault.QueryStoredPokemonAsync(new VaultQueryFilter(Legality: "fishy")));
    }

    [Fact]
    public async Task Query_EmptyVault_ReturnsEmpty()
    {
        Assert.Empty(await _vault.QueryStoredPokemonAsync(new VaultQueryFilter(Species: 25)));
    }
}
