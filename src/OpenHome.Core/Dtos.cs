using System.Text.Json.Serialization;

namespace OpenHome.Core;

/// <summary>One slot in a save-file or vault box grid. No PKHeX types cross the wire.</summary>
public sealed record BoxSlotSummary(
    int Box,
    int Slot,
    bool IsEmpty,
    int Species,
    int Form,
    string Nickname,
    int Level,
    bool IsShiny,
    Guid? StoredPokemonId);

/// <summary>A named box with its slot grid.</summary>
public sealed record BoxView(
    int Box,
    string Name,
    IReadOnlyList<BoxSlotSummary> Slots);

/// <summary>A vault box with its slot grid.</summary>
public sealed record VaultBoxView(
    Guid Id,
    string Name,
    int Order,
    IReadOnlyList<BoxSlotSummary> Slots);

/// <summary>Metadata of a Pokémon stored in the vault.</summary>
public sealed record StoredPokemonSummary(
    Guid Id,
    Guid BoxId,
    string BoxName,
    int Slot,
    int Species,
    int Form,
    bool IsShiny,
    int Level,
    string Nickname,
    string OTName,
    string OriginGame,
    ulong HomeTracker,
    DateTime DepositedAt);

/// <summary>Six battle stats, in canonical order.</summary>
public sealed record StatSet(int Hp, int Attack, int Defense, int SpAttack, int SpDefense, int Speed);

/// <summary>A learned move: national move ID plus its display name.</summary>
public sealed record MoveInfo(int Id, string Name);

/// <summary>
/// Full detail of a Pokémon stored in the vault: the denormalized metadata from
/// <see cref="StoredPokemonSummary"/> plus IVs, EVs and moves read back out of the
/// stored PKH bytes. IVs/EVs are pinned to "ivs"/"evs" — System.Text.Json would
/// otherwise camelCase the acronyms to "iVs"/"eVs".
/// </summary>
public sealed record StoredPokemonDetail(
    Guid Id,
    Guid BoxId,
    string BoxName,
    int Slot,
    int Species,
    int Form,
    bool IsShiny,
    int Level,
    string Nickname,
    string OTName,
    string OriginGame,
    ulong HomeTracker,
    DateTime DepositedAt,
    [property: JsonPropertyName("ivs")] StatSet IVs,
    [property: JsonPropertyName("evs")] StatSet EVs,
    IReadOnlyList<MoveInfo> Moves);

/// <summary>A save file registered in the library.</summary>
public sealed record RegisteredSaveSummary(
    Guid Id,
    string FileName,
    string Game,
    string TrainerName,
    string Sha256,
    DateTime RegisteredAt,
    DateTime LastOpenedAt);
