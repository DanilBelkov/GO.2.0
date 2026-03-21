using GO2.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Data;

// Главный EF DbContext: описывает таблицы и связи доменной модели.
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Map> Maps => Set<Map>();
    public DbSet<MapVersion> MapVersions => Set<MapVersion>();
    public DbSet<TerrainObject> TerrainObjects => Set<TerrainObject>();
    public DbSet<TerrainObjectType> TerrainObjectTypes => Set<TerrainObjectType>();
    public DbSet<DigitizationJob> DigitizationJobs => Set<DigitizationJob>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Email уникален для авторизации.
        modelBuilder.Entity<User>()
            .HasIndex(x => x.Email)
            .IsUnique();

        modelBuilder.Entity<RefreshToken>()
            .HasOne(x => x.User)
            .WithMany(x => x.RefreshTokens)
            .HasForeignKey(x => x.UserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<Map>()
            .HasOne(x => x.OwnerUser)
            .WithMany()
            .HasForeignKey(x => x.OwnerUserId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<MapVersion>()
            .HasOne(x => x.Map)
            .WithMany(x => x.Versions)
            .HasForeignKey(x => x.MapId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TerrainObject>()
            .HasOne(x => x.Map)
            .WithMany()
            .HasForeignKey(x => x.MapId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TerrainObject>()
            .HasOne(x => x.MapVersion)
            .WithMany(x => x.TerrainObjects)
            .HasForeignKey(x => x.MapVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<TerrainObject>()
            .HasOne(x => x.TerrainObjectType)
            .WithMany()
            .HasForeignKey(x => x.TerrainObjectTypeId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<TerrainObjectType>()
            .HasIndex(x => new { x.OwnerUserId, x.Name })
            .IsUnique();

        modelBuilder.Entity<DigitizationJob>()
            .HasOne(x => x.Map)
            .WithMany()
            .HasForeignKey(x => x.MapId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DigitizationJob>()
            .HasOne(x => x.MapVersion)
            .WithMany()
            .HasForeignKey(x => x.MapVersionId)
            .OnDelete(DeleteBehavior.Cascade);

        modelBuilder.Entity<DigitizationJob>()
            .HasIndex(x => new { x.MapId, x.OwnerUserId, x.CreatedAtUtc });
    }
}

