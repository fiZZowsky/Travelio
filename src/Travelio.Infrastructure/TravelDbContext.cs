using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Travelio.Infrastructure;

public sealed class TravelDbContext(DbContextOptions<TravelDbContext> options) : IdentityDbContext<IdentityUser>(options)
{
    public DbSet<TripRecord> Trips => Set<TripRecord>();
    public DbSet<TripMember> Members => Set<TripMember>();
    public DbSet<PassportRecord> Passports => Set<PassportRecord>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.Entity<TripRecord>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasIndex(x => x.OwnerId);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.OwnerId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<TripMember>(entity =>
        {
            entity.HasKey(x => new { x.TripId, x.UserId });
            entity.HasIndex(x => x.UserId);
            entity.HasOne<TripRecord>().WithMany(x => x.Members).HasForeignKey(x => x.TripId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        builder.Entity<PassportRecord>(entity =>
        {
            entity.HasKey(x => x.UserId);
            entity.Property(x => x.Version).IsConcurrencyToken();
            entity.HasOne<IdentityUser>().WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}

public sealed class TripRecord
{
    public Guid Id { get; set; }
    public string OwnerId { get; set; } = "";
    public string Payload { get; set; } = "";
    public long Version { get; set; }
    public DateTime UpdatedAt { get; set; }
    public List<TripMember> Members { get; set; } = [];
}

public sealed class TripMember
{
    public Guid TripId { get; set; }
    public string UserId { get; set; } = "";
    public bool CanEdit { get; set; }
}

public sealed class PassportRecord
{
    public string UserId { get; set; } = "";
    public string CountriesJson { get; set; } = "[]";
    public long Version { get; set; }
}

