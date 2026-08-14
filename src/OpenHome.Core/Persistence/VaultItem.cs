namespace OpenHome.Core.Persistence;

/// <summary>
/// A stack of identical held items in the item vault: national item id plus count.
/// One row per item id; rows are removed when the count reaches zero.
/// </summary>
public sealed class VaultItem
{
    public Guid Id { get; set; }

    /// <summary>National item id (indexes <c>GameInfo.Strings.itemlist</c>).</summary>
    public int ItemId { get; set; }

    /// <summary>How many copies the vault holds; always positive while the row exists.</summary>
    public int Count { get; set; }
}
