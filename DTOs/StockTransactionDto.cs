using System.ComponentModel.DataAnnotations;

namespace InventoryManagementAPI.DTOs
{
    public class StockTransactionDto
    {
        [Required]
        public int TxnTypeId { get; set; }

        [Required]
        public DateTime TxnDate { get; set; } = DateTime.Now;

        public string? ReferenceNo { get; set; }

        public string? Remarks { get; set; }

        [Required]
        public List<StockTransactionLineDto> Lines { get; set; } = new();
    }

    public class StockTransactionLineDto
    {
        [Required]
        public int ItemId { get; set; }

        [Required]
        [Range(0.001, double.MaxValue, ErrorMessage = "Quantity must be greater than 0")]
        public decimal Quantity { get; set; }

        public int? AdjustmentReasonId { get; set; }

        public decimal? UnitPrice { get; set; }

        public string? Remarks { get; set; }
    }
}
