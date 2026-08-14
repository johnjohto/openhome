using System.Reflection;
using Microsoft.EntityFrameworkCore;
using OpenHome.Core;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core.Tests;

/// <summary>
/// Dex progress at the service seam: national dex from vault contents, per-save
/// dex from the save's own seen/caught data. Same temp data root +
/// <see cref="BlankSaveFile"/> pattern as <see cref="VaultReadTests"/>.
/// </summary>
public class DexServiceTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"openhome-test-{Guid.NewGuid():N}");
    private readonly OpenHomeOptions _options;
    private readonly OpenHomeDbContext _db;
    private readonly BackupService _backups;
    private readonly SaveLibraryService _library;
    private readonly VaultService _vault;
    private readonly DexService _dex;

    public DexServiceTests()
    {
        _options = new OpenHomeOptions(_root);
        _options.EnsureDirectories();
        _db = OpenHomeDbContext.Create(_options.DatabasePath);
        _db.Database.EnsureCreated();
        _backups = new BackupService(_options);
        _library = new SaveLibraryService(_db, new SaveFileService(), _backups, _options);
        _vault = new VaultService(_db, _library, _backups, new LegalityService(_db), _options);
        _dex = new DexService(_db, _library);
    }

    public void Dispose()
    {
        _db.Dispose();
        try { Directory.Delete(_root, recursive: true); }
        catch (IOException) { /* SQLite may hold the file briefly on Windows */ }
    }

    /// <summary>
    /// Fabricates a Black save with the given box occupants and registers it.
    /// <paramref name="customizeSave"/> runs against the loaded save before it is
    /// written (e.g. to mark dex flags).
    /// </summary>
    private async Task<RegisteredSaveSummary> ImportSaveAsync(
        string ot,
        IReadOnlyList<(int Slot, ushort Species, Action<PKM>? Customize)> occupants,
        Action<SaveFile>? customizeSave = null)
    {
        var path = Path.Combine(_root, $"upload-{Guid.NewGuid():N}.sav");
        var sav = BlankSaveFile.Get(GameVersion.B, ot);
        foreach (var (slot, species, customize) in occupants)
        {
            var pk = sav.BlankPKM;
            pk.Species = species;
            pk.Nickname = $"Mon{species}";
            // BlankPKM starts with empty OT, Language 0 and PID 0 (which reads shiny against a blank trainer) — give it all three.
            pk.OriginalTrainerName = ot;
            pk.Language = (int)LanguageID.English;
            pk.PID = 0x12345678;
            customize?.Invoke(pk);
            sav.SetBoxSlotAtIndex(pk, 0, slot);
        }
        customizeSave?.Invoke(sav);
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

    /// <summary>
    /// Marks a species seen+caught in the save's Pokédex. The public
    /// SetSeen/SetCaught are no-ops on a blank (never-initialized) save — only the
    /// non-public SetDex(PKM) populates the dex block, so tests reach it by
    /// reflection. Real gameplay saves have an initialized dex and need none of this.
    /// </summary>
    private static void MarkDex(SaveFile sav, ushort species)
    {
        var pk = sav.BlankPKM;
        pk.Species = species;
        pk.OriginalTrainerName = "TEST";
        pk.Language = (int)LanguageID.English;
        pk.PID = 0x12345678;
        var setDex = sav.GetType().GetMethod(
            "SetDex", BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.Instance, [typeof(PKM)])
            ?? throw new InvalidOperationException("PKHeX SetDex(PKM) not found.");
        setDex.Invoke(sav, [pk]);
    }

    private static void MakeShiny(PKM pk)
    {
        while (!pk.IsShiny)
            pk.PID++;
    }

    [Fact]
    public async Task NationalDex_CountsDistinctSpecies_ShinyAndFormsTrackedSeparately()
    {
        var save = await ImportSaveAsync("TEST", [
            (0, (ushort)25, null),                                  // Pikachu
            (1, (ushort)25, null),                                  // duplicate Pikachu — counts once
            (2, (ushort)25, (Action<PKM>?)MakeShiny),               // shiny Pikachu — shiny tracked separately
            (3, (ushort)201, pk => pk.Form = 1),                    // Unown form 1
            (4, (ushort)201, pk => pk.Form = 2),                    // Unown form 2 — same species, second form
            (5, (ushort)133, null),                                 // Eevee
        ]);
        await _vault.DepositManyAsync(save.Id, Enumerable.Range(0, 6).Select(s => new BoxSlotRef(0, s)).ToList());

        var dex = await _dex.GetNationalDexAsync();

        Assert.Equal(DexService.NationalSpeciesCount, dex.Total);
        Assert.Equal(dex.Total, dex.Species.Count);
        Assert.Equal(3, dex.Owned); // 25, 201, 133
        Assert.Equal(1, dex.ShinyOwned); // only Pikachu

        var pikachu = dex.Species[24];
        Assert.Equal(25, pikachu.Species);
        Assert.True(pikachu.Owned);
        Assert.True(pikachu.ShinyOwned);
        Assert.Equal([0], pikachu.OwnedForms);

        var unown = dex.Species[200];
        Assert.True(unown.Owned);
        Assert.False(unown.ShinyOwned);
        Assert.Equal([1, 2], unown.OwnedForms);

        var eevee = dex.Species[132];
        Assert.True(eevee.Owned);

        var bulbasaur = dex.Species[0];
        Assert.False(bulbasaur.Owned);
        Assert.False(bulbasaur.ShinyOwned);
        Assert.Empty(bulbasaur.OwnedForms);

        Assert.Equal("Bulbasaur", dex.Species[0].Name);
        Assert.Equal("Pikachu", pikachu.Name);
    }

    [Fact]
    public async Task NationalDex_EmptyVault_NothingOwned()
    {
        var dex = await _dex.GetNationalDexAsync();

        Assert.Equal(DexService.NationalSpeciesCount, dex.Total);
        Assert.Equal(0, dex.Owned);
        Assert.Equal(0, dex.ShinyOwned);
        Assert.All(dex.Species, s => Assert.False(s.Owned));
    }

    [Fact]
    public async Task SaveDex_UsesSaveSeenCaughtData_NotBoxContents()
    {
        // Species 1 and 4 are dexed but never placed in a box — only the dex-data
        // path can report them.
        var save = await ImportSaveAsync("TEST", [], sav =>
        {
            MarkDex(sav, 1);
            MarkDex(sav, 4);
        });

        var progress = await _dex.GetSaveDexAsync(save.Id);

        Assert.True(progress.UsesSaveDexData);
        Assert.Equal(Math.Min(649, DexService.NationalSpeciesCount), progress.Total); // Black's species range
        Assert.Equal([1, 4], progress.SeenSpecies);
        Assert.Equal([1, 4], progress.CaughtSpecies);
        Assert.Equal(2, progress.Seen);
        Assert.Equal(2, progress.Caught);
        Assert.Equal(save.Id, progress.SaveId);
        Assert.Equal("TEST", progress.TrainerName);
    }

    [Fact]
    public async Task SaveDex_BoxPlacementRegistersInDex()
    {
        // PKHeX's SetBoxSlotAtIndex (default EntityImportSettings) marks a placed
        // Pokémon seen+caught in the save's dex automatically.
        var save = await ImportSaveAsync("TEST", [(3, (ushort)25, null)]);

        var progress = await _dex.GetSaveDexAsync(save.Id);

        Assert.True(progress.UsesSaveDexData);
        Assert.Equal([25], progress.SeenSpecies);
        Assert.Equal([25], progress.CaughtSpecies);
    }

    [Fact]
    public async Task SaveDex_EmptyDex_ReportsZero()
    {
        var save = await ImportSaveAsync("TEST", []);

        var progress = await _dex.GetSaveDexAsync(save.Id);

        Assert.True(progress.UsesSaveDexData);
        Assert.Equal(0, progress.Seen);
        Assert.Equal(0, progress.Caught);
        Assert.Empty(progress.SeenSpecies);
        Assert.Empty(progress.CaughtSpecies);
    }

    [Fact]
    public async Task SaveDex_UnknownId_ThrowsKeyNotFound()
    {
        await Assert.ThrowsAsync<KeyNotFoundException>(() => _dex.GetSaveDexAsync(Guid.NewGuid()));
    }
}
