using System.Security.Cryptography;
using Microsoft.EntityFrameworkCore;
using OpenHome.Core.Persistence;

namespace OpenHome.Core;

/// <summary>
/// Manages the library of registered save files: import, listing, and loading.
/// </summary>
public sealed class SaveLibraryService(
    OpenHomeDbContext db,
    SaveFileService saveFiles,
    BackupService backups,
    OpenHomeOptions options)
{
    /// <summary>
    /// Imports an uploaded save file: copies it under <c>data/saves/{id}{ext}</c>,
    /// takes an initial backup snapshot, and registers it in the database.
    /// </summary>
    public async Task<RegisteredSaveSummary> RegisterAsync(string uploadedPath, string originalFileName, CancellationToken ct = default)
    {
        // Load first so a bad file is rejected before anything touches disk or the db.
        var summary = saveFiles.Summarize(uploadedPath);

        var record = new SaveFileRecord
        {
            Id = Guid.NewGuid(),
            Game = summary.Game,
            TrainerName = summary.TrainerName,
            RegisteredAt = DateTime.UtcNow,
            LastOpenedAt = DateTime.UtcNow,
        };
        record.FilePath = Path.Combine(options.SavesDirectory, record.Id + Path.GetExtension(originalFileName));

        options.EnsureDirectories();
        File.Copy(uploadedPath, record.FilePath);
        record.Sha256 = ComputeSha256(record.FilePath);
        backups.Snapshot(record);

        db.SaveFiles.Add(record);
        await db.SaveChangesAsync(ct);
        return ToSummary(record);
    }

    public async Task<IReadOnlyList<RegisteredSaveSummary>> ListAsync(CancellationToken ct = default) =>
        (await db.SaveFiles.OrderBy(s => s.RegisteredAt).ToListAsync(ct))
            .Select(ToSummary)
            .ToList();

    public async Task<SaveFileRecord> GetAsync(Guid saveId, CancellationToken ct = default) =>
        await db.SaveFiles.FindAsync([saveId], ct)
        ?? throw new KeyNotFoundException($"No save registered with id {saveId}.");

    /// <summary>Reloads the save entity from disk and stamps <see cref="SaveFileRecord.LastOpenedAt"/>.</summary>
    public async Task<PKHeX.Core.SaveFile> LoadAsync(SaveFileRecord record, CancellationToken ct = default)
    {
        record.LastOpenedAt = DateTime.UtcNow;
        await db.SaveChangesAsync(ct);
        return saveFiles.Load(record.FilePath);
    }

    /// <summary>Writes the save back to disk and refreshes the stored hash.</summary>
    public async Task PersistAsync(SaveFileRecord record, PKHeX.Core.SaveFile sav, CancellationToken ct = default)
    {
        sav.State.Edited = true;
        await File.WriteAllBytesAsync(record.FilePath, sav.Write().ToArray(), ct);
        record.Sha256 = ComputeSha256(record.FilePath);
        await db.SaveChangesAsync(ct);
    }

    public static RegisteredSaveSummary ToSummary(SaveFileRecord record) => new(
        record.Id,
        Path.GetFileName(record.FilePath),
        record.Game,
        record.TrainerName,
        record.Sha256,
        record.RegisteredAt,
        record.LastOpenedAt);

    private static string ComputeSha256(string path)
    {
        using var stream = File.OpenRead(path);
        return Convert.ToHexString(SHA256.HashData(stream)).ToLowerInvariant();
    }
}
