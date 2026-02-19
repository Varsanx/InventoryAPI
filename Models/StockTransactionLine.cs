namespace InventoryManagementAPI.Models
{
    public class StockTransactionLine
    {
        public int LineId { get; set; }
        public int TxnId { get; set; }
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public short Direction { get; set; }  // Change to short (smallint in SQL)
        public int? AdjustmentReasonId { get; set; }
        public decimal? UnitPrice { get; set; }
        public decimal? TotalAmount { get; set; }
        public string? Remarks { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int CreatedBy { get; set; }

        // Navigation properties
        public StockTransaction? Transaction { get; set; }
        public Item? Item { get; set; }
        public Reason? AdjustmentReason { get; set; }
        public User? CreatedByUser { get; set; }
    }
}

