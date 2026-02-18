using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class DashboardController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public DashboardController(InventoryDbContext context)
        {
            _context = context;
        }

        // GET: api/Dashboard/Summary
        [HttpGet("Summary")]
        public async Task<ActionResult<object>> GetDashboardSummary()
        {
            var totalItems = await _context.Items.CountAsync(i => i.Status);
            
            var totalCategories = await _context.Categories.CountAsync(c => c.IsActive);
            
            var totalTransactions = await _context.StockTransactions.CountAsync();
            
            var lowStockCount = await _context.CurrentStock
                .Join(_context.Items,
                    cs => cs.ItemId,
                    i => i.ItemId,
                    (cs, i) => new { cs.QtyOnHand, i.MinStockLevel, i.Status })
                .CountAsync(x => x.Status && x.QtyOnHand < x.MinStockLevel && x.QtyOnHand > 0);
            
            var outOfStockCount = await _context.CurrentStock
                .Join(_context.Items,
                    cs => cs.ItemId,
                    i => i.ItemId,
                    (cs, i) => new { cs.QtyOnHand, i.Status })
                .CountAsync(x => x.Status && x.QtyOnHand == 0);
            
            var totalStockValue = await _context.CurrentStock
                .SumAsync(cs => cs.QtyOnHand);

            var unacknowledgedAlerts = await _context.StockAlerts
                .CountAsync(sa => !sa.IsAcknowledged);

            var result = new
            {
                totalItems,
                totalCategories,
                totalTransactions,
                lowStockCount,
                outOfStockCount,
                totalStockValue = Math.Round(totalStockValue, 2),
                unacknowledgedAlerts,
                lastUpdated = DateTime.Now
            };

            return Ok(result);
        }

        // GET: api/Dashboard/LowStock
        [HttpGet("LowStock")]
        public async Task<ActionResult<IEnumerable<object>>> GetLowStockItems([FromQuery] int top = 10)
        {
            var lowStockItems = await _context.CurrentStock
                .Include(cs => cs.Item)
                    .ThenInclude(i => i!.Category)
                .Include(cs => cs.Item)
                    .ThenInclude(i => i!.UOM)
                .Where(cs => cs.Item!.Status && cs.QtyOnHand < cs.Item.MinStockLevel)
                .OrderBy(cs => cs.QtyOnHand)
                .Take(top)
                .Select(cs => new
                {
                    cs.ItemId,
                    ItemCode = cs.Item!.ItemCode,
                    ItemName = cs.Item!.ItemName,
                    CategoryName = cs.Item!.Category!.CategoryName,
                    UOMCode = cs.Item!.UOM!.UOMCode,
                    cs.QtyOnHand,
                    MinStockLevel = cs.Item!.MinStockLevel,
                    Shortage = cs.Item!.MinStockLevel - cs.QtyOnHand,
                    StockStatus = cs.QtyOnHand == 0 ? "OUT OF STOCK" : "LOW STOCK"
                })
                .ToListAsync();

            return Ok(lowStockItems);
        }

        // GET: api/Dashboard/RecentTransactions
        [HttpGet("RecentTransactions")]
        public async Task<ActionResult<IEnumerable<object>>> GetRecentTransactions([FromQuery] int top = 10)
        {
            var recentTransactions = await _context.StockTransactions
                .Include(st => st.TxnType)
                .Include(st => st.Lines)
                    .ThenInclude(l => l.Item)
                .OrderByDescending(st => st.TxnDate)
                .Take(top)
                .Select(st => new
                {
                    st.TxnId,
                    TxnTypeCode = st.TxnType!.TxnTypeCode,
                    TxnTypeDescription = st.TxnType!.Description,
                    st.TxnDate,
                    st.ReferenceNo,
                    ItemCount = st.Lines.Count,
                    TotalQuantity = st.Lines.Sum(l => l.Quantity),
                    st.CreatedAt
                })
                .ToListAsync();

            return Ok(recentTransactions);
        }

        // GET: api/Dashboard/TopMovingItems
        [HttpGet("TopMovingItems")]
        public async Task<ActionResult<IEnumerable<object>>> GetTopMovingItems(
            [FromQuery] int top = 10,
            [FromQuery] int days = 30)
        {
            var fromDate = DateTime.Now.AddDays(-days);

            var topItems = await _context.StockTransactionLines
                .Include(stl => stl.Item)
                    .ThenInclude(i => i!.UOM)
                .Where(stl => stl.CreatedAt >= fromDate)
                .GroupBy(stl => new
                {
                    stl.ItemId,
                    ItemCode = stl.Item!.ItemCode,
                    ItemName = stl.Item!.ItemName,
                    UOMCode = stl.Item!.UOM!.UOMCode
                })
                .Select(g => new
                {
                    g.Key.ItemId,
                    g.Key.ItemCode,
                    g.Key.ItemName,
                    g.Key.UOMCode,
                    TotalInward = g.Where(x => x.Direction == 1).Sum(x => x.Quantity),
                    TotalOutward = g.Where(x => x.Direction == -1).Sum(x => x.Quantity),
                    TotalMovement = g.Sum(x => x.Quantity),
                    TransactionCount = g.Count()
                })
                .OrderByDescending(x => x.TotalMovement)
                .Take(top)
                .ToListAsync();

            return Ok(topItems);
        }

        // GET: api/Dashboard/StockByCategory
        [HttpGet("StockByCategory")]
        public async Task<ActionResult<IEnumerable<object>>> GetStockByCategory()
        {
            var stockByCategory = await _context.Items
                .Include(i => i.Category)
                .Where(i => i.Status)
                .GroupBy(i => new
                {
                    i.CategoryId,
                    CategoryName = i.Category!.CategoryName
                })
                .Select(g => new
                {
                    g.Key.CategoryId,
                    g.Key.CategoryName,
                    ItemCount = g.Count(),
                    TotalMinStockLevel = g.Sum(i => i.MinStockLevel)
                })
                .OrderByDescending(x => x.ItemCount)
                .ToListAsync();

            // Get current stock for each category
            var result = new List<object>();
            foreach (var cat in stockByCategory)
            {
                var itemIds = await _context.Items
                    .Where(i => i.CategoryId == cat.CategoryId && i.Status)
                    .Select(i => i.ItemId)
                    .ToListAsync();

                var totalStock = await _context.CurrentStock
                    .Where(cs => itemIds.Contains(cs.ItemId))
                    .SumAsync(cs => cs.QtyOnHand);

                result.Add(new
                {
                    cat.CategoryId,
                    cat.CategoryName,
                    cat.ItemCount,
                    TotalStock = totalStock,
                    cat.TotalMinStockLevel
                });
            }

            return Ok(result);
        }

        // GET: api/Dashboard/MonthlyTrend
        [HttpGet("MonthlyTrend")]
        public async Task<ActionResult<IEnumerable<object>>> GetMonthlyTrend([FromQuery] int months = 6)
        {
            var fromDate = DateTime.Now.AddMonths(-months).Date;

            var monthlyData = await _context.StockTransactions
                .Include(st => st.TxnType)
                .Include(st => st.Lines)
                .Where(st => st.TxnDate >= fromDate)
                .GroupBy(st => new
                {
                    Year = st.TxnDate.Year,
                    Month = st.TxnDate.Month
                })
                .Select(g => new
                {
                    g.Key.Year,
                    g.Key.Month,
                    MonthName = new DateTime(g.Key.Year, g.Key.Month, 1).ToString("MMM yyyy"),
                    TotalInward = g.Where(st => st.TxnType!.TxnTypeCode == "INWARD")
                                   .Sum(st => st.Lines.Sum(l => l.Quantity)),
                    TotalOutward = g.Where(st => st.TxnType!.TxnTypeCode == "OUTWARD")
                                    .Sum(st => st.Lines.Sum(l => l.Quantity)),
                    TotalAdjustments = g.Where(st => st.TxnType!.TxnTypeCode == "ADJUST")
                                        .Sum(st => st.Lines.Sum(l => l.Quantity * l.Direction)),
                    TransactionCount = g.Count()
                })
                .OrderBy(x => x.Year).ThenBy(x => x.Month)
                .ToListAsync();

            return Ok(monthlyData);
        }
    }
}
