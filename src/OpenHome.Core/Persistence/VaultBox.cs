namespace OpenHome.Core.Persistence;

/// <summary>A 30-slot box in the OpenHome vault.</summary>
public sealed class VaultBox
{
    public const int SlotCount = 30;

    public Guid Id { get; set; }
    public string Name { get; set; } = "";

    /// <summary>Display order; also used to pick the next box when one fills up.</summary>
    public int Order { get; set; }

    public List<StoredPokemon> Pokemon { get; set; } = [];
}
