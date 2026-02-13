namespace InventoryManagementAPI.Models
{
    public class UOM
    {
        public int UOMId { get; set; }
        public string UOMCode { get; set; } = string.Empty;
        public string UOMDescription { get; set; } = string.Empty;
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
