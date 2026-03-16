using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;
using InventoryManagementAPI.Models;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AdjustmentController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public AdjustmentController(InventoryDbContext context)
        {
            _context = context;
        }

        // ────────────────────────────────────────────────────────────────────────
        // GET: api/Adjustment/Reasons
        // Get all adjustment reasons
        // ────────────────────────────────────────────────────────────────────────
        [HttpGet("Reasons")]
        public async Task<ActionResult<IEnumerable<object>>> GetAdjustmentReasons()
        {
            try
            {
                var reasons = await _context.Reasons
                    .Where(r => r.ReasonType == "Adjustment" && r.IsActive)
                    .OrderBy(r => r.ReasonText)
                    .Select(r => new
                    {
                        r.ReasonId,
                        r.ReasonText,
                        r.ReasonType
                    })
                    .ToListAsync();

                Console.WriteLine($"[ADJUSTMENT] Found {reasons.Count} adjustment reasons");

                return Ok(reasons);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADJUSTMENT] Error fetching reasons: {ex.Message}");
                return StatusCode(500, new { message = "Error fetching adjustment reasons", error = ex.Message });
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // POST: api/Adjustment/Create
        // Create a new adjustment transaction
        // ────────────────────────────────────────────────────────────────────────
        [HttpPost("Create")]
        public async Task<ActionResult> CreateAdjustment([FromBody] CreateAdjustmentRequest request)
        {
            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                Console.WriteLine($"[ADJUSTMENT] Creating adjustment with {request.Items.Count} item(s)");

                // Validate user
                var user = await _context.Users.FindAsync(request.CreatedBy);
                if (user == null)
                {
                    return Unauthorized(new { message = "Invalid user" });
                }

                // Get ADJUST transaction type
                var txnType = await _context.TxnTypes
                    .FirstOrDefaultAsync(t => t.TxnTypeCode == "ADJUST" && t.IsActive);

                if (txnType == null)
                {
                    return BadRequest(new { message = "ADJUST transaction type not found. Please configure TxnTypes table." });
                }

                // Create main transaction
                var stockTransaction = new StockTransaction
                {
                    TxnTypeId = txnType.TxnTypeId,
                    TxnDate = request.TxnDate,
                    ReferenceNo = request.ReferenceNo,
                    Remarks = request.Remarks,
                    CreatedAt = DateTime.Now,
                    CreatedBy = request.CreatedBy
                };

                _context.StockTransactions.Add(stockTransaction);
                await _context.SaveChangesAsync();

                Console.WriteLine($"[ADJUSTMENT] Created transaction TxnId: {stockTransaction.TxnId}");

                // Process each adjustment item
                foreach (var item in request.Items)
                {
                    // Validate item exists
                    var dbItem = await _context.Items.FindAsync(item.ItemId);
                    if (dbItem == null)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = $"Item ID {item.ItemId} not found" });
                    }

                    // Validate adjustment reason
                    var reason = await _context.Reasons.FindAsync(item.AdjustmentReasonId);
                    if (reason == null || reason.ReasonType != "Adjustment")
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = $"Invalid adjustment reason ID {item.AdjustmentReasonId}" });
                    }

                    // Get current stock
                    var currentStock = await _context.CurrentStock
                        .FirstOrDefaultAsync(cs => cs.ItemId == item.ItemId);

                    var currentQty = currentStock?.QtyOnHand ?? 0;

                    // Calculate new quantity
                    decimal newQty;
                    short direction;

                    if (item.AdjustmentType == "INCREASE")
                    {
                        newQty = currentQty + item.Quantity;
                        direction = 1; // Positive adjustment
                        Console.WriteLine($"[ADJUSTMENT] Item {item.ItemId}: {currentQty} + {item.Quantity} = {newQty}");
                    }
                    else if (item.AdjustmentType == "DECREASE")
                    {
                        newQty = currentQty - item.Quantity;
                        direction = -1; // Negative adjustment

                        // Prevent negative stock
                        if (newQty < 0)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { 
                                message = $"Insufficient stock for {dbItem.ItemName}. Current: {currentQty}, Adjustment: {item.Quantity}" 
                            });
                        }

                        Console.WriteLine($"[ADJUSTMENT] Item {item.ItemId}: {currentQty} - {item.Quantity} = {newQty}");
                    }
                    else
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = "Invalid adjustment type. Must be INCREASE or DECREASE" });
                    }

                    // Create transaction line
                    var transactionLine = new StockTransactionLine
                    {
                        TxnId = stockTransaction.TxnId,
                        ItemId = item.ItemId,
                        Quantity = item.Quantity,
                        Direction = direction,
                        AdjustmentReasonId = item.AdjustmentReasonId,
                        Remarks = item.Remarks,
                        CreatedAt = DateTime.Now,
                        CreatedBy = request.CreatedBy
                    };

                    _context.StockTransactionLines.Add(transactionLine);

                    // Update CurrentStock
                    if (currentStock == null)
                    {
                        // Create new stock record
                        currentStock = new CurrentStock
                        {
                            ItemId = item.ItemId,
                            QtyOnHand = newQty,
                            UpdatedAt = DateTime.Now
                        };
                        _context.CurrentStock.Add(currentStock);
                    }
                    else
                    {
                        // Update existing stock
                        currentStock.QtyOnHand = newQty;
                        currentStock.UpdatedAt = DateTime.Now;
                    }

                    // Check if low stock alert needs to be created
                    if (newQty < dbItem.MinStockLevel)
                    {
                        // Check if alert already exists
                        var existingAlert = await _context.StockAlerts
                            .FirstOrDefaultAsync(a => a.ItemId == item.ItemId && !a.IsAcknowledged);

                        if (existingAlert == null)
                        {
                            // Create new alert
                            var alert = new StockAlert
                            {
                                ItemId = item.ItemId,
                                QtyOnHand = newQty,
                                MinStockLevel = dbItem.MinStockLevel,
                                AlertDate = DateTime.Now,
                                IsAcknowledged = false
                            };
                            _context.StockAlerts.Add(alert);
                            Console.WriteLine($"[ADJUSTMENT] Created low stock alert for Item {item.ItemId}");
                        }
                        else
                        {
                            // Update existing alert with new quantity
                            existingAlert.QtyOnHand = newQty;
                            existingAlert.AlertDate = DateTime.Now;
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                Console.WriteLine($"[ADJUSTMENT] Successfully created adjustment TxnId: {stockTransaction.TxnId}");

                return Ok(new
                {
                    message = "Stock adjustment created successfully",
                    txnId = stockTransaction.TxnId,
                    txnDate = stockTransaction.TxnDate,
                    itemsAdjusted = request.Items.Count
                });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"[ADJUSTMENT] Error: {ex.Message}");
                Console.WriteLine($"[ADJUSTMENT] Stack: {ex.StackTrace}");
                return StatusCode(500, new { message = "Error creating adjustment", error = ex.Message });
            }
        }

        // ────────────────────────────────────────────────────────────────────────
        // GET: api/Adjustment/History
        // Get adjustment history
        // ────────────────────────────────────────────────────────────────────────
        [HttpGet("History")]
        public async Task<ActionResult<IEnumerable<object>>> GetAdjustmentHistory(
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            try
            {
                var query = _context.StockTransactions
                    .Include(st => st.TxnType)
                    .Include(st => st.Lines)
                        .ThenInclude(line => line.Item)
                    .Include(st => st.Lines)
                        .ThenInclude(line => line.AdjustmentReason)
                    .Where(st => st.TxnType!.TxnTypeCode == "ADJUST")
                    .AsQueryable();

                if (fromDate.HasValue)
                    query = query.Where(st => st.TxnDate >= fromDate.Value);

                if (toDate.HasValue)
                    query = query.Where(st => st.TxnDate <= toDate.Value);

                var adjustments = await query
                    .OrderByDescending(st => st.TxnDate)
                    .Select(st => new
                    {
                        st.TxnId,
                        st.TxnDate,
                        st.ReferenceNo,
                        st.Remarks,
                        st.CreatedAt,
                        ItemsAdjusted = st.Lines!.Count,
                        Items = st.Lines!.Select(line => new
                        {
                            line.LineId,
                            ItemCode = line.Item!.ItemCode,
                            ItemName = line.Item.ItemName,
                            Quantity = line.Quantity,
                            AdjustmentType = line.Direction == 1 ? "INCREASE" : "DECREASE",
                            ReasonText = line.AdjustmentReason!.ReasonText,
                            line.Remarks
                        })
                    })
                    .ToListAsync();

                return Ok(adjustments);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[ADJUSTMENT] Error fetching history: {ex.Message}");
                return StatusCode(500, new { message = "Error fetching adjustment history", error = ex.Message });
            }
        }
    }

    // ────────────────────────────────────────────────────────────────────────
    // REQUEST MODELS
    // ────────────────────────────────────────────────────────────────────────

    public class CreateAdjustmentRequest
    {
        public DateTime TxnDate { get; set; }
        public string ReferenceNo { get; set; } = string.Empty;
        public string? Remarks { get; set; }
        public int CreatedBy { get; set; }
        public List<AdjustmentItemRequest> Items { get; set; } = new();
    }

    public class AdjustmentItemRequest
    {
        public int ItemId { get; set; }
        public decimal Quantity { get; set; }
        public string AdjustmentType { get; set; } = "DECREASE"; // "INCREASE" or "DECREASE"
        public int AdjustmentReasonId { get; set; }
        public string? Remarks { get; set; }
    }
}
