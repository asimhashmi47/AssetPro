using AssetPro.Api.Domain;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace AssetPro.Api.Data;

public class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<User> Users => Set<User>();
    public DbSet<UserPermission> UserPermissions => Set<UserPermission>();
    public DbSet<Employee> Employees => Set<Employee>();
    public DbSet<Department> Departments => Set<Department>();
    public DbSet<Designation> Designations => Set<Designation>();
    public DbSet<Location> Locations => Set<Location>();
    public DbSet<Brand> Brands => Set<Brand>();
    public DbSet<AssetType> AssetTypes => Set<AssetType>();
    public DbSet<InventoryItem> InventoryItems => Set<InventoryItem>();
    public DbSet<Asset> Assets => Set<Asset>();
    public DbSet<AssetAssignment> AssetAssignments => Set<AssetAssignment>();
    public DbSet<AssetHistory> AssetHistory => Set<AssetHistory>();
    public DbSet<ComponentReplacement> ComponentReplacements => Set<ComponentReplacement>();
    public DbSet<ScrapRecord> ScrapRecords => Set<ScrapRecord>();
    public DbSet<InventoryTransaction> InventoryTransactions => Set<InventoryTransaction>();
    public DbSet<AssetRequest> Requests => Set<AssetRequest>();
    public DbSet<RequestStatusHistory> RequestStatusHistory => Set<RequestStatusHistory>();
    public DbSet<ActivityLog> ActivityLogs => Set<ActivityLog>();
    public DbSet<ApplicationLog> ApplicationLogs => Set<ApplicationLog>();
    public DbSet<AppSetting> Settings => Set<AppSetting>();

    protected override void ConfigureConventions(ModelConfigurationBuilder builder)
    {
        // Enums are stored as readable text; DateTimes are always UTC; SQLite cannot order decimals, so store as double.
        builder.Properties<Enum>().HaveConversion<string>().HaveMaxLength(32);
        builder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        builder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
        builder.Properties<decimal>().HaveConversion<double>();
        builder.Properties<decimal?>().HaveConversion<double?>();
    }

    protected override void OnModelCreating(ModelBuilder b)
    {
        foreach (var lookup in new[] { typeof(Department), typeof(Designation), typeof(Location), typeof(Brand), typeof(AssetType) })
        {
            b.Entity(lookup).Property(nameof(LookupEntity.Name)).HasMaxLength(100).IsRequired();
            b.Entity(lookup).HasIndex(nameof(LookupEntity.Name)).IsUnique();
        }
        b.Entity<AssetType>().Property(t => t.TagPrefix).HasMaxLength(6);

        b.Entity<User>(e =>
        {
            e.Property(u => u.Email).HasMaxLength(200).IsRequired();
            e.Property(u => u.NormalizedEmail).HasMaxLength(200).IsRequired();
            e.HasIndex(u => u.NormalizedEmail).IsUnique();
            e.HasIndex(u => u.EmployeeId).IsUnique();
            e.HasOne(u => u.Employee).WithOne(x => x.User).HasForeignKey<User>(u => u.EmployeeId).OnDelete(DeleteBehavior.SetNull);
            e.HasMany(u => u.Permissions).WithOne().HasForeignKey(p => p.UserId).OnDelete(DeleteBehavior.Cascade);
        });
        b.Entity<UserPermission>(e =>
        {
            e.Property(p => p.Permission).HasMaxLength(64);
            e.HasIndex(p => new { p.UserId, p.Permission }).IsUnique();
        });

        b.Entity<Employee>(e =>
        {
            e.Property(x => x.EmployeeCode).HasMaxLength(20).IsRequired();
            e.Property(x => x.FullName).HasMaxLength(150).IsRequired();
            e.Property(x => x.Email).HasMaxLength(200).IsRequired();
            e.Property(x => x.NormalizedEmail).HasMaxLength(200).IsRequired();
            e.Property(x => x.ContactNumber).HasMaxLength(30);
            e.Property(x => x.CompanyName).HasMaxLength(150);
            e.HasIndex(x => x.EmployeeCode).IsUnique();
            e.HasIndex(x => x.NormalizedEmail).IsUnique();
            e.HasIndex(x => x.FullName);
            e.HasOne(x => x.Department).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Designation).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<InventoryItem>(e =>
        {
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Model).HasMaxLength(100);
            e.HasIndex(x => new { x.AssetTypeId, x.BrandId, x.Model }).IsUnique();
            e.HasOne(x => x.AssetType).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Brand).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<Asset>(e =>
        {
            e.Property(x => x.AssetTag).HasMaxLength(30).IsRequired();
            e.Property(x => x.Name).HasMaxLength(150).IsRequired();
            e.Property(x => x.Model).HasMaxLength(100);
            e.Property(x => x.SerialNumber).HasMaxLength(100);
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => x.AssetTag).IsUnique();
            e.HasIndex(x => x.SerialNumber).IsUnique().HasFilter("SerialNumber IS NOT NULL");
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.Name);
            e.HasOne(x => x.AssetType).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Brand).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Location).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.InventoryItem).WithMany(i => i.Assets).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AssetAssignment>(e =>
        {
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.Property(x => x.ReturnedBy).HasMaxLength(150);
            e.Property(x => x.DamageDetails).HasMaxLength(2000);
            e.Property(x => x.ReturnNotes).HasMaxLength(2000);
            // Database-level guarantee: an asset can have at most one open assignment.
            e.HasIndex(x => x.AssetId).IsUnique().HasFilter("ReturnedAt IS NULL").HasDatabaseName("IX_AssetAssignments_ActiveAsset");
            e.HasIndex(x => x.EmployeeId);
            e.HasOne(x => x.Asset).WithMany(a => a.Assignments).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.AssignedBy).WithMany().HasForeignKey(x => x.AssignedById).OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.ReceivedBy).WithMany().HasForeignKey(x => x.ReceivedById).OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AssetHistory>(e =>
        {
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => new { x.AssetId, x.OccurredAt });
            e.HasIndex(x => x.EmployeeId);
            e.HasIndex(x => x.OccurredAt);
            e.HasOne(x => x.Asset).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PerformedBy).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ComponentReplacement>(e =>
        {
            e.Property(x => x.Component).HasMaxLength(100).IsRequired();
            e.Property(x => x.OldComponent).HasMaxLength(150);
            e.Property(x => x.NewComponent).HasMaxLength(150).IsRequired();
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => x.AssetId);
            e.HasOne(x => x.Asset).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PerformedBy).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ScrapRecord>(e =>
        {
            e.Property(x => x.Reason).HasMaxLength(500).IsRequired();
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => x.AssetId).IsUnique();
            e.HasOne(x => x.Asset).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PerformedBy).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<InventoryTransaction>(e =>
        {
            e.Property(x => x.Notes).HasMaxLength(2000);
            e.HasIndex(x => new { x.InventoryItemId, x.OccurredAt });
            e.HasOne(x => x.InventoryItem).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Asset).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.Employee).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.PerformedBy).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<AssetRequest>(e =>
        {
            e.ToTable("Requests");
            e.Property(x => x.RequestNumber).HasMaxLength(20).IsRequired();
            e.Property(x => x.Comments).HasMaxLength(2000).IsRequired();
            e.HasIndex(x => x.RequestNumber).IsUnique();
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.RequesterEmployeeId);
            e.HasOne(x => x.Asset).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequesterEmployee).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasOne(x => x.RequesterUser).WithMany().OnDelete(DeleteBehavior.Restrict);
            e.HasMany(x => x.StatusHistory).WithOne().HasForeignKey(h => h.RequestId).OnDelete(DeleteBehavior.Cascade);
        });

        b.Entity<RequestStatusHistory>(e =>
        {
            e.Property(x => x.Note).HasMaxLength(1000);
            e.HasOne(x => x.ChangedBy).WithMany().OnDelete(DeleteBehavior.Restrict);
        });

        b.Entity<ActivityLog>(e =>
        {
            e.Property(x => x.Action).HasMaxLength(60).IsRequired();
            e.Property(x => x.Entity).HasMaxLength(60);
            e.Property(x => x.EntityId).HasMaxLength(40);
            e.Property(x => x.UserEmail).HasMaxLength(200);
            e.Property(x => x.Description).HasMaxLength(1000);
            e.Property(x => x.IpAddress).HasMaxLength(64);
            e.HasIndex(x => x.OccurredAt);
            e.HasIndex(x => x.UserId);
        });

        b.Entity<ApplicationLog>(e =>
        {
            e.Property(x => x.Level).HasMaxLength(20);
            e.Property(x => x.Category).HasMaxLength(200);
            e.Property(x => x.Message).HasMaxLength(4000);
            e.Property(x => x.Exception).HasMaxLength(8000);
            e.Property(x => x.TraceId).HasMaxLength(64);
            e.Property(x => x.RequestPath).HasMaxLength(300);
            e.HasIndex(x => x.Timestamp);
        });

        b.Entity<AppSetting>(e =>
        {
            e.HasKey(x => x.Key);
            e.Property(x => x.Key).HasMaxLength(60);
            e.Property(x => x.Value).HasMaxLength(500);
        });
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        StampAuditFields();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        StampAuditFields();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    private void StampAuditFields()
    {
        var now = DateTime.UtcNow;
        foreach (var entry in ChangeTracker.Entries<AuditableEntity>())
        {
            if (entry.State == EntityState.Added)
            {
                if (entry.Entity.CreatedAt == default) entry.Entity.CreatedAt = now;
                if (entry.Entity.UpdatedAt == default) entry.Entity.UpdatedAt = now;
            }
            else if (entry.State == EntityState.Modified && !entry.Property(e => e.UpdatedAt).IsModified)
            {
                // Application code never sets UpdatedAt itself; the seeder does, to build a believable history.
                entry.Entity.UpdatedAt = now;
            }
        }
    }

    private sealed class UtcDateTimeConverter() : ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v.Value : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);
}
