using PKHeX.Core;

namespace OpenHome.Core;

/// <summary>High-level summary of a loaded save file, safe to serialize to the API.</summary>
public sealed record SaveSummary(
    string FileName,
    string Game,
    string TrainerName,
    int BoxCount,
    int BoxSlotCount,
    IReadOnlyList<string> BoxNames);

/// <summary>
/// Thin facade over PKHeX.Core save loading. All PKHeX-touching code lives behind
/// services like this so the rest of the app never sees PKHeX types.
/// </summary>
public sealed class SaveFileService
{
    public SaveFile Load(string path)
    {
        var sav = SaveUtil.GetSaveFile(path);
        return sav ?? throw new InvalidDataException($"Unrecognized or unsupported save file: {path}");
    }

    public SaveSummary Summarize(string path)
    {
        var sav = Load(path);
        var names = new List<string>(sav.BoxCount);
        for (var i = 0; i < sav.BoxCount; i++)
            names.Add(((IBoxDetailName)sav).GetBoxName(i));

        return new SaveSummary(
            Path.GetFileName(path),
            sav is ICustomSaveDisplayName custom ? custom.SaveDisplayName : GameInfo.GetVersionName(sav.Version),
            sav.OT,
            sav.BoxCount,
            sav.BoxSlotCount,
            names);
    }
}
