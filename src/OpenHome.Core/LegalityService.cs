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
