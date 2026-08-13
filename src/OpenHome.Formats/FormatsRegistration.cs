using OpenHome.Formats.Essentials;
using OpenHome.Formats.Profiles;
using PKHeX.Core;

namespace OpenHome.Formats;

/// <summary>
/// One-call registration hook for all OpenHome save formats. Call once at server startup
/// (before any save is loaded):
/// <code>FormatsRegistration.RegisterAll(Path.Combine(options.DataRoot, "profiles"));</code>
/// Adds a <see cref="ProfileSaveReader"/> per romhack profile (bundled pokeemerald-expansion
/// default plus any *.json in the profiles folder) and the Essentials Game.rxdata reader to
/// <see cref="SaveUtil.CustomSaveReaders"/>. Idempotent.
/// </summary>
public static class FormatsRegistration
{
    private static readonly object Lock = new();
    private static bool _registered;

    /// <summary>Profiles registered on the last <see cref="RegisterAll"/> call (for diagnostics/UI).</summary>
    public static IReadOnlyList<string> RegisteredProfiles { get; private set; } = [];

    /// <summary>Profile files that failed to parse on the last call.</summary>
    public static IReadOnlyList<string> ProfileErrors { get; private set; } = [];

    public static void RegisterAll(string? profilesDirectory = null)
    {
        lock (Lock)
        {
            if (_registered)
                return;

            var profiles = ProfileStore.LoadAll(profilesDirectory, out var errors);
            foreach (var profile in profiles)
                SaveUtil.CustomSaveReaders.Add(new ProfileSaveReader(profile));

            // Essentials last: it recognizes by content, so it must not shadow the profile readers.
            SaveUtil.CustomSaveReaders.Add(new EssentialsSaveReader());

            RegisteredProfiles = profiles.Select(p => p.Name).ToList();
            ProfileErrors = errors;
            _registered = true;
        }
    }

    /// <summary>Removes everything <see cref="RegisterAll"/> added. Intended for tests.</summary>
    public static void Reset()
    {
        lock (Lock)
        {
            SaveUtil.CustomSaveReaders.RemoveAll(r => r is ProfileSaveReader or EssentialsSaveReader);
            RegisteredProfiles = [];
            ProfileErrors = [];
            _registered = false;
        }
    }
}
