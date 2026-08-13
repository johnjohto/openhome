using Microsoft.EntityFrameworkCore;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core;

/// <summary>
/// Living-dex progress: the national dex computed from current vault contents,
/// and per-save dex progress for each registered save. Species names and the
/// national total come from PKHeX's bundled string list (index 0 is the "---"
/// placeholder, so the last valid species id is <c>Count - 1</c>).
/// </summary>
public sealed class DexService(OpenHomeDbContext db, SaveLibraryService library)
{
    /// <summary>Highest valid national species id for the pinned PKHeX version.</summary>
    public static int NationalSpeciesCount => GameInfo.Strings.specieslist.Count() - 1;

    /// <summary>
    /// National dex progress from the vault: one entry per species id (owned or
    /// not), with shiny ownership and owned forms tracked separately per species.
    /// Reads only the denormalized vault columns — no PKH deserialization.
    /// </summary>
    public async Task<NationalDexProgress> GetNationalDexAsync(CancellationToken ct = default)
    {
        var stored = await db.StoredPokemon
            .Select(p => new { p.Species, p.Form, p.IsShiny })
            .ToListAsync(ct);

        var names = GameInfo.Strings.specieslist;
        var total = NationalSpeciesCount;
        var bySpecies = stored
            .Where(p => p.Species >= 1 && p.Species <= total)
            .GroupBy(p => p.Species)
            .ToDictionary(g => g.Key, g => g.ToList());

        var species = new List<DexSpeciesProgress>(total);
        var owned = 0;
        var shinyOwned = 0;
        for (var id = 1; id <= total; id++)
        {
            if (bySpecies.TryGetValue(id, out var rows))
            {
                var forms = rows.Select(r => r.Form).Distinct().Order().ToList();
                var shiny = rows.Any(r => r.IsShiny);
                species.Add(new DexSpeciesProgress(id, names[id], true, shiny, forms));
                owned++;
                if (shiny)
                    shinyOwned++;
            }
            else
            {
                species.Add(new DexSpeciesProgress(id, names[id], false, false, []));
            }
        }
        return new NationalDexProgress(total, owned, shinyOwned, species);
    }

    /// <summary>
    /// Per-save dex progress. When the save has a Pokédex (<c>SaveFile.HasPokeDex</c>),
    /// seen/caught come from the save's own dex data via the cross-version
    /// <c>GetSeen</c>/<c>GetCaught</c> API, capped at the save's
    /// <c>MaxSpeciesID</c> (i.e. that game's regional range). Saves without a dex
    /// (e.g. Colosseum/XD) fall back to species present in the save's boxes —
    /// <see cref="SaveDexProgress.UsesSaveDexData"/> reports which path was taken.
    /// </summary>
    public async Task<SaveDexProgress> GetSaveDexAsync(Guid saveId, CancellationToken ct = default)
    {
        var record = await library.GetAsync(saveId, ct);
        var sav = await library.LoadAsync(record, ct);

        HashSet<int> seen;
        HashSet<int> caught;
        int total;
        var usesSaveDexData = sav.HasPokeDex;
        if (usesSaveDexData)
        {
            total = Math.Min(sav.MaxSpeciesID, NationalSpeciesCount);
            seen = [];
            caught = [];
            for (var id = 1; id <= total; id++)
            {
                if (sav.GetSeen((ushort)id))
                    seen.Add(id);
                if (sav.GetCaught((ushort)id))
                    caught.Add(id);
            }
        }
        else
        {
            // No dex data on this save format — fall back to box contents.
            total = NationalSpeciesCount;
            caught = [];
            for (var box = 0; box < sav.BoxCount; box++)
            for (var slot = 0; slot < sav.BoxSlotCount; slot++)
            {
                var species = sav.GetBoxSlotAtIndex(box, slot).Species;
                if (species >= 1 && species <= total)
                    caught.Add(species);
            }
            seen = [.. caught];
        }

        return new SaveDexProgress(
            record.Id, record.Game, record.TrainerName, usesSaveDexData,
            total, seen.Count, caught.Count,
            seen.Order().ToList(), caught.Order().ToList());
    }
}
