namespace OpenHome.Core;

/// <summary>
/// Filesystem layout for OpenHome's local data. Resolved once at startup from the
/// <c>OPENHOME_DATA</c> environment variable (or <c>data/</c> under the content root).
/// </summary>
public sealed class OpenHomeOptions
{
    public OpenHomeOptions(string dataRoot, bool strictTransfers = false)
    {
        DataRoot = Path.GetFullPath(dataRoot);
        StrictTransfers = strictTransfers;
    }

    /// <summary>Root directory that holds the database, uploaded saves, and backups.</summary>
    public string DataRoot { get; }

    /// <summary>
    /// Strict transfer mode: refuse vault withdraws the target game cannot legally
    /// receive (species absent, backwards-generation transfer). Off by default
    /// (free mode), where the withdraw proceeds and the response carries warnings.
    /// </summary>
    public bool StrictTransfers { get; }

    public string DatabasePath => Path.Combine(DataRoot, "openhome.db");
    public string SavesDirectory => Path.Combine(DataRoot, "saves");
    public string BackupsDirectory => Path.Combine(DataRoot, "backups");

    /// <summary>Creates all data directories if they do not exist yet.</summary>
    public void EnsureDirectories()
    {
        Directory.CreateDirectory(DataRoot);
        Directory.CreateDirectory(SavesDirectory);
        Directory.CreateDirectory(BackupsDirectory);
    }
}
