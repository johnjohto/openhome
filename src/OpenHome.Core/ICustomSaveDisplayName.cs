namespace OpenHome.Core;

/// <summary>
/// Optional display-name override for non-standard save formats (romhack
/// profiles, Essentials fangames). Implemented by SaveFile subclasses in
/// OpenHome.Formats; Core checks for it when summarizing a save so the save
/// library shows "Emerald (pokeemerald-expansion)" instead of plain "Emerald".
/// </summary>
public interface ICustomSaveDisplayName
{
    /// <summary>Human-readable name shown anywhere the save's game is displayed.</summary>
    string SaveDisplayName { get; }
}
