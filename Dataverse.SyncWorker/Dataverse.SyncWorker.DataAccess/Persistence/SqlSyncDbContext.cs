using Microsoft.EntityFrameworkCore;

namespace DataverseSyncWorker.DataAccess.Persistence;

/// <summary>
/// DbContext chỉ phục vụ các truy vấn đọc catalog.
///
/// Không dùng context này để ghi delivery ledger: ledger có app lock, replay,
/// dead-letter và transaction semantics đã được triển khai bằng ADO.NET.
/// </summary>
public sealed class SqlSyncDbContext(DbContextOptions<SqlSyncDbContext> options) : DbContext(options)
{
    public DbSet<BuildingCatalogRow> Buildings => Set<BuildingCatalogRow>();
    public DbSet<EquipmentCatalogRow> Equipment => Set<EquipmentCatalogRow>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<BuildingCatalogRow>(entity =>
        {
            entity.ToTable("bms_building", "raw");
            entity.HasKey(row => row.BuildingCode);
            entity.Property(row => row.BuildingCode).HasColumnName("building_code").HasMaxLength(50);
            entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(200);
            entity.Property(row => row.SourceBuilding).HasColumnName("source_building").HasMaxLength(100);
            entity.Property(row => row.Description).HasColumnName("description").HasMaxLength(2000);
            entity.Property(row => row.SourceUpdatedAt).HasColumnName("source_updated_at");
        });

        modelBuilder.Entity<EquipmentCatalogRow>(entity =>
        {
            entity.ToTable("bms_equipment", "raw");
            entity.HasKey(row => row.EquipmentCode);
            entity.Property(row => row.EquipmentCode).HasColumnName("equipment_code").HasMaxLength(100);
            entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(200);
            entity.Property(row => row.EquipmentType).HasColumnName("equipment_type").HasMaxLength(100);
            entity.Property(row => row.BuildingCode).HasColumnName("building_code").HasMaxLength(50);
            entity.Property(row => row.Description).HasColumnName("description").HasMaxLength(2000);
            entity.Property(row => row.SourceUpdatedAt).HasColumnName("source_updated_at");
        });
    }
}

public sealed class BuildingCatalogRow
{
    public string BuildingCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceBuilding { get; set; } = "";
    public string? Description { get; set; }
    public DateTime SourceUpdatedAt { get; set; }
}

public sealed class EquipmentCatalogRow
{
    public string EquipmentCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string EquipmentType { get; set; } = "";
    public string BuildingCode { get; set; } = "";
    public string? Description { get; set; }
    public DateTime SourceUpdatedAt { get; set; }
}