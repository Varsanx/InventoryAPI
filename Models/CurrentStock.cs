namespace InventoryManagementAPI.Models
{
    public class CurrentStock
    {
        public int ItemId { get; set; }
        public decimal QtyOnHand { get; set; } = 0;
        public DateTime UpdatedAt { get; set; } = DateTime.Now;

        // Navigation property
        public Item? Item { get; set; }
    }
}
