using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core;

/// <summary>
/// Local trades between two registered saves: swaps the Pokémon occupying two save
/// slots and applies trade evolution on receipt (Kadabra → Alakazam, Onix + Metal
/// Coat → Steelix, …) using PKHeX's evolution data for the receiving save's game.
/// A trade between two slots of the same save counts too — self-trade evolution is
/// a core fan request. Both saves are snapshotted before anything is written.
/// </summary>
public sealed class TradeService(
    SaveLibraryService library,
    BackupService backups)
{
    /// <summary>
    /// Swaps the Pokémon at (boxA, slotA) of save A with the one at (boxB, slotB) of
    /// save B, evolving either side on receipt when the destination game has a trade
    /// evolution for it. Returns both sides as they stand after the swap.
    /// </summary>
    public async Task<TradeReport> TradeAsync(
        Guid saveAId, int boxA, int slotA,
        Guid saveBId, int boxB, int slotB,
        CancellationToken ct = default)
    {
        var recordA = await library.GetAsync(saveAId, ct);
        var savA = await library.LoadAsync(recordA, ct);

        var sameSave = saveAId == saveBId;
        var recordB = sameSave ? recordA : await library.GetAsync(saveBId, ct);
        var savB = sameSave ? savA : await library.LoadAsync(recordB, ct);

        if (sameSave && boxA == boxB && slotA == slotB)
            throw new InvalidOperationException("Pick two different slots to trade.");
        ValidateSlot(savA, boxA, slotA);
        ValidateSlot(savB, boxB, slotB);

        var pkA = savA.GetBoxSlotAtIndex(boxA, slotA);
        var pkB = savB.GetBoxSlotAtIndex(boxB, slotB);
        if (pkA.Species == 0)
            throw new InvalidOperationException($"Box {boxA} slot {slotA} is empty — nothing to trade.");
        if (pkB.Species == 0)
            throw new InvalidOperationException($"Box {boxB} slot {slotB} is empty — nothing to trade.");

        // A box slot stores raw bytes in the save's own entity format; writing a
        // foreign entity would corrupt it. Same-generation saves share the format.
        if (savA.BlankPKM.GetType() != savB.BlankPKM.GetType())
            throw new InvalidOperationException(
                $"Trades require both saves to use the same entity format ({recordA.Game} uses " +
                $"{savA.BlankPKM.GetType().Name}, {recordB.Game} uses {savB.BlankPKM.GetType().Name}) — " +
                "move Pokémon across generations through the vault instead.");

        // Capture what each side sends before any evolution mutates the entities.
        var sentByA = pkA.Species;
        var sentByB = pkB.Species;

        backups.Snapshot(recordA);
        if (!sameSave)
            backups.Snapshot(recordB);

        // Trade evolution happens on receipt, judged against the destination save.
        var evolvedA = TryTradeEvolve(savA, pkB, sentByA, out var fromA); // save A receives B's Pokémon
        var evolvedB = TryTradeEvolve(savB, pkA, sentByB, out var fromB);

        savA.SetBoxSlotAtIndex(pkB, boxA, slotA);
        savB.SetBoxSlotAtIndex(pkA, boxB, slotB);

        await library.PersistAsync(recordA, savA, ct);
        if (!sameSave)
            await library.PersistAsync(recordB, savB, ct);

        return new TradeReport(
            ToResult(recordA, boxA, slotA, pkB, evolvedA, fromA),
            ToResult(recordB, boxB, slotB, pkA, evolvedB, fromB));
    }

    /// <summary>
    /// Applies trade evolution to a freshly received Pokémon, judged against the
    /// destination save's evolution data: only <see cref="EvolutionType.Trade"/>-family
    /// methods trigger, held-item requirements must be met, Karrablast ↔ Shelmet
    /// only evolve when traded for each other, and the evolved species must exist
    /// in the receiving game. Returns whether the entity evolved.
    /// </summary>
    private static bool TryTradeEvolve(SaveFile dest, PKM received, ushort partnerSpecies, out ushort fromSpecies)
    {
        fromSpecies = received.Species;
        var tree = EvolutionTree.GetEvolutionTree(dest.Context);
        var forward = ((IEvolutionNetwork)tree).Forward;
        foreach (var method in forward.GetForward(received.Species, received.Form).Span)
        {
            var applies = method.Method switch
            {
                EvolutionType.Trade => true,
                EvolutionType.TradeHeldItem => received.HeldItem == method.Argument,
                EvolutionType.TradeShelmetKarrablast => partnerSpecies is 588 or 616 && partnerSpecies != received.Species,
                _ => false,
            };
            if (!applies)
                continue;

            var destForm = method.GetDestinationForm(received.Form);
            if (!dest.Personal.IsPresentInGame(method.Species, destForm))
                continue; // the receiving game doesn't contain the evolved species

            received.Species = method.Species;
            received.Form = destForm;
            received.RefreshChecksum();
            return true;
        }
        return false;
    }

    private static TradeSlotResult ToResult(SaveFileRecord record, int box, int slot, PKM pk, bool evolved, ushort fromSpecies)
    {
        var names = GameInfo.Strings.specieslist;
        return new TradeSlotResult(
            record.Id,
            box,
            slot,
            pk.Species,
            pk.Form,
            Sanitize(pk.Nickname),
            pk.CurrentLevel,
            pk.IsShiny,
            pk.Species < names.Length ? names[pk.Species] : "",
            evolved,
            evolved ? fromSpecies : 0,
            evolved && fromSpecies < names.Length ? names[fromSpecies] : null);
    }

    private static void ValidateSlot(SaveFile sav, int box, int slot)
    {
        if ((uint)box >= (uint)sav.BoxCount)
            throw new InvalidOperationException($"Box must be between 0 and {sav.BoxCount - 1}.");
        if ((uint)slot >= (uint)sav.BoxSlotCount)
            throw new InvalidOperationException($"Slot must be between 0 and {sav.BoxSlotCount - 1}.");
    }

    /// <summary>Gen 1-5 string terminators can leak into the managed string — strip them.</summary>
    private static string Sanitize(string value) => value.TrimEnd('￿', '\0');
}
