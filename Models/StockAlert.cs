namespace InventoryManagementAPI.Models
{
    public class StockAlert
    {
        public int AlertId { get; set; }
        public int ItemId { get; set; }
        public decimal QtyOnHand { get; set; }
        public decimal MinStockLevel { get; set; }
        public DateTime AlertDate { get; set; } = DateTime.Now;
        public bool IsAcknowledged { get; set; } = false;
        public int? AcknowledgedBy { get; set; }
        public DateTime? AcknowledgedAt { get; set; }

        // Navigation properties
        public Item? Item { get; set; }
        public User? AcknowledgedByUser { get; set; }
    }
}

