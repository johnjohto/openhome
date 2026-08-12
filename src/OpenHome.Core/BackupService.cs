using OpenHome.Core.Persistence;

namespace OpenHome.Core;

/// <summary>
/// Snapshots save files before any write so a bad edit can never destroy data.
/// Backups land in <c>{dataRoot}/backups/{saveId}/{yyyyMMdd-HHmmss-fff}{ext}</c>.
/// </summary>
public sealed class BackupService(OpenHomeOptions options)
{
    /// <summary>
    /// Copies the save's current on-disk bytes into the backup directory.
    /// Returns the backup path, or null if there is nothing on disk to back up.
    /// </summary>
    public string? Snapshot(SaveFileRecord record)
    {
        if (!File.Exists(record.FilePath))
            return null;

        var dir = Path.Combine(options.BackupsDirectory, record.Id.ToString("N"));
        Directory.CreateDirectory(dir);

        var ext = Path.GetExtension(record.FilePath);
        var stamp = DateTime.Now.ToString("yyyyMMdd-HHmmss-fff");
        var dest = Path.Combine(dir, stamp + ext);
        // Same-millisecond writes are possible in tests; never overwrite an existing snapshot.
        for (var i = 1; File.Exists(dest); i++)
            dest = Path.Combine(dir, $"{stamp}-{i}{ext}");

        File.Copy(record.FilePath, dest);
        return dest;
    }
}
