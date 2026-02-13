using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;
using InventoryManagementAPI.Models;
using InventoryManagementAPI.DTOs;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class StockTransactionsController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public StockTransactionsController(InventoryDbContext context)
        {
            _context = context;
        }

        // GET: api/StockTransactions
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetTransactions(
            [FromQuery] int? txnTypeId = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null,
            [FromQuery] int? itemId = null)
        {
            var query = _context.StockTransactions
                .Include(st => st.TxnType)
                .Include(st => st.Lines)
                    .ThenInclude(l => l.Item)
                .AsQueryable();

            // Filter by transaction type
            if (txnTypeId.HasValue)
            {
                query = query.Where(st => st.TxnTypeId == txnTypeId.Value);
            }

            // Filter by date range
            if (fromDate.HasValue)
            {
                query = query.Where(st => st.TxnDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(st => st.TxnDate <= toDate.Value);
            }

            // Filter by item
            if (itemId.HasValue)
            {
                query = query.Where(st => st.Lines.Any(l => l.ItemId == itemId.Value));
            }

            var transactions = await query
                .OrderByDescending(st => st.TxnDate)
                .Select(st => new
                {
                    st.TxnId,
                    st.TxnTypeId,
                    TxnTypeCode = st.TxnType!.TxnTypeCode,
                    TxnTypeDescription = st.TxnType!.Description,
                    st.TxnDate,
                    st.ReferenceNo,
                    st.Remarks,
                    st.CreatedAt,
                    LineCount = st.Lines.Count,
                    TotalQuantity = st.Lines.Sum(l => l.Quantity * l.Direction)
                })
                .ToListAsync();

            return Ok(transactions);
        }

        // GET: api/StockTransactions/5
        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetTransaction(int id)
        {
            var transaction = await _context.StockTransactions
                .Include(st => st.TxnType)
                .Include(st => st.Lines)
                    .ThenInclude(l => l.Item)
                        .ThenInclude(i => i!.UOM)
                .Include(st => st.Lines)
                    .ThenInclude(l => l.AdjustmentReason)
                .FirstOrDefaultAsync(st => st.TxnId == id);

            if (transaction == null)
            {
                return NotFound(new { message = "Transaction not found" });
            }

            var result = new
            {
                transaction.TxnId,
                transaction.TxnTypeId,
                TxnTypeCode = transaction.TxnType!.TxnTypeCode,
                TxnTypeDescription = transaction.TxnType!.Description,
                transaction.TxnDate,
                transaction.ReferenceNo,
                transaction.Remarks,
                transaction.CreatedAt,
                transaction.CreatedBy,
                Lines = transaction.Lines.Select(l => new
                {
                    l.LineId,
                    l.ItemId,
                    ItemCode = l.Item!.ItemCode,
                    ItemName = l.Item!.ItemName,
                    UOMCode = l.Item!.UOM!.UOMCode,
                    l.Quantity,
                    l.Direction,
                    DirectionText = l.Direction == 1 ? "INWARD" : "OUTWARD",
                    l.AdjustmentReasonId,
                    ReasonText = l.AdjustmentReason?.ReasonText,
                    l.UnitPrice,
                    l.TotalAmount,
                    l.Remarks
                }).ToList()
            };

            return Ok(result);
        }

        // POST: api/StockTransactions/Inward
        [HttpPost("Inward")]
        public async Task<ActionResult> CreateInwardTransaction(StockTransactionDto dto)
        {
            // Get Inward transaction type
            var inwardType = await _context.TxnTypes
                .FirstOrDefaultAsync(t => t.TxnTypeCode == "INWARD");

            if (inwardType == null)
            {
                return BadRequest(new { message = "Inward transaction type not found" });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Create transaction header
                var stockTxn = new StockTransaction
                {
                    TxnTypeId = inwardType.TxnTypeId,
                    TxnDate = dto.TxnDate,
                    ReferenceNo = dto.ReferenceNo,
                    Remarks = dto.Remarks,
                    CreatedAt = DateTime.Now,
                    CreatedBy = 1 // TODO: Get from authenticated user
                };

                _context.StockTransactions.Add(stockTxn);
                await _context.SaveChangesAsync();

                // Create transaction lines and update stock
                foreach (var lineDto in dto.Lines)
                {
                    // Validate item exists
                    var item = await _context.Items.FindAsync(lineDto.ItemId);
                    if (item == null)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = $"Item with ID {lineDto.ItemId} not found" });
                    }

                    if (!item.Status)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = $"Item {item.ItemCode} is inactive" });
                    }

                    // Create line
                    var line = new StockTransactionLine
                    {
                        TxnId = stockTxn.TxnId,
                        ItemId = lineDto.ItemId,
                        Quantity = lineDto.Quantity,
                        Direction = 1, // Inward
                        UnitPrice = lineDto.UnitPrice,
                        TotalAmount = lineDto.Quantity * (lineDto.UnitPrice ?? 0),
                        Remarks = lineDto.Remarks,
                        CreatedAt = DateTime.Now,
                        CreatedBy = 1
                    };

                    _context.StockTransactionLines.Add(line);

                    // Update current stock
                    var currentStock = await _context.CurrentStock
                        .FirstOrDefaultAsync(cs => cs.ItemId == lineDto.ItemId);

                    if (currentStock == null)
                    {
                        currentStock = new CurrentStock
                        {
                            ItemId = lineDto.ItemId,
                            QtyOnHand = lineDto.Quantity,
                            UpdatedAt = DateTime.Now
                        };
                        _context.CurrentStock.Add(currentStock);
                    }
                    else
                    {
                        currentStock.QtyOnHand += lineDto.Quantity;
                        currentStock.UpdatedAt = DateTime.Now;
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetTransaction), new { id = stockTxn.TxnId }, new { txnId = stockTxn.TxnId });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error creating transaction", error = ex.Message });
            }
        }

        // POST: api/StockTransactions/Outward
        [HttpPost("Outward")]
        public async Task<ActionResult> CreateOutwardTransaction(StockTransactionDto dto)
        {
            // Get Outward transaction type
            var outwardType = await _context.TxnTypes
                .FirstOrDefaultAsync(t => t.TxnTypeCode == "OUTWARD");

            if (outwardType == null)
            {
                return BadRequest(new { message = "Outward transaction type not found" });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                // Validate stock availability for all items BEFORE creating transaction
                foreach (var lineDto in dto.Lines)
                {
                    var currentStock = await _context.CurrentStock
                        .FirstOrDefaultAsync(cs => cs.ItemId == lineDto.ItemId);

                    var availableQty = currentStock?.QtyOnHand ?? 0;

                    if (availableQty < lineDto.Quantity)
                    {
                        var item = await _context.Items.FindAsync(lineDto.ItemId);
                        return BadRequest(new
                        {
                            message = $"Insufficient stock for {item?.ItemCode}. Available: {availableQty}, Requested: {lineDto.Quantity}"
                        });
                    }
                }

                // Create transaction header
                var stockTxn = new StockTransaction
                {
                    TxnTypeId = outwardType.TxnTypeId,
                    TxnDate = dto.TxnDate,
                    ReferenceNo = dto.ReferenceNo,
                    Remarks = dto.Remarks,
                    CreatedAt = DateTime.Now,
                    CreatedBy = 1
                };

                _context.StockTransactions.Add(stockTxn);
                await _context.SaveChangesAsync();

                // Create lines and update stock
                foreach (var lineDto in dto.Lines)
                {
                    var item = await _context.Items.FindAsync(lineDto.ItemId);
                    if (!item!.Status)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = $"Item {item.ItemCode} is inactive" });
                    }

                    var line = new StockTransactionLine
                    {
                        TxnId = stockTxn.TxnId,
                        ItemId = lineDto.ItemId,
                        Quantity = lineDto.Quantity,
                        Direction = -1, // Outward
                        UnitPrice = lineDto.UnitPrice,
                        TotalAmount = lineDto.Quantity * (lineDto.UnitPrice ?? 0),
                        Remarks = lineDto.Remarks,
                        CreatedAt = DateTime.Now,
                        CreatedBy = 1
                    };

                    _context.StockTransactionLines.Add(line);

                    // Update current stock
                    var currentStock = await _context.CurrentStock
                        .FirstOrDefaultAsync(cs => cs.ItemId == lineDto.ItemId);

                    currentStock!.QtyOnHand -= lineDto.Quantity;
                    currentStock.UpdatedAt = DateTime.Now;

                    // Check if stock falls below minimum and create alert
                    if (currentStock.QtyOnHand < item.MinStockLevel)
                    {
                        var existingAlert = await _context.StockAlerts
                            .Where(sa => sa.ItemId == lineDto.ItemId && !sa.IsAcknowledged)
                            .FirstOrDefaultAsync();

                        if (existingAlert == null)
                        {
                            var alert = new StockAlert
                            {
                                ItemId = lineDto.ItemId,
                                QtyOnHand = currentStock.QtyOnHand,
                                MinStockLevel = item.MinStockLevel,
                                AlertDate = DateTime.Now,
                                IsAcknowledged = false
                            };
                            _context.StockAlerts.Add(alert);
                        }
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetTransaction), new { id = stockTxn.TxnId }, new { txnId = stockTxn.TxnId });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error creating transaction", error = ex.Message });
            }
        }

        // POST: api/StockTransactions/Adjustment
        [HttpPost("Adjustment")]
        public async Task<ActionResult> CreateAdjustmentTransaction(StockTransactionDto dto)
        {
            // Get Adjustment transaction type
            var adjustType = await _context.TxnTypes
                .FirstOrDefaultAsync(t => t.TxnTypeCode == "ADJUST");

            if (adjustType == null)
            {
                return BadRequest(new { message = "Adjustment transaction type not found" });
            }

            // Validate that all lines have adjustment reason
            if (dto.Lines.Any(l => !l.AdjustmentReasonId.HasValue))
            {
                return BadRequest(new { message = "Adjustment reason is required for all lines" });
            }

            using var transaction = await _context.Database.BeginTransactionAsync();

            try
            {
                var stockTxn = new StockTransaction
                {
                    TxnTypeId = adjustType.TxnTypeId,
                    TxnDate = dto.TxnDate,
                    ReferenceNo = dto.ReferenceNo,
                    Remarks = dto.Remarks,
                    CreatedAt = DateTime.Now,
                    CreatedBy = 1
                };

                _context.StockTransactions.Add(stockTxn);
                await _context.SaveChangesAsync();

                foreach (var lineDto in dto.Lines)
                {
                    var item = await _context.Items.FindAsync(lineDto.ItemId);
                    if (item == null || !item.Status)
                    {
                        await transaction.RollbackAsync();
                        return BadRequest(new { message = $"Item with ID {lineDto.ItemId} not found or inactive" });
                    }

                    // Determine direction based on context in remarks or default to increase
                    // You can add a Direction field to DTO if you want explicit control
                    sbyte direction = 1; // Default to increase
                    
                    // Check if adjustment is decrease (you can customize this logic)
                    if (lineDto.Remarks?.ToLower().Contains("decrease") == true ||
                        lineDto.Remarks?.ToLower().Contains("reduce") == true)
                    {
                        direction = -1;
                    }

                    var line = new StockTransactionLine
                    {
                        TxnId = stockTxn.TxnId,
                        ItemId = lineDto.ItemId,
                        Quantity = lineDto.Quantity,
                        Direction = direction,
                        AdjustmentReasonId = lineDto.AdjustmentReasonId,
                        Remarks = lineDto.Remarks,
                        CreatedAt = DateTime.Now,
                        CreatedBy = 1
                    };

                    _context.StockTransactionLines.Add(line);

                    // Update current stock
                    var currentStock = await _context.CurrentStock
                        .FirstOrDefaultAsync(cs => cs.ItemId == lineDto.ItemId);

                    if (currentStock == null)
                    {
                        currentStock = new CurrentStock
                        {
                            ItemId = lineDto.ItemId,
                            QtyOnHand = direction * lineDto.Quantity,
                            UpdatedAt = DateTime.Now
                        };
                        _context.CurrentStock.Add(currentStock);
                    }
                    else
                    {
                        currentStock.QtyOnHand += (direction * lineDto.Quantity);
                        currentStock.UpdatedAt = DateTime.Now;
                    }
                }

                await _context.SaveChangesAsync();
                await transaction.CommitAsync();

                return CreatedAtAction(nameof(GetTransaction), new { id = stockTxn.TxnId }, new { txnId = stockTxn.TxnId });
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                return StatusCode(500, new { message = "Error creating adjustment", error = ex.Message });
            }
        }
    }
}
