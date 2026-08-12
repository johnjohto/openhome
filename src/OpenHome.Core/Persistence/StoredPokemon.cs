namespace OpenHome.Core.Persistence;

/// <summary>
/// A Pokémon stored in the vault. <see cref="Data"/> holds the serialized PKH
/// (Pokémon HOME container) bytes; the remaining columns are denormalized for search.
/// </summary>
public sealed class StoredPokemon
{
    public Guid Id { get; set; }

    public Guid VaultBoxId { get; set; }
    public VaultBox? VaultBox { get; set; }

    /// <summary>Slot within the box (0-29).</summary>
    public int Slot { get; set; }

    /// <summary>Serialized PKH bytes (<c>PKH.Rebuild()</c> output).</summary>
    public byte[] Data { get; set; } = [];

    public int Species { get; set; }
    public int Form { get; set; }
    public bool IsShiny { get; set; }
    public int Level { get; set; }
    public string Nickname { get; set; } = "";
    public string OTName { get; set; } = "";
    public string OriginGame { get; set; } = "";

    /// <summary>Pokémon HOME tracker; assigned by OpenHome at deposit time.</summary>
    public ulong HomeTracker { get; set; }

    public DateTime DepositedAt { get; set; }
}
