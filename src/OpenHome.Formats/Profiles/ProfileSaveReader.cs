using System.Buffers.Binary;
using PKHeX.Core;

namespace OpenHome.Formats.Profiles;

/// <summary>
/// PKHeX <see cref="ISaveReader"/> for one <see cref="Gba3Profile"/>. Custom readers run
/// before PKHeX's built-in detection, so claiming is deliberately conservative: the file
/// must match the profile size and carry a structurally valid gen-3 sector layout, plus one
/// of the profile's positive signals — a content rule, or an occupied slot whose raw species
/// value is impossible in vanilla gen-3 internal order (the national-order signature of
/// pokeemerald-expansion). Vanilla saves fall through to the built-in SAV3 readers.
/// </summary>
public sealed class ProfileSaveReader(Gba3Profile profile) : ISaveReader
{
    public Gba3Profile Profile => profile;

    public bool IsRecognized(long size) => size == profile.SaveSize;

    public bool TryRead(Memory<byte> data, [System.Diagnostics.CodeAnalysis.NotNullWhen(true)] out SaveFile? sav, string? path)
    {
        sav = null;
        if (!IsRecognized(data.Length))
            return false;

        var slot = ProfileSaveFile.FindActiveSlot(data.Span, profile);
        if (!ProfileSaveFile.SlotValid(data.Span, profile, slot, out _) && profile.Detection.ContentRules.Length == 0)
            return false;

        if (!IsClaimed(data.Span, slot))
            return false;

        try
        {
            sav = new ProfileSaveFile(profile, data);
            sav.Metadata.SetExtraInfo($"profile:{profile.Name}");
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    private bool IsClaimed(ReadOnlySpan<byte> data, int slot)
    {
        foreach (var rule in profile.Detection.ContentRules)
        {
            var expected = Convert.FromHexString(rule.HexBytes.Replace(" ", ""));
            if (rule.Offset + expected.Length > data.Length || !data.Slice(rule.Offset, expected.Length).SequenceEqual(expected))
                return false;
        }
        if (profile.Detection.ContentRules.Length != 0)
            return true;

        if (profile.Detection.ClaimIfNationalSpecies && HasNationalOnlySpecies(data, slot))
            return true;

        return profile.Detection.ClaimAllGen3;
    }

    /// <summary>
    /// Scans occupied party/box slots for a raw species value that cannot occur in vanilla
    /// gen-3 internal order: the 252-276 and 387-411 gaps, or values beyond 412 (egg).
    /// </summary>
    private bool HasNationalOnlySpecies(ReadOnlySpan<byte> data, int slot)
    {
        var sav = new ProfileSaveFile(profile, data.ToArray());
        var max = profile.Pokemon.MaxSpeciesID;
        foreach (var raw in sav.RawOccupiedSpecies())
        {
            if (raw is >= 252 and <= 276 or >= 387 and <= 411)
                return true;
            if (raw > 412 && raw <= max)
                return true;
        }
        return false;
    }
}
