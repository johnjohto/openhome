namespace OpenHome.Core.Persistence;

/// <summary>A save file registered in the OpenHome library.</summary>
public sealed class SaveFileRecord
{
    public Guid Id { get; set; }

    /// <summary>Absolute path of the stored copy under the data root.</summary>
    public string FilePath { get; set; } = "";

    /// <summary>Hex SHA-256 of the file contents, refreshed after every write.</summary>
    public string Sha256 { get; set; } = "";

    /// <summary>Display name of the game version (e.g. "Black").</summary>
    public string Game { get; set; } = "";

    public string TrainerName { get; set; } = "";

    public DateTime RegisteredAt { get; set; }
    public DateTime LastOpenedAt { get; set; }
}
