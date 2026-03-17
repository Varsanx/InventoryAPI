using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagementAPI.Models
{
    [Table("BatchStock")]
    public class BatchStock
    {
        [Key]
        public int BatchId { get; set; }
        
        public int ItemId { get; set; }
        
        [Required]
        [MaxLength(50)]
        public string BatchNo { get; set; } = string.Empty;
        
        public int InwardLineId { get; set; }
        
        [Column(TypeName = "decimal(18,3)")]
        public decimal OriginalQty { get; set; }
        
        [Column(TypeName = "decimal(18,3)")]
        public decimal RemainingQty { get; set; }
        
        public DateTime ReceivedDate { get; set; }
        
        public DateTime? ExpiryDate { get; set; }
        
        public DateTime CreatedAt { get; set; }

        // ❌ NO NAVIGATION PROPERTIES
        // Don't add: Item, InwardLine
    }

    [Table("BatchConsumption")]
    public class BatchConsumption
    {
        [Key]
        public int ConsumptionId { get; set; }
        
        public int OutwardLineId { get; set; }
        
        public int BatchId { get; set; }
        
        [Column(TypeName = "decimal(18,3)")]
        public decimal QtyConsumed { get; set; }
        
        public DateTime ConsumedAt { get; set; }

        // ❌ NO NAVIGATION PROPERTIES
        // Don't add: OutwardLine, Batch
    }
}
