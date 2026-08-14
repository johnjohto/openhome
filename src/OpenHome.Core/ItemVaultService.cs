using Microsoft.EntityFrameworkCore;
using OpenHome.Core.Persistence;
using PKHeX.Core;

namespace OpenHome.Core;

/// <summary>
/// The item vault: held-item storage independent of any game, which official
/// Pokémon HOME cannot do. Deposit takes the held item off a Pokémon in a
/// registered save (stacking with the vault's existing count); withdraw puts an
/// item onto an empty-handed Pokémon. Every save write snapshots first.
/// </summary>
public sealed class ItemVaultService(
    OpenHomeDbContext db,
    SaveLibraryService library,
    BackupService backups)
{
    /// <summary>Lists every item stack in the vault, ordered by item id.</summary>
    public async Task<IReadOnlyList<VaultItemSummary>> ListItemsAsync(CancellationToken ct = default)
    {
        var rows = await db.VaultItems.OrderBy(i => i.ItemId).ToListAsync(ct);
        return rows.Select(i => new VaultItemSummary(i.ItemId, ItemName(i.ItemId), i.Count)).ToList();
    }

    /// <summary>
    /// Takes the held item off the Pokémon at (box, slot) of a registered save and
    /// adds it to the vault's stack for that item. The save is snapshotted before
    /// being written back.
    /// </summary>
    public async Task<VaultItemSummary> DepositItemAsync(Guid saveId, int box, int slot, CancellationToken ct = default)
    {
        var record = await library.GetAsync(saveId, ct);
        var sav = await library.LoadAsync(record, ct);
        ValidateSlot(sav, box, slot);

        var pk = sav.GetBoxSlotAtIndex(box, slot);
        if (pk.Species == 0)
            throw new InvalidOperationException($"Box {box} slot {slot} is empty — no Pokémon to take an item from.");
        var itemId = pk.HeldItem;
        if (itemId == 0)
            throw new InvalidOperationException(
                $"The Pokémon at box {box} slot {slot} is not holding an item.");
        ValidateItemId(itemId);

        backups.Snapshot(record);
        pk.HeldItem = 0;
        sav.SetBoxSlotAtIndex(pk, box, slot);
        await library.PersistAsync(record, sav, ct);

        var row = await db.VaultItems.FirstOrDefaultAsync(i => i.ItemId == itemId, ct);
        if (row is null)
        {
            row = new VaultItem { Id = Guid.NewGuid(), ItemId = itemId, Count = 0 };
            db.VaultItems.Add(row);
        }
        row.Count++;
        await db.SaveChangesAsync(ct);
        return new VaultItemSummary(itemId, ItemName(itemId), row.Count);
    }

    /// <summary>
    /// Takes one copy of <paramref name="itemId"/> out of the vault and puts it on
    /// the Pokémon at (box, slot) of a registered save. The Pokémon must be
    /// empty-handed; replacing an existing held item is refused so nothing is
    /// silently destroyed. The save is snapshotted before being written back.
    /// </summary>
    public async Task<VaultItemSummary> WithdrawItemAsync(int itemId, Guid saveId, int box, int slot, CancellationToken ct = default)
    {
        ValidateItemId(itemId);
        var row = await db.VaultItems.FirstOrDefaultAsync(i => i.ItemId == itemId && i.Count > 0, ct)
            ?? throw new KeyNotFoundException($"The item vault holds no {ItemName(itemId)}.");

        var record = await library.GetAsync(saveId, ct);
        var sav = await library.LoadAsync(record, ct);
        ValidateSlot(sav, box, slot);

        var pk = sav.GetBoxSlotAtIndex(box, slot);
        if (pk.Species == 0)
            throw new InvalidOperationException($"Box {box} slot {slot} is empty — no Pokémon to give an item to.");
        if (pk.HeldItem != 0)
            throw new InvalidOperationException(
                $"The Pokémon at box {box} slot {slot} is already holding {ItemName(pk.HeldItem)}. " +
                "Deposit that item into the vault first.");

        backups.Snapshot(record);
        pk.HeldItem = itemId;
        sav.SetBoxSlotAtIndex(pk, box, slot);
        await library.PersistAsync(record, sav, ct);

        row.Count--;
        if (row.Count == 0)
            db.VaultItems.Remove(row);
        await db.SaveChangesAsync(ct);
        return new VaultItemSummary(itemId, ItemName(itemId), row.Count);
    }

    /// <summary>Maps an item id to its display name, or null when the id is not an item.</summary>
    public static ItemInfo? ToItemInfo(int itemId) =>
        itemId <= 0 ? null : new ItemInfo(itemId, ItemName(itemId));

    /// <summary>English display name for an item id via PKHeX's bundled string list.</summary>
    public static string ItemName(int itemId)
    {
        var names = GameInfo.Strings.itemlist;
        return itemId >= 0 && itemId < names.Length ? names[itemId] : $"#{itemId}";
    }

    private static void ValidateItemId(int itemId)
    {
        if (itemId <= 0 || itemId >= GameInfo.Strings.itemlist.Length)
            throw new InvalidOperationException($"Unknown item id {itemId}.");
    }

    private static void ValidateSlot(SaveFile sav, int box, int slot)
    {
        if ((uint)box >= (uint)sav.BoxCount)
            throw new InvalidOperationException($"Box must be between 0 and {sav.BoxCount - 1}.");
        if ((uint)slot >= (uint)sav.BoxSlotCount)
            throw new InvalidOperationException($"Slot must be between 0 and {sav.BoxSlotCount - 1}.");
    }
}
