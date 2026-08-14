using OpenHome.Core;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

/// <summary>
/// Legality reports: the pure <see cref="LegalityService.Analyze"/> seam with
/// generated fixtures, plus the stored round-trip (deposit a legal and a corrupted
/// entity, read the report back out of the vault). Same temp data root +
/// <see cref="BlankSaveFile"/> pattern as <see cref="VaultServiceTests"/>.
/// </summary>
public class LegalityServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}");
    private readonly OpenHomeOptions _options;
    private readonly OpenHomeDbContext _db;
    private readonly BackupService _backups;
    private readonly SaveLibraryService _library;
    private readonly LegalityService _legality;
    private readonly VaultService _vault;

    public LegalityServiceTests()
    {
        _options = new OpenHomeOptions(_root);
        _options.EnsureDirectories();
        _db = OpenHomeDbContext.Create(_options.DatabasePath);
        _db.Database.EnsureCreated();
        _backups = new BackupService(_options);
        _library = new SaveLibraryService(_db, new SaveFileService(), _backups, _options);
        _legality = new LegalityService(_db);
        _vault = new VaultService(_db, _library, _backups, _legality, _options);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* SQLite may hold the file briefly on Windows */ }
    }

    /// <summary>
    /// Generates a genuinely legal entity by materializing a real encounter template
    /// (an egg) against a blank Black save, instead of hand-editing a BlankPKM.
    /// </summary>
    private static PKM GenerateLegal(SaveFile sav, Species species)
    {
        var seed = sav.BlankPKM;
        seed.Species = (ushort)species;
        var encounter = EncounterMovesetGenerator.GenerateEncounters(seed, seed.Moves, [GameVersion.B]).First();
        return encounter.ConvertToPKM(sav);
    }

    /// <summary>Registers a blank Black save holding <paramref name="pk"/> at box 0 slot 3.</summary>
    private async Task<RegisteredSaveSummary> ImportSaveAsync(string ot, PKM pk)
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

    [Fact]
    public void Analyze_CleanGenerated_ReportsValidAndParsed()
    {
        var sav = BlankSaveFile.Get(GameVersion.B, "TEST");
        var pk = GenerateLegal(sav, Species.Pikachu);

        var report = _legality.Analyze(pk);

        Assert.True(report.Valid);
        Assert.True(report.Parsed);
        Assert.NotEmpty(report.Checks);
        Assert.All(report.Checks, c => Assert.False(c.Severity == "Invalid", $"{c.Identifier}: {c.Message}"));
    }

    [Fact]
    public void Analyze_CorruptedBall_ReportsInvalidWithFailingChecks()
    {
        var sav = BlankSaveFile.Get(GameVersion.B, "TEST");
        var pk = GenerateLegal(sav, Species.Pikachu);
        pk.Ball = (byte)Ball.Master; // an egg cannot hatch into a Master Ball

        var report = _legality.Analyze(pk);

        Assert.False(report.Valid);
        Assert.True(report.Parsed);
        var failing = report.Checks.Where(c => !c.Valid).ToList();
        Assert.NotEmpty(failing);
        Assert.Contains(failing, c => c.Identifier == "Ball" && c.Severity == "Invalid" && c.Message.Length > 0);
    }

    [Fact]
    public async Task AnalyzeStored_DepositedLegalEntity_ReportsValidAndParsed()
    {
        var sav = BlankSaveFile.Get(GameVersion.B, "TEST");
        var save = await ImportSaveAsync("TEST", GenerateLegal(sav, Species.Pikachu));
        // Legality never gates deposit — this must succeed regardless of verdict.
        var stored = await _vault.DepositAsync(save.Id, box: 0, slot: 3);

        var report = await _legality.AnalyzeStoredAsync(stored.Id);

        Assert.True(report.Parsed);
        Assert.True(report.Valid, string.Join("; ", report.Checks.Where(c => !c.Valid).Select(c => $"{c.Identifier}: {c.Message}")));
        Assert.NotEmpty(report.Checks);
    }

    [Fact]
    public async Task AnalyzeStored_DepositedCorruptedEntity_ReportsInvalid()
    {
        var sav = BlankSaveFile.Get(GameVersion.B, "TEST");
        var pk = GenerateLegal(sav, Species.Pikachu);
        pk.Ball = (byte)Ball.Master;
        var save = await ImportSaveAsync("TEST", pk);
        var stored = await _vault.DepositAsync(save.Id, box: 0, slot: 3);

        var report = await _legality.AnalyzeStoredAsync(stored.Id);

        Assert.True(report.Parsed);
        Assert.False(report.Valid);
        Assert.Contains(report.Checks, c => !c.Valid && c.Severity == "Invalid");
    }

    [Fact]
    public async Task AnalyzeStored_UnknownId_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _legality.AnalyzeStoredAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task ListVaultBoxes_OccupiedSlots_CarryLegalityBadge()
    {
        var savLegal = BlankSaveFile.Get(GameVersion.B, "CLEAN");
        var legalSave = await ImportSaveAsync("CLEAN", GenerateLegal(savLegal, Species.Pikachu));
        var savBad = BlankSaveFile.Get(GameVersion.B, "HACKD");
        var corrupted = GenerateLegal(savBad, Species.Pikachu);
        corrupted.Ball = (byte)Ball.Master;
        var badSave = await ImportSaveAsync("HACKD", corrupted);
        var clean = await _vault.DepositAsync(legalSave.Id, box: 0, slot: 3);
        var bad = await _vault.DepositAsync(badSave.Id, box: 0, slot: 3);

        var boxes = await _vault.ListVaultBoxesAsync();
        var slots = boxes.SelectMany(b => b.Slots).ToList();
        var cleanSlot = slots.Single(s => s.StoredPokemonId == clean.Id);
        var badSlot = slots.Single(s => s.StoredPokemonId == bad.Id);

        Assert.True(cleanSlot.LegalityValid);
        Assert.False(badSlot.LegalityValid);
        Assert.All(slots.Where(s => s.IsEmpty), s => Assert.Null(s.LegalityValid));
    }
}
