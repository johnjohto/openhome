namespace OpenHome.Formats.Profiles;

/// <summary>
/// Loads <see cref="Gba3Profile"/> definitions: the bundled pokeemerald-expansion default
/// (embedded resource, always available) plus any *.json files dropped into the profiles
/// folder, which override the default when names match. Malformed files are collected as
/// errors, never fatal.
/// </summary>
public static class ProfileStore
{
    private const string DefaultResourceName = "OpenHome.Formats.profiles.pokeemerald-expansion.json";

    public static IReadOnlyList<Gba3Profile> LoadAll(string? profilesDirectory, out IReadOnlyList<string> errors)
    {
        var profiles = new Dictionary<string, Gba3Profile>(StringComparer.OrdinalIgnoreCase);
        var failures = new List<string>();

        if (LoadEmbeddedDefault() is { } fallback)
            profiles[fallback.Name] = fallback;

        if (profilesDirectory is not null && Directory.Exists(profilesDirectory))
        {
            foreach (var file in Directory.EnumerateFiles(profilesDirectory, "*.json").OrderBy(f => f))
            {
                try
                {
                    var profile = Gba3Profile.Load(file);
                    profiles[profile.Name] = profile;
                }
                catch (Exception ex)
                {
                    failures.Add($"{Path.GetFileName(file)}: {ex.Message}");
                }
            }
        }

        errors = failures;
        return profiles.Values.ToList();
    }

    private static Gba3Profile? LoadEmbeddedDefault()
    {
        var assembly = typeof(ProfileStore).Assembly;
        using var stream = assembly.GetManifestResourceStream(DefaultResourceName);
        if (stream is null)
            return null;
        using var reader = new StreamReader(stream);
        try
        {
            return Gba3Profile.Parse(reader.ReadToEnd());
        }
        catch (InvalidDataException)
        {
            return null;
        }
    }
}
