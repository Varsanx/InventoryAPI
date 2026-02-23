namespace InventoryManagementAPI.DTOs
{
    public class ItemDto
    {
        public int ItemId { get; set; }
        public string ItemCode { get; set; } = string.Empty;
        public string ItemName { get; set; } = string.Empty;
        public string CategoryName { get; set; } = string.Empty;
        public string UomCode { get; set; } = string.Empty;
        public decimal MinStockLevel { get; set; }
        public bool Status { get; set; }
        public decimal QtyOnHand { get; set; }
    }
}