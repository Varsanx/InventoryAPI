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

        // ────────────────────────────────────────────────────────────────────────
        // GET: api/Reports/CurrentStock/Export
        // ────────────────────────────────────────────────────────────────────────
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
                .Where(cs => cs.Item != null && cs.Item.Status) // ✅ NULL CHECK
                .AsQueryable();

            if (categoryId.HasValue)
                query = query.Where(cs => cs.Item!.CategoryId == categoryId.Value);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(cs =>
                    cs.Item!.ItemCode.Contains(search) ||
                    cs.Item!.ItemName.Contains(search));

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
                .Where(cs => cs.Item != null && cs.Item.Category != null && cs.Item.UOM != null) // ✅ NULL CHECK
                .OrderBy(cs => cs.Item!.ItemCode)
                .Select(cs => new CurrentStockReportItem
                {
                    ItemCode      = cs.Item!.ItemCode,
                    ItemName      = cs.Item!.ItemName,
                    CategoryName  = cs.Item!.Category!.CategoryName,
                    UOMCode       = cs.Item!.UOM!.UOMCode,
                    QtyOnHand     = cs.QtyOnHand,
                    MinStockLevel = cs.Item!.MinStockLevel,
                    StockStatus   = cs.QtyOnHand == 0 ? "OUT OF STOCK" :
                                    cs.QtyOnHand < cs.Item.MinStockLevel ? "LOW STOCK" :
                                    "IN STOCK",
                    LastUpdated   = cs.UpdatedAt
                })
                .ToListAsync();

            var excelData = _excelService.GenerateCurrentStockReport(data, "Current Stock Report");
            var fileName  = $"CurrentStockReport_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            return File(excelData,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // ────────────────────────────────────────────────────────────────────────
        // GET: api/Reports/MonthlyMovement/Export
        // ────────────────────────────────────────────────────────────────────────
        [HttpGet("MonthlyMovement/Export")]
        public async Task<IActionResult> ExportMonthlyMovementReport(
            [FromQuery] int year,
            [FromQuery] int month,
            [FromQuery] int? categoryId = null,
            [FromQuery] int? itemId = null)
        {
            if (year < 2000 || year > 2100 || month < 1 || month > 12)
                return BadRequest(new { message = "Invalid year or month" });

            var startDate = new DateTime(year, month, 1);
            var endDate   = startDate.AddMonths(1).AddDays(-1);

            var itemsQuery = _context.Items
                .Include(i => i.Category)
                .Include(i => i.UOM)
                .Where(i => i.Status)
                .AsQueryable();

            if (categoryId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.CategoryId == categoryId.Value);

            if (itemId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.ItemId == itemId.Value);

            var items   = await itemsQuery.ToListAsync();
            var itemIds = items.Select(i => i.ItemId).ToList();

            // Single bulk query for opening stocks
            var openingStockMap = await _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                .Where(stl => itemIds.Contains(stl.ItemId) && 
                             stl.Transaction != null && // ✅ NULL CHECK
                             stl.Transaction.TxnDate < startDate)
                .GroupBy(stl => stl.ItemId)
                .Select(g => new
                {
                    ItemId       = g.Key,
                    OpeningStock = g.Sum(t => t.Quantity * t.Direction)
                })
                .ToDictionaryAsync(x => x.ItemId, x => x.OpeningStock);

            // Single bulk query for month transactions
            var allMonthTransactions = await _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                .Where(stl => itemIds.Contains(stl.ItemId) &&
                             stl.Transaction != null && // ✅ NULL CHECK
                             stl.Transaction.TxnDate >= startDate &&
                             stl.Transaction.TxnDate <= endDate)
                .ToListAsync();

            var reportData = new List<MonthlyMovementReportItem>();

            foreach (var item in items)
            {
                // ✅ NULL CHECKS for navigation properties
                if (item.Category == null || item.UOM == null)
                    continue;

                var openingStock      = openingStockMap.TryGetValue(item.ItemId, out var os) ? os : 0m;
                var monthTransactions = allMonthTransactions.Where(t => t.ItemId == item.ItemId).ToList();

                var inward      = monthTransactions.Where(t => t.Direction == 1).Sum(t => t.Quantity);
                var outward     = monthTransactions.Where(t => t.Direction == -1).Sum(t => t.Quantity);
                var adjustments = monthTransactions.Where(t => t.AdjustmentReasonId != null)
                                                   .Sum(t => t.Quantity * t.Direction);

                reportData.Add(new MonthlyMovementReportItem
                {
                    ItemCode     = item.ItemCode,
                    ItemName     = item.ItemName,
                    CategoryName = item.Category.CategoryName,
                    UOMCode      = item.UOM.UOMCode,
                    OpeningStock = openingStock,
                    Inward       = inward,
                    Outward      = outward,
                    Adjustments  = adjustments,
                    ClosingStock = openingStock + inward - outward + adjustments
                });
            }

            var excelData = _excelService.GenerateMonthlyMovementReport(reportData, $"{startDate:MMMM yyyy}", year, month);
            var fileName  = $"MonthlyMovementReport_{year}_{month:00}_{DateTime.Now:yyyyMMdd_HHmmss}.xlsx";

            return File(excelData,
                "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
                fileName);
        }

        // ────────────────────────────────────────────────────────────────────────
        // GET: api/Reports/CurrentStock
        // ────────────────────────────────────────────────────────────────────────
        [HttpGet("CurrentStock")]
        public async Task<ActionResult<IEnumerable<object>>> GetCurrentStockReport(
            [FromQuery] int? categoryId = null,
            [FromQuery] string? search = null,
            [FromQuery] string? stockStatus = null)
        {
            var query = _context.CurrentStock
                .Include(cs => cs.Item)
                    .ThenInclude(i => i!.Category)
                .Include(cs => cs.Item)
                    .ThenInclude(i => i!.UOM)
                .Where(cs => cs.Item != null && cs.Item.Status) // ✅ NULL CHECK
                .AsQueryable();

            if (categoryId.HasValue)
                query = query.Where(cs => cs.Item!.CategoryId == categoryId.Value);

            if (!string.IsNullOrWhiteSpace(search))
                query = query.Where(cs =>
                    cs.Item!.ItemCode.Contains(search) ||
                    cs.Item!.ItemName.Contains(search));

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

            var report = await query
                .Where(cs => cs.Item != null && cs.Item.Category != null && cs.Item.UOM != null) // ✅ NULL CHECK
                .OrderBy(cs => cs.Item!.ItemCode)
                .Select(cs => new
                {
                    cs.ItemId,
                    ItemCode      = cs.Item!.ItemCode,
                    ItemName      = cs.Item!.ItemName,
                    CategoryName  = cs.Item!.Category!.CategoryName,
                    UOMCode       = cs.Item!.UOM!.UOMCode,
                    cs.QtyOnHand,
                    MinStockLevel = cs.Item!.MinStockLevel,
                    StockValue    = cs.QtyOnHand,
                    StockStatus   = cs.QtyOnHand == 0 ? "OUT OF STOCK" :
                                    cs.QtyOnHand < cs.Item.MinStockLevel ? "LOW STOCK" :
                                    "IN STOCK",
                    LastUpdated   = cs.UpdatedAt
                })
                .ToListAsync();

            return Ok(new
            {
                reportDate    = DateTime.Now,
                totalItems    = report.Count,
                totalStockQty = report.Sum(r => r.QtyOnHand),
                data          = report
            });
        }

        // ────────────────────────────────────────────────────────────────────────
        // GET: api/Reports/MonthlyMovement
        // ────────────────────────────────────────────────────────────────────────
        [HttpGet("MonthlyMovement")]
        public async Task<ActionResult<IEnumerable<object>>> GetMonthlyMovementReport(
            [FromQuery] int year,
            [FromQuery] int month,
            [FromQuery] int? categoryId = null,
            [FromQuery] int? itemId = null)
        {
            if (year < 2000 || year > 2100 || month < 1 || month > 12)
                return BadRequest(new { message = "Invalid year or month" });

            var startDate = new DateTime(year, month, 1);
            var endDate   = startDate.AddMonths(1).AddDays(-1);

            var itemsQuery = _context.Items
                .Include(i => i.Category)
                .Include(i => i.UOM)
                .Where(i => i.Status)
                .AsQueryable();

            if (categoryId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.CategoryId == categoryId.Value);

            if (itemId.HasValue)
                itemsQuery = itemsQuery.Where(i => i.ItemId == itemId.Value);

            var items   = await itemsQuery.ToListAsync();
            var itemIds = items.Select(i => i.ItemId).ToList();

            // Single bulk query for opening stocks
            var openingStockMap = await _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                .Where(stl => itemIds.Contains(stl.ItemId) && 
                             stl.Transaction != null && // ✅ NULL CHECK
                             stl.Transaction.TxnDate < startDate)
                .GroupBy(stl => stl.ItemId)
                .Select(g => new
                {
                    ItemId       = g.Key,
                    OpeningStock = g.Sum(t => t.Quantity * t.Direction)
                })
                .ToDictionaryAsync(x => x.ItemId, x => x.OpeningStock);

            // Single bulk query for month transactions
            var allMonthTransactions = await _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                .Where(stl => itemIds.Contains(stl.ItemId) &&
                             stl.Transaction != null && // ✅ NULL CHECK
                             stl.Transaction.TxnDate >= startDate &&
                             stl.Transaction.TxnDate <= endDate)
                .ToListAsync();

            var report = new List<object>();

            foreach (var item in items)
            {
                // ✅ NULL CHECKS for navigation properties
                if (item.Category == null || item.UOM == null)
                    continue;

                var openingStock      = openingStockMap.TryGetValue(item.ItemId, out var os) ? os : 0m;
                var monthTransactions = allMonthTransactions.Where(t => t.ItemId == item.ItemId).ToList();

                var inward      = monthTransactions.Where(t => t.Direction == 1).Sum(t => t.Quantity);
                var outward     = monthTransactions.Where(t => t.Direction == -1).Sum(t => t.Quantity);
                var adjustments = monthTransactions.Where(t => t.AdjustmentReasonId != null)
                                                   .Sum(t => t.Quantity * t.Direction);
                var closingStock = openingStock + inward - outward + adjustments;

                report.Add(new
                {
                    item.ItemId,
                    item.ItemCode,
                    item.ItemName,
                    CategoryName = item.Category.CategoryName,
                    UOMCode      = item.UOM.UOMCode,
                    OpeningStock = openingStock,
                    Inward       = inward,
                    Outward      = outward,
                    Adjustments  = adjustments,
                    ClosingStock = closingStock,
                    Movement     = inward + outward + Math.Abs(adjustments)
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
                data       = report
            });
        }

        // ────────────────────────────────────────────────────────────────────────
        // GET: api/Reports/ItemLedger/{itemId}
        // ────────────────────────────────────────────────────────────────────────
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
                return NotFound(new { message = "Item not found" });

            // ✅ NULL CHECKS for navigation properties
            if (item.Category == null || item.UOM == null)
                return BadRequest(new { message = "Item data is incomplete" });

            var query = _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                    .ThenInclude(t => t!.TxnType) // ✅ NULL-FORGIVING OPERATOR
                .Include(stl => stl.AdjustmentReason)
                .Where(stl => stl.ItemId == itemId && stl.Transaction != null) // ✅ NULL CHECK
                .AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(stl => stl.Transaction!.TxnDate >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(stl => stl.Transaction!.TxnDate <= toDate.Value);

            var transactions = await query
                .OrderBy(stl => stl.Transaction!.TxnDate)
                .ThenBy(stl => stl.LineId)
                .ToListAsync();

            var openingBalance = fromDate.HasValue
                ? await GetStockAtDate(itemId, fromDate.Value.AddDays(-1))
                : 0m;

            decimal runningBalance = openingBalance;
            var ledger = new List<object>();

            foreach (var t in transactions)
            {
                // ✅ NULL CHECK for Transaction and TxnType
                if (t.Transaction == null || t.Transaction.TxnType == null)
                    continue;

                var movement = t.Quantity * t.Direction;
                runningBalance += movement;

                ledger.Add(new
                {
                    t.LineId,
                    t.Transaction.TxnId,
                    TxnDate            = t.Transaction.TxnDate,
                    TxnTypeCode        = t.Transaction.TxnType.TxnTypeCode,
                    TxnTypeDescription = t.Transaction.TxnType.Description,
                    ReferenceNo        = t.Transaction.ReferenceNo,
                    t.Quantity,
                    t.Direction,
                    DirectionText = t.Direction == 1 ? "INWARD" : "OUTWARD",
                    Movement      = movement,
                    Balance       = runningBalance,
                    ReasonText    = t.AdjustmentReason?.ReasonText,
                    t.Remarks,
                    t.CreatedAt
                });
            }

            var currentBalance = await _context.CurrentStock
                .Where(cs => cs.ItemId == itemId)
                .Select(cs => cs.QtyOnHand)
                .FirstOrDefaultAsync();

            return Ok(new
            {
                item.ItemId,
                item.ItemCode,
                item.ItemName,
                CategoryName     = item.Category.CategoryName,
                UOMCode          = item.UOM.UOMCode,
                fromDate,
                toDate,
                openingBalance,
                currentBalance,
                transactionCount = ledger.Count,
                ledger
            });
        }

        // ────────────────────────────────────────────────────────────────────────
        // GET: api/Reports/TransactionSummary
        // ────────────────────────────────────────────────────────────────────────
        [HttpGet("TransactionSummary")]
        public async Task<ActionResult<object>> GetTransactionSummary(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? categoryId = null)
        {
            var query = _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                    .ThenInclude(t => t!.TxnType) // ✅ NULL-FORGIVING OPERATOR
                .Include(stl => stl.Item)
                    .ThenInclude(i => i!.Category) // ✅ NULL-FORGIVING OPERATOR
                .Where(stl => stl.Transaction != null) // ✅ NULL CHECK
                .AsQueryable();

            if (fromDate.HasValue)
                query = query.Where(stl => stl.Transaction!.TxnDate >= fromDate.Value);

            if (toDate.HasValue)
                query = query.Where(stl => stl.Transaction!.TxnDate <= toDate.Value);

            if (categoryId.HasValue)
                query = query.Where(stl => stl.Item != null && stl.Item.CategoryId == categoryId.Value);

            var transactions = await query
                .Where(stl => stl.Transaction!.TxnType != null && 
                             stl.Item != null && 
                             stl.Item.Category != null) // ✅ NULL CHECK
                .ToListAsync();

            var summary = new
            {
                period = new
                {
                    from = fromDate ?? DateTime.MinValue,
                    to   = toDate   ?? DateTime.MaxValue
                },
                totalTransactions = transactions.Select(t => t.TxnId).Distinct().Count(),
                totalLineItems    = transactions.Count,
                byType = transactions
                    .GroupBy(t => new
                    {
                        t.Transaction!.TxnType!.TxnTypeCode,
                        t.Transaction.TxnType.Description
                    })
                    .Select(g => new
                    {
                        txnType          = g.Key.TxnTypeCode,
                        description      = g.Key.Description,
                        transactionCount = g.Select(x => x.TxnId).Distinct().Count(),
                        totalQuantity    = g.Sum(x => x.Quantity),
                        totalValue       = g.Sum(x => x.TotalAmount ?? 0)
                    })
                    .ToList(),
                byCategory = transactions
                    .GroupBy(t => new
                    {
                        t.Item!.Category!.CategoryId,
                        t.Item.Category.CategoryName
                    })
                    .Select(g => new
                    {
                        categoryId    = g.Key.CategoryId,
                        categoryName  = g.Key.CategoryName,
                        itemCount     = g.Select(x => x.ItemId).Distinct().Count(),
                        totalQuantity = g.Sum(x => x.Quantity)
                    })
                    .OrderByDescending(x => x.totalQuantity)
                    .ToList()
            };

            return Ok(summary);
        }

        // ────────────────────────────────────────────────────────────────────────
        // Helper: Calculate stock at a specific date
        // ────────────────────────────────────────────────────────────────────────
        private async Task<decimal> GetStockAtDate(int itemId, DateTime date)
        {
            var transactions = await _context.StockTransactionLines
                .Include(stl => stl.Transaction)
                .Where(stl => stl.ItemId == itemId && 
                             stl.Transaction != null && // ✅ NULL CHECK
                             stl.Transaction.TxnDate <= date)
                .ToListAsync();

            return transactions.Sum(t => t.Quantity * t.Direction);
        }
    }
}
