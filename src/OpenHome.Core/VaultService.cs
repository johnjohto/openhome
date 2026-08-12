using Microsoft.EntityFrameworkCore;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core;

/// <summary>
/// The OpenHome vault: deposit Pokémon from registered saves into cloud-style
/// storage (serialized as PKH, the Pokémon HOME container format), withdraw them
/// back into a save, and move them between vault boxes.
/// </summary>
public sealed class VaultService(
    OpenHomeDbContext db,
    SaveLibraryService library,
    BackupService backups)
{
    /// <summary>Loads a registered save and returns its per-box, per-slot grid.</summary>
    public async Task<IReadOnlyList<BoxView>> ListSaveBoxesAsync(Guid saveId, CancellationToken ct = default)
    {
        var record = await library.GetAsync(saveId, ct);
        var sav = await library.LoadAsync(record, ct);

        var boxes = new List<BoxView>(sav.BoxCount);
        var names = (IBoxDetailName)sav;
        for (var box = 0; box < sav.BoxCount; box++)
        {
            var slots = new List<BoxSlotSummary>(sav.BoxSlotCount);
            for (var slot = 0; slot < sav.BoxSlotCount; slot++)
                slots.Add(ToSlotSummary(sav.GetBoxSlotAtIndex(box, slot), box, slot));
            boxes.Add(new BoxView(box, names.GetBoxName(box), slots));
        }
        return boxes;
    }

    /// <summary>Lists all vault boxes (creating the default box on first use) with their slot grids.</summary>
    public async Task<IReadOnlyList<VaultBoxView>> ListVaultBoxesAsync(CancellationToken ct = default)
    {
        await EnsureDefaultBoxAsync(ct);
        var boxes = await db.VaultBoxes
            .Include(b => b.Pokemon)
            .OrderBy(b => b.Order)
            .ToListAsync(ct);
        return boxes.Select(ToVaultBoxView).ToList();
    }

    /// <summary>Creates a new, empty vault box.</summary>
    public async Task<VaultBoxView> CreateVaultBoxAsync(string? name, CancellationToken ct = default)
    {
        var order = await db.VaultBoxes.CountAsync(ct);
        var box = new VaultBox { Id = Guid.NewGuid(), Name = name ?? $"Vault {order + 1}", Order = order };
        db.VaultBoxes.Add(box);
        await db.SaveChangesAsync(ct);
        return ToVaultBoxView(box);
    }

    /// <summary>
    /// Moves the Pokémon at (box, slot) of a registered save into the first free
    /// vault slot, then clears the slot in the save and writes the save back
    /// (snapshotting the previous on-disk state first).
    /// </summary>
    public async Task<StoredPokemonSummary> DepositAsync(Guid saveId, int box, int slot, CancellationToken ct = default)
    {
        var record = await library.GetAsync(saveId, ct);
        var sav = await library.LoadAsync(record, ct);
        ValidateSlot(sav, box, slot);

        var pk = sav.GetBoxSlotAtIndex(box, slot);
        if (pk.Species == 0)
            throw new InvalidOperationException($"Box {box} slot {slot} is empty — nothing to deposit.");

        var pkh = PKH.ConvertFromPKM(ToHomeCompatible(pk));
        pkh.Tracker = await NewHomeTrackerAsync(ct);
        var data = pkh.Rebuild();

        var (targetBox, targetSlot) = await FindFreeSlotAsync(ct);
        var stored = new StoredPokemon
        {
            Id = Guid.NewGuid(),
            VaultBoxId = targetBox.Id,
            Slot = targetSlot,
            Data = data,
            Species = pkh.Species,
            Form = pkh.Form,
            IsShiny = pkh.IsShiny,
            Level = pkh.CurrentLevel,
            Nickname = Sanitize(pkh.Nickname),
            OTName = Sanitize(pkh.OriginalTrainerName),
            OriginGame = pk.Version != GameVersion.Any ? GameInfo.GetVersionName(pk.Version) : record.Game,
            HomeTracker = pkh.Tracker,
            DepositedAt = DateTime.UtcNow,
        };

        backups.Snapshot(record);
        sav.SetBoxSlotAtIndex(sav.BlankPKM, box, slot);
        await library.PersistAsync(record, sav, ct);

        db.StoredPokemon.Add(stored);
        await db.SaveChangesAsync(ct);
        return ToSummary(stored, targetBox.Name);
    }

    /// <summary>Lists every stored Pokémon with its denormalized metadata, ordered by box order then slot.</summary>
    public async Task<IReadOnlyList<StoredPokemonSummary>> ListStoredPokemonAsync(CancellationToken ct = default)
    {
        var stored = await db.StoredPokemon
            .Include(p => p.VaultBox)
            .OrderBy(p => p.VaultBox!.Order)
            .ThenBy(p => p.Slot)
            .ToListAsync(ct);
        return stored.Select(p => ToSummary(p, p.VaultBox?.Name ?? "")).ToList();
    }

    /// <summary>
    /// Reads one stored Pokémon in full: denormalized metadata plus IVs, EVs and moves
    /// deserialized from the stored PKH bytes (<c>PKH.Rebuild()</c> round-trips).
    /// </summary>
    public async Task<StoredPokemonDetail> GetStoredPokemonAsync(Guid storedPokemonId, CancellationToken ct = default)
    {
        var stored = await db.StoredPokemon.Include(p => p.VaultBox).FirstOrDefaultAsync(p => p.Id == storedPokemonId, ct)
            ?? throw new KeyNotFoundException($"No stored Pokémon with id {storedPokemonId}.");

        var pkh = new PKH(stored.Data);
        var ivs = new StatSet(pkh.IV_HP, pkh.IV_ATK, pkh.IV_DEF, pkh.IV_SPA, pkh.IV_SPD, pkh.IV_SPE);
        var evs = new StatSet(pkh.EV_HP, pkh.EV_ATK, pkh.EV_DEF, pkh.EV_SPA, pkh.EV_SPD, pkh.EV_SPE);
        var moves = new[] { pkh.Move1, pkh.Move2, pkh.Move3, pkh.Move4 }.Select(ToMoveInfo).ToList();

        return new StoredPokemonDetail(
            stored.Id, stored.VaultBoxId, stored.VaultBox?.Name ?? "", stored.Slot,
            stored.Species, stored.Form, stored.IsShiny, stored.Level,
            stored.Nickname, stored.OTName, stored.OriginGame, stored.HomeTracker, stored.DepositedAt,
            ivs, evs, moves);
    }

    /// <summary>Maps a move ID to its English display name via PKHeX's bundled string list.</summary>
    private static MoveInfo ToMoveInfo(ushort move)
    {
        var names = GameInfo.Strings.movelist;
        return new MoveInfo(move, move < names.Length ? names[move] : "");
    }

    /// <summary>
    /// PKH stores current moves in per-game side data that is only created from
    /// PB7/PK7/PK8/PB8/PA8/PK9/PA9 sources — a gen ≤5 entity converted straight to
    /// PKH loses its moves. Upgrade those to PK8 first so moves survive the deposit.
    /// The transfer re-localizes un-nicknamed species names; restore the exact
    /// nickname the save held.
    /// </summary>
    private static PKM ToHomeCompatible(PKM pk)
    {
        if (pk is PB7 or PK7 or PK8 or PB8 or PA8 or PK9 or PA9)
            return pk;
        var converted = EntityConverter.ConvertToType(pk, typeof(PK8), out var result);
        if (converted is null || result is not EntityConverterResult.Success)
            return pk;
        converted.Nickname = pk.Nickname;
        return converted;
    }

    /// <summary>
    /// Converts a stored Pokémon into the target save's entity format and places it
    /// at (box, slot), removing it from the vault. The save is snapshotted before
    /// being written.
    /// </summary>
    public async Task<StoredPokemonSummary> WithdrawAsync(Guid storedPokemonId, Guid saveId, int box, int slot, CancellationToken ct = default)
    {
        var stored = await db.StoredPokemon.Include(p => p.VaultBox).FirstOrDefaultAsync(p => p.Id == storedPokemonId, ct)
            ?? throw new KeyNotFoundException($"No stored Pokémon with id {storedPokemonId}.");
        var record = await library.GetAsync(saveId, ct);
        var sav = await library.LoadAsync(record, ct);
        ValidateSlot(sav, box, slot);

        if (sav.GetBoxSlotAtIndex(box, slot).Species != 0)
            throw new InvalidOperationException($"Box {box} slot {slot} is already occupied.");

        var pkh = new PKH(stored.Data);
        var converted = ConvertForSave(pkh, sav, record.Game);

        backups.Snapshot(record);
        sav.SetBoxSlotAtIndex(converted, box, slot);
        await library.PersistAsync(record, sav, ct);

        var summary = ToSummary(stored, stored.VaultBox?.Name ?? "");
        db.StoredPokemon.Remove(stored);
        await db.SaveChangesAsync(ct);
        return summary;
    }

    /// <summary>Moves a stored Pokémon to another slot within the vault.</summary>
    public async Task<StoredPokemonSummary> MovePokemonAsync(Guid storedPokemonId, Guid targetBoxId, int targetSlot, CancellationToken ct = default)
    {
        var stored = await db.StoredPokemon.FindAsync([storedPokemonId], ct)
            ?? throw new KeyNotFoundException($"No stored Pokémon with id {storedPokemonId}.");
        var targetBox = await db.VaultBoxes.Include(b => b.Pokemon).FirstOrDefaultAsync(b => b.Id == targetBoxId, ct)
            ?? throw new KeyNotFoundException($"No vault box with id {targetBoxId}.");
        if ((uint)targetSlot >= VaultBox.SlotCount)
            throw new InvalidOperationException($"Slot must be between 0 and {VaultBox.SlotCount - 1}.");
        if (targetBox.Pokemon.Any(p => p.Slot == targetSlot))
            throw new InvalidOperationException($"Vault box '{targetBox.Name}' slot {targetSlot} is already occupied.");

        stored.VaultBoxId = targetBoxId;
        stored.Slot = targetSlot;
        await db.SaveChangesAsync(ct);
        return ToSummary(stored, targetBox.Name);
    }

    /// <summary>
    /// Converts a PKH into the entity format used by <paramref name="sav"/>. Prefers
    /// the dedicated <c>PKH.ConvertTo*</c> methods; falls back to
    /// <see cref="EntityConverter"/> and finally throws <see cref="UnsupportedConversionException"/>.
    /// </summary>
    private static PKM ConvertForSave(PKH pkh, SaveFile sav, string gameName)
    {
        var target = sav.BlankPKM;
        try
        {
            PKM? result = target switch
            {
                PK8 => pkh.ConvertToPK8(),
                PB8 => pkh.ConvertToPB8(),
                PA8 => pkh.ConvertToPA8(),
                PK9 => pkh.ConvertToPK9(),
                PA9 => pkh.ConvertToPA9(),
                PB7 => pkh.ConvertToPB7(),
                _ => EntityConverter.ConvertToType(pkh, target.GetType(), out _),
            };
            if (result is not null)
                return result;
        }
        catch (Exception ex)
        {
            throw new UnsupportedConversionException(
                $"Cannot convert the stored Pokémon for {gameName} ({target.GetType().Name}).", ex);
        }

        throw new UnsupportedConversionException(
            $"Withdrawing into {gameName} ({target.GetType().Name}) is not supported: " +
            "Pokémon HOME cannot transfer an entity back to a generation older than its origin.");
    }

    private async Task<(VaultBox Box, int Slot)> FindFreeSlotAsync(CancellationToken ct)
    {
        await EnsureDefaultBoxAsync(ct);
        var boxes = await db.VaultBoxes.Include(b => b.Pokemon).OrderBy(b => b.Order).ToListAsync(ct);
        foreach (var box in boxes)
        {
            var used = box.Pokemon.Select(p => p.Slot).ToHashSet();
            for (var slot = 0; slot < VaultBox.SlotCount; slot++)
            {
                if (!used.Contains(slot))
                    return (box, slot);
            }
        }

        var created = new VaultBox { Id = Guid.NewGuid(), Name = $"Vault {boxes.Count + 1}", Order = boxes.Count };
        db.VaultBoxes.Add(created);
        return (created, 0);
    }

    private async Task EnsureDefaultBoxAsync(CancellationToken ct)
    {
        if (await db.VaultBoxes.AnyAsync(ct))
            return;
        db.VaultBoxes.Add(new VaultBox { Id = Guid.NewGuid(), Name = "Vault 1", Order = 0 });
        await db.SaveChangesAsync(ct);
    }

    private async Task<ulong> NewHomeTrackerAsync(CancellationToken ct)
    {
        ulong tracker;
        do
        {
            tracker = (ulong)Random.Shared.NextInt64(1, long.MaxValue);
        }
        while (await db.StoredPokemon.AnyAsync(p => p.HomeTracker == tracker, ct));
        return tracker;
    }

    private static void ValidateSlot(SaveFile sav, int box, int slot)
    {
        if ((uint)box >= (uint)sav.BoxCount)
            throw new InvalidOperationException($"Box must be between 0 and {sav.BoxCount - 1}.");
        if ((uint)slot >= (uint)sav.BoxSlotCount)
            throw new InvalidOperationException($"Slot must be between 0 and {sav.BoxSlotCount - 1}.");
    }

    private static BoxSlotSummary ToSlotSummary(PKM pk, int box, int slot) => new(
        box,
        slot,
        pk.Species == 0,
        pk.Species,
        pk.Form,
        pk.Species == 0 ? "" : Sanitize(pk.Nickname),
        pk.Species == 0 ? 0 : pk.CurrentLevel,
        pk.Species != 0 && pk.IsShiny,
        null);

    private static VaultBoxView ToVaultBoxView(VaultBox box)
    {
        var bySlot = box.Pokemon.ToDictionary(p => p.Slot);
        var slots = new List<BoxSlotSummary>(VaultBox.SlotCount);
        for (var slot = 0; slot < VaultBox.SlotCount; slot++)
        {
            if (bySlot.TryGetValue(slot, out var p))
            {
                slots.Add(new BoxSlotSummary(box.Order, slot, false, p.Species, p.Form, p.Nickname, p.Level, p.IsShiny, p.Id));
            }
            else
            {
                slots.Add(new BoxSlotSummary(box.Order, slot, true, 0, 0, "", 0, false, null));
            }
        }
        return new VaultBoxView(box.Id, box.Name, box.Order, slots);
    }

    private static StoredPokemonSummary ToSummary(StoredPokemon p, string boxName) => new(
        p.Id, p.VaultBoxId, boxName, p.Slot, p.Species, p.Form, p.IsShiny, p.Level,
        p.Nickname, p.OTName, p.OriginGame, p.HomeTracker, p.DepositedAt);

    /// <summary>
    /// Gen 1-5 strings use 0xFFFF/0x0000 terminators that can leak into the managed
    /// string when PKHeX copies trash bytes into the HOME container — strip them.
    /// </summary>
    private static string Sanitize(string value) => value.TrimEnd('￿', '\0');
}
