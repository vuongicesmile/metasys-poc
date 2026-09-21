using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace BMS.Ingestion.DataAccess.Persistence;

/// <summary>DbContext EF Core kết nối tới schema SQL BMS hiện có, đặc biệt là schema <c>raw</c>.</summary>
public sealed class BmsIngestionDbContext(DbContextOptions<BmsIngestionDbContext> options) : DbContext(options)
{
    /// <summary>Tập entity tòa nhà; EF ánh xạ tới raw.bms_building.</summary>
    internal DbSet<BmsBuildingEntity> Buildings => Set<BmsBuildingEntity>();

    /// <summary>Tập entity thiết bị; EF ánh xạ tới raw.bms_equipment.</summary>
    internal DbSet<BmsEquipmentEntity> Equipment => Set<BmsEquipmentEntity>();

    /// <summary>Tập entity reading; EF ánh xạ tới raw.bms_reading.</summary>
    internal DbSet<BmsReadingEntity> Readings => Set<BmsReadingEntity>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        // Gọi cấu hình riêng cho từng bảng để method chính dễ đọc.
        ConfigureBuilding(modelBuilder.Entity<BmsBuildingEntity>());
        ConfigureEquipment(modelBuilder.Entity<BmsEquipmentEntity>());
        ConfigureReading(modelBuilder.Entity<BmsReadingEntity>());
    }

    /// <summary>Cấu hình entity tòa nhà ánh xạ tới raw.bms_building.</summary>
    private static void ConfigureBuilding(EntityTypeBuilder<BmsBuildingEntity> entity)
    {
        // Chỉ rõ tên bảng và schema thay vì để EF tự suy luận.
        entity.ToTable("bms_building", "raw");
        // building_code là khóa chính của bảng.
        entity.HasKey(row => row.BuildingCode);
        // Các dòng Property bên dưới ánh xạ property C# sang tên/cấu hình cột SQL.
        entity.Property(row => row.BuildingCode).HasColumnName("building_code").HasMaxLength(50).IsUnicode(false);
        entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        entity.Property(row => row.SourceBuilding).HasColumnName("source_building").HasMaxLength(100).IsUnicode(false).IsRequired();
        entity.Property(row => row.Description).HasColumnName("description").HasMaxLength(2000);
        entity.Property(row => row.SourceUpdatedAt).HasColumnName("source_updated_at").HasColumnType("datetime2");
        // Nếu insert không truyền IngestedAt, SQL Server dùng SYSUTCDATETIME().
        entity.Property(row => row.IngestedAt).HasColumnName("ingested_at").HasColumnType("datetime2")
            .HasDefaultValueSql("SYSUTCDATETIME()");
    }

    /// <summary>Cấu hình entity thiết bị và foreign key tới tòa nhà.</summary>
    private static void ConfigureEquipment(EntityTypeBuilder<BmsEquipmentEntity> entity)
    {
        // Ánh xạ entity vào đúng bảng và schema hiện hữu.
        entity.ToTable("bms_equipment", "raw");
        // equipment_code là khóa chính.
        entity.HasKey(row => row.EquipmentCode);
        // Ánh xạ tên, độ dài và kiểu chuỗi của từng cột.
        entity.Property(row => row.EquipmentCode).HasColumnName("equipment_code").HasMaxLength(100).IsUnicode(false);
        entity.Property(row => row.Name).HasColumnName("name").HasMaxLength(200).IsRequired();
        entity.Property(row => row.EquipmentType).HasColumnName("equipment_type").HasMaxLength(50).IsUnicode(false).IsRequired();
        entity.Property(row => row.BuildingCode).HasColumnName("building_code").HasMaxLength(50).IsUnicode(false).IsRequired();
        entity.Property(row => row.Description).HasColumnName("description").HasMaxLength(2000);
        entity.Property(row => row.SourceUpdatedAt).HasColumnName("source_updated_at").HasColumnType("datetime2");
        // SQL Server tự điền thời điểm ingestion khi insert mới.
        entity.Property(row => row.IngestedAt).HasColumnName("ingested_at").HasColumnType("datetime2")
            .HasDefaultValueSql("SYSUTCDATETIME()");
        // Thiết bị bắt buộc phải tham chiếu tới một tòa nhà tồn tại.
        entity.HasOne<BmsBuildingEntity>()
            .WithMany()
            .HasForeignKey(row => row.BuildingCode)
            .HasConstraintName("FK_raw_bms_equipment_building");
    }

    /// <summary>Cấu hình reading; các cột legacy khác được giữ nguyên nhưng không map vào EF.</summary>
    private static void ConfigureReading(EntityTypeBuilder<BmsReadingEntity> entity)
    {
        // Ánh xạ vào bảng lịch sử raw.bms_reading.
        entity.ToTable("bms_reading", "raw");
        // id là khóa chính và identity do SQL Server sinh.
        entity.HasKey(row => row.Id);
        entity.Property(row => row.Id).HasColumnName("id").ValueGeneratedOnAdd();
        // object_id là định danh bắt buộc của point.
        entity.Property(row => row.ObjectId).HasColumnName("object_id").HasMaxLength(100).IsUnicode(false).IsRequired();
        // Các metadata còn lại được map đúng độ dài theo SQL script.
        entity.Property(row => row.ObjectName).HasColumnName("object_name").HasMaxLength(200).IsUnicode(false);
        entity.Property(row => row.ObjectType).HasColumnName("object_type").HasMaxLength(100).IsUnicode(false);
        entity.Property(row => row.Building).HasColumnName("building").HasMaxLength(100).IsUnicode(false);
        entity.Property(row => row.EquipmentCode).HasColumnName("equipment_code").HasMaxLength(100).IsUnicode(false);
        entity.Property(row => row.ReadingTime).HasColumnName("reading_time").HasColumnType("datetime2");
        // Giữ bốn chữ số thập phân theo contract của raw.bms_reading.
        entity.Property(row => row.ReadingValue).HasColumnName("reading_value").HasColumnType("decimal(18,4)");
        entity.Property(row => row.Unit).HasColumnName("unit").HasMaxLength(50).IsUnicode(false);
        entity.Property(row => row.SourceSystem).HasColumnName("source_system").HasMaxLength(100).IsUnicode(false).IsRequired();
        // Dùng giờ local SQL theo contract cũ của bảng reading.
        entity.Property(row => row.IngestedAt).HasColumnName("ingested_at").HasColumnType("datetime2")
            .HasDefaultValueSql("GETDATE()");
    }
}
