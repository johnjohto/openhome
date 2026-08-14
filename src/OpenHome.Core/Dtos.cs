using System.Text.Json.Serialization;

namespace OpenHome.Core;

/// <summary>An item by national id plus its English display name.</summary>
public sealed record ItemInfo(int Id, string Name);

/// <summary>
/// One slot in a save-file or vault box grid. No PKHeX types cross the wire.
/// <see cref="LegalityValid"/> is the PKHeX legality verdict for occupied vault
/// slots (null for save slots, empty slots, or when analysis was unavailable).
/// <see cref="HeldItem"/> is populated for occupied save slots; vault slots carry
/// null (the vault detail endpoint reports the item instead).
/// </summary>
public sealed record BoxSlotSummary(
    int Box,
    int Slot,
    bool IsEmpty,
    int Species,
    int Form,
    string Nickname,
    int Level,
    bool IsShiny,
    Guid? StoredPokemonId,
    bool? LegalityValid,
    ItemInfo? HeldItem = null);

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
    IReadOnlyList<MoveInfo> Moves,
    ItemInfo? HeldItem = null);

/// <summary>
/// Result of a vault withdraw: the Pokémon as it left the vault, plus the
/// transfer-legality warnings raised before the move. In strict transfer mode a
/// withdraw with any warning is refused instead, so warnings only ever reach the
/// client in free mode.
/// </summary>
public sealed record WithdrawResult(
    StoredPokemonSummary Pokemon,
    IReadOnlyList<string> Warnings);

/// <summary>A stack of identical items in the item vault.</summary>
public sealed record VaultItemSummary(
    int ItemId,
    string Name,
    int Count);

/// <summary>Runtime configuration the UI needs to render: the transfer mode.</summary>
public sealed record ServerConfig(bool StrictTransfers);

/// <summary>
/// Filter/sort parameters for a vault query. All filters are optional and
/// AND-combined; <see cref="Legality"/> accepts "valid" or "invalid" (null verdicts
/// match neither). <see cref="Search"/> is a case-insensitive substring over
/// nickname and OT. <see cref="SortBy"/> names a denormalized column
/// ("species", "form", "level", "nickname", "ot", "origingame", "tracker",
/// "depositedat", "box"); null/empty sorts by box order then slot.
/// </summary>
public sealed record VaultQueryFilter(
    int? Species = null,
    int? MinLevel = null,
    int? MaxLevel = null,
    bool? Shiny = null,
    string? OriginGame = null,
    string? Legality = null,
    string? Search = null,
    string? SortBy = null,
    bool SortDescending = false);

/// <summary>
/// One row of a vault query result: the denormalized metadata of
/// <see cref="StoredPokemonSummary"/> plus the PKHeX legality verdict (null when
/// analysis was unavailable). Legality is computed lazily per result row — fine at
/// vault scale (the box grid already does the same for its badges).
/// </summary>
public sealed record StoredPokemonQueryResult(
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
    bool? LegalityValid);

/// <summary>A (box, slot) coordinate in a save file's box storage.</summary>
public sealed record BoxSlotRef(int Box, int Slot);

/// <summary>
/// One side of a completed trade: the Pokémon as it now stands in this save's slot,
/// plus whether receiving it triggered a trade evolution (and from which species).
/// </summary>
public sealed record TradeSlotResult(
    Guid SaveId,
    int Box,
    int Slot,
    int Species,
    int Form,
    string Nickname,
    int Level,
    bool IsShiny,
    string SpeciesName,
    bool Evolved,
    int EvolvedFromSpecies,
    string? EvolvedFromName);

/// <summary>
/// Report of a completed trade: both sides after the swap. <see cref="SideA"/> is
/// what save A received (now sitting at A's slot), <see cref="SideB"/> what B received.
/// </summary>
public sealed record TradeReport(
    TradeSlotResult SideA,
    TradeSlotResult SideB);

/// <summary>A save file registered in the library.</summary>
public sealed record RegisteredSaveSummary(
    Guid Id,
    string FileName,
    string Game,
    string TrainerName,
    string Sha256,
    DateTime RegisteredAt,
    DateTime LastOpenedAt);

/// <summary>
/// One line of a PKHeX legality report: the check identifier, its judgement
/// ("Valid"/"Fishy"/"Invalid"), whether the check passed, and the localized
/// human-readable message.
/// </summary>
public sealed record LegalityCheckItem(
    string Identifier,
    string Severity,
    bool Valid,
    string Message);

/// <summary>
/// Full legality report for a stored Pokémon: the overall PKHeX verdict, whether
/// the entity could be parsed at all, and the per-check list. Informational only —
/// legality never blocks deposit, withdraw, or move.
/// </summary>
public sealed record LegalityReport(
    bool Valid,
    bool Parsed,
    IReadOnlyList<LegalityCheckItem> Checks);

/// <summary>
/// National-dex progress for one species: whether any copy is in the vault,
/// whether a shiny copy is (tracked separately), and which forms are owned.
/// </summary>
public sealed record DexSpeciesProgress(
    int Species,
    string Name,
    bool Owned,
    bool ShinyOwned,
    IReadOnlyList<int> OwnedForms);

/// <summary>
/// Living national dex computed from current vault contents: one entry per
/// species id (1..Total), with owned/shiny-owned rollup counts.
/// </summary>
public sealed record NationalDexProgress(
    int Total,
    int Owned,
    int ShinyOwned,
    IReadOnlyList<DexSpeciesProgress> Species);

/// <summary>
/// Dex progress of one registered save. When <see cref="UsesSaveDexData"/> is
/// true, seen/caught come from the save's own Pokédex data (capped at that
/// game's species range); otherwise the save format has no dex and the numbers
/// are species present in the save's boxes, with seen == caught.
/// </summary>
public sealed record SaveDexProgress(
    Guid SaveId,
    string Game,
    string TrainerName,
    bool UsesSaveDexData,
    int Total,
    int Seen,
    int Caught,
    IReadOnlyList<int> SeenSpecies,
    IReadOnlyList<int> CaughtSpecies);
