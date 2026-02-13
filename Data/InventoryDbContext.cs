using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Models;

namespace InventoryManagementAPI.Data
{
    public class InventoryDbContext : DbContext
    {
        public InventoryDbContext(DbContextOptions<InventoryDbContext> options) 
            : base(options)
        {
        }

    
        public DbSet<User> Users { get; set; }
        public DbSet<Category> Categories { get; set; }
        public DbSet<UOM> UOM { get; set; }
        public DbSet<TxnType> TxnTypes { get; set; }
        public DbSet<Reason> Reasons { get; set; }
        public DbSet<Item> Items { get; set; }
        public DbSet<StockTransaction> StockTransactions { get; set; }
        public DbSet<StockTransactionLine> StockTransactionLines { get; set; }
        public DbSet<CurrentStock> CurrentStock { get; set; }
        public DbSet<StockAlert> StockAlerts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Configure Users table
            modelBuilder.Entity<User>(entity =>
            {
                entity.HasKey(e => e.UserId);
                entity.Property(e => e.Username).IsRequired().HasMaxLength(50);
                entity.Property(e => e.PasswordHash).IsRequired().HasMaxLength(255);
                entity.Property(e => e.FullName).IsRequired().HasMaxLength(100);
                entity.Property(e => e.Email).HasMaxLength(100);
                entity.Property(e => e.Role).IsRequired().HasMaxLength(20);
                entity.HasIndex(e => e.Username).IsUnique();
            });

            // Configure Categories table
            modelBuilder.Entity<Category>(entity =>
            {
                entity.HasKey(e => e.CategoryId);
                entity.Property(e => e.CategoryName).IsRequired().HasMaxLength(100);
                entity.HasIndex(e => e.CategoryName).IsUnique();
                
                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.NoAction);
                
                entity.HasOne(e => e.ModifiedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.ModifiedBy)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Configure UOM table
            modelBuilder.Entity<UOM>(entity =>
            {
                entity.HasKey(e => e.UOMId);
                entity.Property(e => e.UOMCode).IsRequired().HasMaxLength(10);
                entity.Property(e => e.UOMDescription).IsRequired().HasMaxLength(100);
                entity.HasIndex(e => e.UOMCode).IsUnique();
                
                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.NoAction);
                
                entity.HasOne(e => e.ModifiedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.ModifiedBy)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Configure TxnTypes table
            modelBuilder.Entity<TxnType>(entity =>
            {
                entity.HasKey(e => e.TxnTypeId);
                entity.Property(e => e.TxnTypeCode).IsRequired().HasMaxLength(20);
                entity.Property(e => e.Description).IsRequired().HasMaxLength(100);
                entity.HasIndex(e => e.TxnTypeCode).IsUnique();
                
                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.NoAction);
                
                entity.HasOne(e => e.ModifiedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.ModifiedBy)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Configure Reasons table
            modelBuilder.Entity<Reason>(entity =>
            {
                entity.HasKey(e => e.ReasonId);
                entity.Property(e => e.ReasonText).IsRequired().HasMaxLength(200);
                entity.HasIndex(e => e.ReasonText).IsUnique();
                
                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.NoAction);
                
                entity.HasOne(e => e.ModifiedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.ModifiedBy)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Configure Items table
            modelBuilder.Entity<Item>(entity =>
            {
                entity.HasKey(e => e.ItemId);
                entity.Property(e => e.ItemCode).IsRequired().HasMaxLength(50);
                entity.Property(e => e.ItemName).IsRequired().HasMaxLength(200);
                entity.Property(e => e.MinStockLevel).HasColumnType("decimal(15,3)");
                entity.HasIndex(e => e.ItemCode).IsUnique();
                
                entity.HasOne(e => e.Category)
                    .WithMany()
                    .HasForeignKey(e => e.CategoryId)
                    .OnDelete(DeleteBehavior.Restrict);
                
                entity.HasOne(e => e.UOM)
                    .WithMany()
                    .HasForeignKey(e => e.UOMId)
                    .OnDelete(DeleteBehavior.Restrict);
                
                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.NoAction);
                
                entity.HasOne(e => e.ModifiedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.ModifiedBy)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Configure StockTransactions table
            modelBuilder.Entity<StockTransaction>(entity =>
            {
                entity.HasKey(e => e.TxnId);
                entity.Property(e => e.ReferenceNo).HasMaxLength(50);
                entity.Property(e => e.Remarks).HasMaxLength(500);
                
                entity.HasOne(e => e.TxnType)
                    .WithMany()
                    .HasForeignKey(e => e.TxnTypeId)
                    .OnDelete(DeleteBehavior.Restrict);
                
                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.NoAction);
                
                entity.HasOne(e => e.ModifiedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.ModifiedBy)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Configure StockTransactionLines table
            modelBuilder.Entity<StockTransactionLine>(entity =>
            {
                entity.HasKey(e => e.LineId);
                entity.Property(e => e.Quantity).HasColumnType("decimal(15,3)");
                entity.Property(e => e.UnitPrice).HasColumnType("decimal(10,2)");
                entity.Property(e => e.TotalAmount).HasColumnType("decimal(15,2)");
                entity.Property(e => e.Remarks).HasMaxLength(500);
                
                entity.HasOne(e => e.Transaction)
                    .WithMany(t => t.Lines)
                    .HasForeignKey(e => e.TxnId)
                    .OnDelete(DeleteBehavior.Cascade);
                
                entity.HasOne(e => e.Item)
                    .WithMany()
                    .HasForeignKey(e => e.ItemId)
                    .OnDelete(DeleteBehavior.Restrict);
                
                entity.HasOne(e => e.AdjustmentReason)
                    .WithMany()
                    .HasForeignKey(e => e.AdjustmentReasonId)
                    .OnDelete(DeleteBehavior.NoAction);
                
                entity.HasOne(e => e.CreatedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.CreatedBy)
                    .OnDelete(DeleteBehavior.NoAction);
            });

            // Configure CurrentStock table
            modelBuilder.Entity<CurrentStock>(entity =>
            {
                entity.HasKey(e => e.ItemId);
                entity.Property(e => e.QtyOnHand).HasColumnType("decimal(15,3)");
                
                entity.HasOne(e => e.Item)
                    .WithMany()
                    .HasForeignKey(e => e.ItemId)
                    .OnDelete(DeleteBehavior.Cascade);
            });

            // Configure StockAlerts table
            modelBuilder.Entity<StockAlert>(entity =>
            {
                entity.HasKey(e => e.AlertId);
                entity.Property(e => e.QtyOnHand).HasColumnType("decimal(15,3)");
                entity.Property(e => e.MinStockLevel).HasColumnType("decimal(15,3)");
                
                entity.HasOne(e => e.Item)
                    .WithMany()
                    .HasForeignKey(e => e.ItemId)
                    .OnDelete(DeleteBehavior.Restrict);
                
                entity.HasOne(e => e.AcknowledgedByUser)
                    .WithMany()
                    .HasForeignKey(e => e.AcknowledgedBy)
                    .OnDelete(DeleteBehavior.NoAction);
            });
        }
    }
}
