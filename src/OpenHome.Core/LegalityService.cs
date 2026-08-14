using Microsoft.EntityFrameworkCore;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core;

/// <summary>
/// Transparent legality reports: wraps PKHeX's <see cref="LegalityAnalysis"/> and
/// exposes the full per-check report for any stored Pokémon. Legality is always
/// shown, never enforced — nothing in the vault consults this service before a
/// deposit, withdraw, or move.
/// </summary>
public sealed class LegalityService(OpenHomeDbContext db)
{
    /// <summary>Localization language for check messages (matches the rest of the UI).</summary>
    private const string Language = "en";

    /// <summary>
    /// Runs the full PKHeX legality analysis on an entity and returns every check.
    /// This is the testable seam: no database, no I/O.
    /// </summary>
    public LegalityReport Analyze(PKM pk)
    {
        var analysis = new LegalityAnalysis(pk);
        var localization = LegalityLocalizationContext.Create(analysis, Language);
        // LegalityLocalizationContext is a ref struct — no LINQ/lambda capture.
        var checks = new List<LegalityCheckItem>(analysis.Results.Count);
        foreach (var r in analysis.Results)
        {
            checks.Add(new LegalityCheckItem(
                r.Identifier.ToString(),
                r.Judgement.ToString(),
                r.Valid,
                localization.Humanize(in r, verbose: false)));
        }
        return new LegalityReport(analysis.Valid, analysis.Parsed, checks);
    }

    /// <summary>
    /// The legality verdict for serialized PKH bytes, or null when analysis is
    /// unavailable (corrupt bytes, unsupported container). Used for grid badges.
    /// </summary>
    public bool? IsStoredDataValid(byte[] data)
    {
        try
        {
            var projected = Project(new PKH(data));
            return projected is null ? null : Analyze(projected).Valid;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Runs the full legality report for one stored Pokémon.</summary>
    public async Task<LegalityReport> AnalyzeStoredAsync(Guid storedPokemonId, CancellationToken ct = default)
    {
        var stored = await db.StoredPokemon.FirstOrDefaultAsync(p => p.Id == storedPokemonId, ct)
            ?? throw new KeyNotFoundException($"No stored Pokémon with id {storedPokemonId}.");
        var projected = Project(new PKH(stored.Data))
            ?? throw new InvalidDataException("The stored Pokémon could not be projected into an analyzable format.");
        return Analyze(projected);
    }

    /// <summary>
    /// Transfer-legality checks for moving a stored Pokémon into a target save: the
    /// two HOME-parity rules strict mode enforces. The species must be present in the
    /// target game's Personal table, and an entity cannot move backwards to a
    /// generation older than the game its side data currently lives in. The full
    /// <see cref="LegalityAnalysis"/> verdict is deliberately NOT the gate: transferred
    /// veterans, event Pokémon and fangame origins routinely fail checks that say
    /// nothing about whether the target game can actually hold the entity.
    /// </summary>
    public IReadOnlyList<string> CheckTransfer(PKH pkh, SaveFile sav, string gameName)
    {
        var warnings = new List<string>();
        var generation = CurrentGeneration(pkh);
        if (generation > sav.Generation)
        {
            warnings.Add(
                $"{SpeciesName(pkh.Species)} lives in a generation {generation} game and cannot enter " +
                $"{gameName} (generation {sav.Generation}) — transfers never go backwards.");
            return warnings;
        }

        var converted = TryConvertForSave(pkh, sav);
        if (converted is null)
        {
            warnings.Add($"No transfer route into {gameName} ({sav.BlankPKM.GetType().Name}).");
            return warnings;
        }
        if (!sav.Personal.IsPresentInGame(converted.Species, converted.Form) &&
            !sav.Personal.IsPresentInGame(converted.Species, 0))
        {
            warnings.Add($"{SpeciesName(converted.Species)} is not present in {gameName}.");
        }
        return warnings;
    }

    /// <summary>
    /// The generation of the game whose side data is current on the stored entity
    /// (<c>LatestGameData</c>). Unknown newer side data is treated as the newest
    /// generation — the conservative choice for the backwards-transfer check.
    /// </summary>
    private static int CurrentGeneration(PKH pkh) => pkh.LatestGameData switch
    {
        null => 0,
        GameDataPB7 => 7,
        GameDataPK8 or GameDataPB8 or GameDataPA8 => 8,
        GameDataPK9 or GameDataPA9 => 9,
        _ => 9,
    };

    /// <summary>
    /// Mirrors <see cref="VaultService"/>'s withdraw conversion, returning null
    /// instead of throwing so transfer checks can report rather than fail.
    /// </summary>
    private static PKM? TryConvertForSave(PKH pkh, SaveFile sav)
    {
        var target = sav.BlankPKM;
        try
        {
            return target switch
            {
                PK8 => pkh.ConvertToPK8(),
                PB8 => pkh.ConvertToPB8(),
                PA8 => pkh.ConvertToPA8(),
                PK9 => pkh.ConvertToPK9(),
                PA9 => pkh.ConvertToPA9(),
                PB7 => pkh.ConvertToPB7(),
                _ => EntityConverter.ConvertToType(pkh, target.GetType(), out _),
            };
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>English species name for a national dex id, bounds-checked.</summary>
    private static string SpeciesName(int species)
    {
        var names = GameInfo.Strings.specieslist;
        return species > 0 && species < names.Length ? names[species] : $"Species #{species}";
    }

    /// <summary>
    /// LegalityAnalysis has no PKH parse path (PKHeX 26.7.7 reports "Internal error"
    /// on raw PKH), so the stored container is projected back into the concrete
    /// entity format of the game whose side data is current (<c>LatestGameData</c>).
    /// Gen ≤5 deposits are stored as PKH upgraded through PK8, so the report
    /// honestly reflects the post-transfer entity, not the pre-transfer original.
    /// </summary>
    private static PKM? Project(PKH pkh)
    {
        PKM? projected = pkh.LatestGameData switch
        {
            GameDataPB7 => pkh.ConvertToPB7(),
            GameDataPK8 => pkh.ConvertToPK8(),
            GameDataPB8 => pkh.ConvertToPB8(),
            GameDataPA8 => pkh.ConvertToPA8(),
            GameDataPK9 => pkh.ConvertToPK9(),
            GameDataPA9 => pkh.ConvertToPA9(),
            _ => null,
        };
        if (projected is not null)
            return projected;
        // Fallback for side data this switch doesn't know (e.g. PC9 from Legends Z-A).
        return (PKM?)pkh.ConvertToPK9() ?? pkh.ConvertToPK8();
    }
}
