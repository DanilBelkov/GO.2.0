using GO2.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace GO2.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<RefreshToken> RefreshTokens => Set<RefreshToken>();
    public DbSet<Map> Maps => Set<Map>();
    public DbSet<MapVersion> MapVersions => Set<MapVersion>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
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

        modelBuilder.Entity<Map>()
            .HasOne(x => x.ActiveVersion)
            .WithMany()
            .HasForeignKey(x => x.ActiveVersionId)
            .OnDelete(DeleteBehavior.SetNull);

        modelBuilder.Entity<MapVersion>()
            .HasOne(x => x.Map)
            .WithMany(x => x.Versions)
            .HasForeignKey(x => x.MapId)
            .OnDelete(DeleteBehavior.Cascade);
    }
}

