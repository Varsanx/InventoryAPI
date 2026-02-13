using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;
using InventoryManagementAPI.Services;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReportsController : ControllerBase
    {
        private readonly InventoryDbContext _context;
private readonly ExcelService _excelService;

public ReportsController(InventoryDbContext context, ExcelService excelService)
{
    _context = context;
    _excelService = excelService;
}

        // GET: api/Reports/CurrentStock/Export
[HttpGet("CurrentStock/Export")]
public async Task<IActionResult> ExportCurrentStockReport(
    [FromQuery] int? categoryId = null,
    [FromQuery] string? search = null,
    [FromQuery] string? stockStatus = null)
{
    var query = _context.CurrentStock
        .Include(cs => cs.Item)
            .ThenInclude(i => i!.Category)
        .Include(cs => cs.Item)
            .ThenInclude(i => i!.UOM)
        .Where(cs => cs.Item!.Status)
        .AsQueryable();

    if (categoryId.HasValue)
    {
        query = query.Where(cs => cs.Item!.CategoryId == categoryId.Value);
    }

    if (!string.IsNullOrWhiteSpace(search))
    {
        query = query.Where(cs =>
            cs.Item!.ItemCode.Contains(search) ||
            cs.Item!.ItemName.Contains(search));
    }

    if (!string.IsNullOrWhiteSpace(stockStatus))
    {
        switch (stockStatus.ToLower())
        {
            case "low":
                query = query.Where(cs => cs.QtyOnHand < cs.Item!.MinStockLevel && cs.QtyOnHand > 0);
                break;
            case "out":
                query = query.Where(cs => cs.QtyOnHand == 0);
                break;
            case "available":
                query = query.Where(cs => cs.QtyOnHand >= cs.Item!.MinStockLevel);
                break;
        }
    }

    var data = await query
        .OrderBy(cs => cs.Item!.ItemCode)
        .Select(cs => new CurrentStockReportItem
        {
            ItemCode = cs.Item!.ItemCode,
            ItemName = cs.Item!.ItemName,
            CategoryName = cs.Item!.Category!.CategoryName,
            UOMCode = cs.Item!.UOM!.UOMCode,
            QtyOnHand = cs.QtyOnHand,
            MinStockLevel = cs.Item!.MinStockLevel,
            StockStatus = cs.QtyOnHand == 0 ? "OUT OF STOCK" :
                         cs.QtyOnHand < cs.Item.MinStockLevel ? "LOW STOCK" :
                         "IN STOCK",
            LastUpdated = cs.UpdatedAt
        })
        .ToListAsync();

    var excelData = _excelService.GenerateCurrentStockReport(
        data,
        "Current Stock Report"
    );

    var fileName = $"CurrentStockReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

    return File(excelData, 
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        fileName);
}

// GET: api/Reports/MonthlyMovement/Export
[HttpGet("MonthlyMovement/Export")]
public async Task<IActionResult> ExportMonthlyMovementReport(
    [FromQuery] int year,
    [FromQuery] int month,
    [FromQuery] int? categoryId = null,
    [FromQuery] int? itemId = null)
{
    if (year < 2000 || year > 2100 || month < 1 || month > 12)
    {
        return BadRequest(new { message = "Invalid year or month" });
    }

    var startDate = new DateTime(year, month, 1);
    var endDate = startDate.AddMonths(1).AddDays(-1);

    var itemsQuery = _context.Items
        .Include(i => i.Category)
        .Include(i => i.UOM)
        .Where(i => i.Status)
        .AsQueryable();

    if (categoryId.HasValue)
    {
        itemsQuery = itemsQuery.Where(i => i.CategoryId == categoryId.Value);
    }

    if (itemId.HasValue)
    {
        itemsQuery = itemsQuery.Where(i => i.ItemId == itemId.Value);
    }

    var items = await itemsQuery.ToListAsync();

    var reportData = new List<MonthlyMovementReportItem>();

    foreach (var item in items)
    {
        var openingStock = await GetStockAtDate(item.ItemId, startDate.AddDays(-1));

        var monthTransactions = await _context.StockTransactionLines
            .Include(stl => stl.Transaction)
            .Where(stl => stl.ItemId == item.ItemId &&
                         stl.Transaction.TxnDate >= startDate &&
                         stl.Transaction.TxnDate <= endDate)
            .ToListAsync();

        var inward = monthTransactions
            .Where(t => t.Direction == 1)
            .Sum(t => t.Quantity);

        var outward = monthTransactions
            .Where(t => t.Direction == -1)
            .Sum(t => t.Quantity);

        var adjustments = monthTransactions
            .Where(t => t.AdjustmentReasonId != null)
            .Sum(t => t.Quantity * t.Direction);

        reportData.Add(new MonthlyMovementReportItem
        {
            ItemCode = item.ItemCode,
            ItemName = item.ItemName,
            CategoryName = item.Category!.CategoryName,
            UOMCode = item.UOM!.UOMCode,
            OpeningStock = openingStock,
            Inward = inward,
            Outward = outward,
            Adjustments = adjustments,
            ClosingStock = openingStock + inward - outward + adjustments
        });
    }

    var excelData = _excelService.GenerateMonthlyMovementReport(
        reportData,
        $"{startDate:MMMM yyyy}",
        year,
        month
    );

    var fileName = $"MonthlyMovementReport_{year}_{month:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

    return File(excelData,
        "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        fileName);
}

        // GET: api/Reports/CurrentStock
        [HttpGet("CurrentStock")]
        public async Task<ActionResult<IEnumerable<object>>> GetCurrentStockReport(
            [FromQuery] int? categoryId = null,
            [FromQuery] string? search = null,
            [FromQuery] string? stockStatus = null) // "all", "low", "out"
        {
            var query = _context.CurrentStock
                .Include(cs => cs.Item)
                    .ThenInclude(i => i!.Category)
                .Include(cs => cs.Item)
                    .ThenInclude(i => i!.UOM)
                .Where(cs => cs.Item!.Status)
                .AsQueryable();

            // Filter by category
            if (categoryId.HasValue)
            {
                query = query.Where(cs => cs.Item!.CategoryId == categoryId.Value);
            }

            // Filter by search
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(cs =>
                    cs.Item!.ItemCode.Contains(search) ||
                    cs.Item!.ItemName.Contains(search));
            }

            // Filter by stock status
            if (!string.IsNullOrWhiteSpace(stockStatus))
            {
                switch (stockStatus.ToLower())
                {
                    case "low":
                        query = query.Where(cs => cs.QtyOnHand < cs.Item!.MinStockLevel && cs.QtyOnHand > 0);
                        break;
                    case "out":
                        query = query.Where(cs => cs.QtyOnHand == 0);
                        break;
                    case "available":
                        query = query.Where(cs => cs.QtyOnHand >= cs.Item!.MinStockLevel);
                        break;
                    // "all" or default shows everything
                }
            }

            var report = await query
                .OrderBy(cs => cs.Item!.ItemCode)
                .Select(cs => new
                {
                    cs.ItemId,
                    ItemCode = cs.Item!.ItemCode,
                    ItemName = cs.Item!.ItemName,
                    CategoryName = cs.Item!.Category!.CategoryName,
                    UOMCode = cs.Item!.UOM!.UOMCode,
                    cs.QtyOnHand,
                    MinStockLevel = cs.Item!.MinStockLevel,
                    StockValue = cs.QtyOnHand, // Can be multiplied by unit price if available
                    StockStatus = cs.QtyOnHand == 0 ? "OUT OF STOCK" :
                                 cs.QtyOnHand < cs.Item.MinStockLevel ? "LOW STOCK" :
                                 "IN STOCK",
                    LastUpdated = cs.UpdatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                reportDate = DateTime.Now,
                totalItems = report.Count,
                totalStockQty = report.Sum(r => r.QtyOnHand),
                data = report
            });
        }

        // GET: api/Reports/MonthlyMovement
        [HttpGet("MonthlyMovement")]
        public async Task<ActionResult<IEnumerable<object>>> GetMonthlyMovementReport(
            [FromQuery] int year,
            [FromQuery] int month,
            [FromQuery] int? categoryId = null,
            [FromQuery] int? itemId = null)
        {
            if (year < 2000 || year > 2100 || month < 1 || month > 12)
            {
                return BadRequest(new { message = "Invalid year or month" });
            }

            var startDate = new DateTime(year, month, 1);
            var endDate = startDate.AddMonths(1).AddDays(-1);

            // Get all items
            var itemsQuery = _context.Items
                .Include(i => i.Category)
                .Include(i => i.UOM)
                .Where(i => i.Status)
                .AsQueryable();

            if (categoryId.HasValue)
            {
                itemsQuery = itemsQuery.Where(i => i.CategoryId == categoryId.Value);
            }

            if (itemId.HasValue)
            {
                itemsQuery = itemsQuery.Where(i => i.ItemId == itemId.Value);
            }

            var items = await itemsQuery.ToListAsync();

            var report = new List<object>();

            foreach (var item in items)
            {
                // Get opening stock (stock at start of month)
                var openingStock = await GetStockAtDate(item.ItemId, startDate.AddDays(-1));

                // Get transactions during the month
                var monthTransactions = await _context.StockTransactionLines
                    .Include(stl => stl.Transaction)
                    .Where(stl => stl.ItemId == item.ItemId &&
                                 stl.Transaction.TxnDate >= startDate &&
                                 stl.Transaction.TxnDate <= endDate)
                    .ToListAsync();

                var inward = monthTransactions
                    .Where(t => t.Direction == 1)
                    .Sum(t => t.Quantity);

                var outward = monthTransactions
                    .Where(t => t.Direction == -1)
                    .Sum(t => t.Quantity);

                // Adjustments (can be positive or negative)
                var adjustments = monthTransactions
                    .Where(t => t.AdjustmentReasonId != null)
                    .Sum(t => t.Quantity * t.Direction);

                var closingStock = openingStock + inward - outward + adjustments;

                report.Add(new
                {
                    item.ItemId,
                    item.ItemCode,
                    item.ItemName,
                    CategoryName = item.Category!.CategoryName,
                    UOMCode = item.UOM!.UOMCode,
                    OpeningStock = openingStock,
                    Inward = inward,
                    Outward = outward,
                    Adjustments = adjustments,
                    ClosingStock = closingStock,
                    Movement = inward + outward + Math.Abs(adjustments)
                });
            }

            return Ok(new
            {
                reportPeriod = $"{startDate:MMMM yyyy}",
                year,
                month,
                startDate,
                endDate,
                totalItems = report.Count,
                data = report
            });
        }

        // GET: api/Reports/ItemLedger
        [HttpGet("ItemLedger/{itemId}")]
        public async Task<ActionResult<object>> GetItemLedger(
            int itemId,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            var item = await _context.Items
                .Include(i => i.Category)
                .Include(i => i.UOM)
                .FirstOrDefaultAsync(i => i.ItemId == itemId);

            if (item == null)
            {
                return NotFound(new { message = "Item not found" });
            }

            var query = _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                    .ThenInclude(t => t.TxnType)
                .Include(stl => stl.AdjustmentReason)
                .Where(stl => stl.ItemId == itemId)
                .AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(stl => stl.Transaction.TxnDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(stl => stl.Transaction.TxnDate <= toDate.Value);
            }

            var transactions = await query
                .OrderBy(stl => stl.Transaction.TxnDate)
                .ThenBy(stl => stl.LineId)
                .ToListAsync();

            // Calculate running balance
            decimal runningBalance = fromDate.HasValue 
                ? await GetStockAtDate(itemId, fromDate.Value.AddDays(-1))
                : 0;

            var ledger = transactions.Select(t =>
            {
                var movement = t.Quantity * t.Direction;
                runningBalance += movement;

                return new
                {
                    t.LineId,
                    t.Transaction.TxnId,
                    TxnDate = t.Transaction.TxnDate,
                    TxnTypeCode = t.Transaction.TxnType!.TxnTypeCode,
                    TxnTypeDescription = t.Transaction.TxnType!.Description,
                    ReferenceNo = t.Transaction.ReferenceNo,
                    t.Quantity,
                    t.Direction,
                    DirectionText = t.Direction == 1 ? "INWARD" : "OUTWARD",
                    Movement = movement,
                    Balance = runningBalance,
                    ReasonText = t.AdjustmentReason?.ReasonText,
                    t.Remarks,
                    t.CreatedAt
                };
            }).ToList();

            return Ok(new
            {
                item.ItemId,
                item.ItemCode,
                item.ItemName,
                CategoryName = item.Category!.CategoryName,
                UOMCode = item.UOM!.UOMCode,
                fromDate,
                toDate,
                openingBalance = fromDate.HasValue 
                    ? await GetStockAtDate(itemId, fromDate.Value.AddDays(-1))
                    : 0,
                currentBalance = await _context.CurrentStock
                    .Where(cs => cs.ItemId == itemId)
                    .Select(cs => cs.QtyOnHand)
                    .FirstOrDefaultAsync(),
                transactionCount = ledger.Count,
                ledger
            });
        }

        // GET: api/Reports/TransactionSummary
        [HttpGet("TransactionSummary")]
        public async Task<ActionResult<object>> GetTransactionSummary(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? categoryId = null)
        {
            var query = _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                    .ThenInclude(t => t.TxnType)
                .Include(stl => stl.Item)
                    .ThenInclude(i => i!.Category)
                .AsQueryable();

            if (fromDate.HasValue)
            {
                query = query.Where(stl => stl.Transaction.TxnDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(stl => stl.Transaction.TxnDate <= toDate.Value);
            }

            if (categoryId.HasValue)
            {
                query = query.Where(stl => stl.Item!.CategoryId == categoryId.Value);
            }

            var transactions = await query.ToListAsync();

            var summary = new
            {
                period = new
                {
                    from = fromDate ?? DateTime.MinValue,
                    to = toDate ?? DateTime.MaxValue
                },
                totalTransactions = transactions.Select(t => t.TxnId).Distinct().Count(),
                totalLineItems = transactions.Count,
                byType = transactions
                    .GroupBy(t => new
                    {
                        t.Transaction.TxnType!.TxnTypeCode,
                        t.Transaction.TxnType!.Description
                    })
                    .Select(g => new
                    {
                        txnType = g.Key.TxnTypeCode,
                        description = g.Key.Description,
                        transactionCount = g.Select(x => x.TxnId).Distinct().Count(),
                        totalQuantity = g.Sum(x => x.Quantity),
                        totalValue = g.Sum(x => x.TotalAmount ?? 0)
                    })
                    .ToList(),
                byCategory = transactions
                    .GroupBy(t => new
                    {
                        t.Item!.Category!.CategoryId,
                        t.Item!.Category!.CategoryName
                    })
                    .Select(g => new
                    {
                        categoryId = g.Key.CategoryId,
                        categoryName = g.Key.CategoryName,
                        itemCount = g.Select(x => x.ItemId).Distinct().Count(),
                        totalQuantity = g.Sum(x => x.Quantity)
                    })
                    .OrderByDescending(x => x.totalQuantity)
                    .ToList()
            };

            return Ok(summary);
        }

        // Helper method to calculate stock at a specific date
        private async Task<decimal> GetStockAtDate(int itemId, DateTime date)
        {
            var transactions = await _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                .Where(stl => stl.ItemId == itemId && stl.Transaction.TxnDate <= date)
                .ToListAsync();

            return transactions.Sum(t => t.Quantity * t.Direction);
        }
        
    }
}