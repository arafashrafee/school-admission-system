using Microsoft.EntityFrameworkCore;
using SchoolAdmissionSystem.Api.Models;

namespace SchoolAdmissionSystem.Api.Data;

public class ApplicationDbContext : DbContext
{
    public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
        : base(options)
    {
    }

    public DbSet<AdmissionClass> AdmissionClasses => Set<AdmissionClass>();
    public DbSet<AdmissionHold> AdmissionHolds => Set<AdmissionHold>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // AdmissionClass Entity Configuration
        modelBuilder.Entity<AdmissionClass>(entity =>
        {
            entity.ToTable("AdmissionClasses", t =>
            {
                // Constraint: TotalSeats must be positive
                t.HasCheckConstraint("CK_AdmissionClasses_TotalSeats_Positive", "\"TotalSeats\" > 0");
            });

            entity.HasKey(c => c.Id);

            entity.Property(c => c.Name)
                .IsRequired()
                .HasMaxLength(100);

            // Constraint: Class names must be unique
            entity.HasIndex(c => c.Name)
                .IsUnique()
                .HasDatabaseName("IX_AdmissionClasses_Name");

            entity.Property(c => c.TotalSeats)
                .IsRequired();

            entity.Property(c => c.CreatedAt)
                .IsRequired();
        });

        // AdmissionHold Entity Configuration
        modelBuilder.Entity<AdmissionHold>(entity =>
        {
            entity.ToTable("AdmissionHolds", t =>
            {
                // Constraint: Valid hold statuses
                t.HasCheckConstraint(
                    "CK_AdmissionHolds_Status_Valid",
                    "\"Status\" IN ('HELD', 'CONFIRMED', 'EXPIRED', 'CANCELLED')");

                // Constraint: StudentId must be exactly 17 decimal digits preserving leading zeroes
                t.HasCheckConstraint(
                    "CK_AdmissionHolds_StudentId_17Digits",
                    "length(\"StudentId\") = 17 AND \"StudentId\" ~ '^[0-9]{17}$'");
            });

            entity.HasKey(h => h.Id);

            entity.Property(h => h.ClassId)
                .IsRequired();

            entity.Property(h => h.StudentId)
                .IsRequired()
                .HasMaxLength(17);

            entity.Property(h => h.Status)
                .IsRequired()
                .HasMaxLength(20)
                .HasConversion<string>();

            entity.Property(h => h.CreatedAt)
                .IsRequired();

            entity.Property(h => h.ExpiresAt)
                .IsRequired();

            entity.Property(h => h.ConfirmedAt)
                .IsRequired(false);

            // Foreign key integrity with Restrict delete behavior
            entity.HasOne(h => h.Class)
                .WithMany(c => c.Holds)
                .HasForeignKey(h => h.ClassId)
                .OnDelete(DeleteBehavior.Restrict)
                .HasConstraintName("FK_AdmissionHolds_AdmissionClasses_ClassId");

            // Index 1: Prevent duplicate active or confirmed holds for same student in same class.
            // Excludes EXPIRED and CANCELLED statuses so expired holds never permanently block a student.
            entity.HasIndex(h => new { h.ClassId, h.StudentId })
                .IsUnique()
                .HasFilter("\"Status\" IN ('HELD', 'CONFIRMED')")
                .HasDatabaseName("IX_AdmissionHolds_ClassId_StudentId_Active");

            // Index 2: Composite index for fast class seat capacity calculation
            entity.HasIndex(h => new { h.ClassId, h.Status })
                .HasDatabaseName("IX_AdmissionHolds_ClassId_Status");

            // Index 3: Student lookup index
            entity.HasIndex(h => h.StudentId)
                .HasDatabaseName("IX_AdmissionHolds_StudentId");

            // Index 4: Partial index for rapid background and transactional expiration sweeps
            entity.HasIndex(h => new { h.Status, h.ExpiresAt })
                .HasFilter("\"Status\" = 'HELD'")
                .HasDatabaseName("IX_AdmissionHolds_Status_ExpiresAt");
        });
    }
}
