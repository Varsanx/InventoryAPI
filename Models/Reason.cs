using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace InventoryManagementAPI.Models
{
    [Table("Reasons")]
    public class Reason
    {
        [Key]
        public int ReasonId { get; set; }

        [Required]
        [MaxLength(200)]
        public string ReasonText { get; set; } = string.Empty;

        [MaxLength(50)]
        public string? ReasonType { get; set; }

        public bool IsActive { get; set; } = true;

        // ✅ ADD AUDIT FIELDS (if your database has them)
        public DateTime? CreatedAt { get; set; }
        
        public int? CreatedBy { get; set; }
        
        public DateTime? ModifiedAt { get; set; }
        
        public int? ModifiedBy { get; set; }

        // ✅ NAVIGATION PROPERTIES (optional, for EF Core relationships)
        [ForeignKey("CreatedBy")]
        public virtual User? CreatedByUser { get; set; }

        [ForeignKey("ModifiedBy")]
        public virtual User? ModifiedByUser { get; set; }
    }
}
