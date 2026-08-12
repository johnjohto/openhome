using Microsoft.EntityFrameworkCore;

namespace OpenHome.Core.Persistence;

/// <summary>SQLite-backed store for the save library and the vault.</summary>
public sealed class OpenHomeDbContext(DbContextOptions<OpenHomeDbContext> options) : DbContext(options)
{
    public DbSet<SaveFileRecord> SaveFiles => Set<SaveFileRecord>();
    public DbSet<VaultBox> VaultBoxes => Set<VaultBox>();
    public DbSet<StoredPokemon> StoredPokemon => Set<StoredPokemon>();

    /// <summary>Builds a context over the database at <paramref name="databasePath"/>.</summary>
    public static OpenHomeDbContext Create(string databasePath)
    {
        var options = new DbContextOptionsBuilder<OpenHomeDbContext>()
            .UseSqlite($"Data Source={databasePath}")
            .Options;
        return new OpenHomeDbContext(options);
    }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<VaultBox>()
            .HasIndex(b => b.Order)
            .IsUnique();

        modelBuilder.Entity<StoredPokemon>()
            .HasIndex(p => new { p.VaultBoxId, p.Slot })
            .IsUnique();

        modelBuilder.Entity<StoredPokemon>()
            .HasIndex(p => p.HomeTracker);
    }
}
