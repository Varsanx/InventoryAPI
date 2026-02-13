namespace InventoryManagementAPI.Models
{
    public class Item
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public int CategoryId { get; set; }
        public int UOMId { get; set; }
        public decimal MinStockLevel { get; set; } = 0;
        public bool Status { get; set; } = true;
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int? CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public int? ModifiedBy { get; set; }

        // Navigation properties
        public Category? Category { get; set; }
        public UOM? UOM { get; set; }
        public User? CreatedByUser { get; set; }
        public User? ModifiedByUser { get; set; }
    }
}

