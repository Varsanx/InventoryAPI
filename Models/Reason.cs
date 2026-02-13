namespace InventoryManagementAPI.Models
{
    public class Reason
    {
        public int ReasonId { get; set; }
        public string ReasonText { get; set; } = string.Empty;
        public bool IsActive { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int? CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public int? ModifiedBy { get; set; }

        // Navigation properties
        public User? CreatedByUser { get; set; }
        public User? ModifiedByUser { get; set; }
    }
}
