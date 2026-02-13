namespace InventoryManagementAPI.Models
{
    public class StockTransaction
    {
        public int TxnId { get; set; }
        public int TxnTypeId { get; set; }
        public DateTime TxnDate { get; set; } = DateTime.Now;
        public string? ReferenceNo { get; set; }
        public string? Remarks { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public int? ModifiedBy { get; set; }

        // Navigation properties
        public TxnType? TxnType { get; set; }
        public User? CreatedByUser { get; set; }
        public User? ModifiedByUser { get; set; }
        public ICollection<StockTransactionLine> Lines { get; set; } = new List<StockTransactionLine>();
    }
}
