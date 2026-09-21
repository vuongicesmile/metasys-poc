using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BMS.Fake.DataAccess.Persistence;

/// <summary>DbContext EF Core cho database riêng của BMS Fake.</summary>
public sealed class FakeBmsDbContext(DbContextOptions<FakeBmsDbContext> options) : DbContext(options)
{
    /// <summary>Tập building entity trong database Fake.</summary>
    internal DbSet<FakeBuildingEntity> Buildings => Set<FakeBuildingEntity>();

    /// <summary>Tập equipment entity trong database Fake.</summary>
    internal DbSet<FakeEquipmentEntity> Equipment => Set<FakeEquipmentEntity>();

    /// <summary>Tập point entity chứa state hiện tại của simulator.</summary>
    internal DbSet<FakePointEntity> Points => Set<FakePointEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Tách dữ liệu Fake vào schema fake để dễ nhận biết và quản trị.
        ConfigureBuilding(modelBuilder.Entity<FakeBuildingEntity>());
        ConfigureEquipment(modelBuilder.Entity<FakeEquipmentEntity>());
        ConfigurePoint(modelBuilder.Entity<FakePointEntity>());
    }

    /// <summary>Map building entity vào bảng fake.bms_building.</summary>
    private static void ConfigureBuilding(EntityTypeBuilder<FakeBuildingEntity> entity)
    {
        entity.ToTable("bms_building", "fake");
        entity.HasKey(row => row.BuildingCode);
        entity.Property(row => row.BuildingCode).HasColumnName("building_code").HasMaxLength(50).IsUnicode(false);
        entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        entity.Property(row => row.SourceBuilding).HasColumnName("source_building").HasMaxLength(100).IsUnicode(false).IsRequired();
        entity.Property(row => row.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
    }

    /// <summary>Map equipment entity và foreign key tới building.</summary>
    private static void ConfigureEquipment(EntityTypeBuilder<FakeEquipmentEntity> entity)
    {
        entity.ToTable("bms_equipment", "fake");
        entity.HasKey(row => row.EquipmentCode);
        entity.Property(row => row.EquipmentCode).HasColumnName("equipment_code").HasMaxLength(100).IsUnicode(false);
        entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        entity.Property(row => row.EquipmentType).HasColumnName("equipment_type").HasMaxLength(50).IsUnicode(false).IsRequired();
        entity.Property(row => row.BuildingCode).HasColumnName("building_code").HasMaxLength(50).IsUnicode(false).IsRequired();
        entity.Property(row => row.Description).HasColumnName("description").HasMaxLength(2000).IsRequired();
        entity.HasOne<FakeBuildingEntity>()
            .WithMany()
            .HasForeignKey(row => row.BuildingCode)
            .HasConstraintName("FK_fake_bms_equipment_building");
    }

    /// <summary>Map current point state vào bảng fake.bms_point.</summary>
    private static void ConfigurePoint(EntityTypeBuilder<FakePointEntity> entity)
    {
        entity.ToTable("bms_point", "fake");
        entity.HasKey(row => row.ObjectId);
        entity.Property(row => row.ObjectId).HasColumnName("object_id").HasMaxLength(100).IsUnicode(false);
        entity.Property(row => row.ObjectName).HasColumnName("object_name").HasMaxLength(200).IsRequired();
        entity.Property(row => row.ObjectType).HasColumnName("object_type").HasMaxLength(100).IsRequired();
        entity.Property(row => row.Building).HasColumnName("building").HasMaxLength(100).IsRequired();
        entity.Property(row => row.EquipmentCode).HasColumnName("equipment_code").HasMaxLength(100).IsRequired();
        entity.Property(row => row.Value).HasColumnName("value").HasColumnType("decimal(18,4)");
        entity.Property(row => row.Unit).HasColumnName("unit").HasMaxLength(50).IsRequired();
        entity.Property(row => row.Timestamp).HasColumnName("timestamp").HasColumnType("datetime2");
    }
}

/// <summary>Entity database cho một building của Fake Metasys.</summary>
internal sealed class FakeBuildingEntity
{
    public string BuildingCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string SourceBuilding { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Entity database cho một equipment của Fake Metasys.</summary>
internal sealed class FakeEquipmentEntity
{
    public string EquipmentCode { get; set; } = "";
    public string Name { get; set; } = "";
    public string EquipmentType { get; set; } = "";
    public string BuildingCode { get; set; } = "";
    public string Description { get; set; } = "";
}

/// <summary>Entity database chứa current state của một point.</summary>
internal sealed class FakePointEntity
{
    public string ObjectId { get; set; } = "";
    public string ObjectName { get; set; } = "";
    public string ObjectType { get; set; } = "";
    public string Building { get; set; } = "";
    public string EquipmentCode { get; set; } = "";
    public decimal Value { get; set; }
    public string Unit { get; set; } = "";
    public DateTime Timestamp { get; set; }
}
