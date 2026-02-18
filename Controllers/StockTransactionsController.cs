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
            try
            {
                // Get the transaction
                var transaction = await _context.StockTransactions
                    .Include(st => st.TxnType)
                    .FirstOrDefaultAsync(st => st.TxnId == id);

                if (transaction == null)
                {
                    return NotFound(new { message = $"Transaction {id} not found" });
                }

                // Get all lines for this transaction
                var transactionLines = await _context.StockTransactionLines
                    .Where(l => l.TxnId == id)
                    .ToListAsync();

                // Build line items with item details
                var lines = new List<object>();
                
                foreach (var line in transactionLines)
                {
                    var item = await _context.Items
                        .Include(i => i.UOM)
                        .FirstOrDefaultAsync(i => i.ItemId == line.ItemId);

                    Reason? reason = null;
                    if (line.AdjustmentReasonId.HasValue)
                    {
                        reason = await _context.Reasons.FindAsync(line.AdjustmentReasonId.Value);
                    }

                    lines.Add(new
                    {
                        lineId = line.LineId,
                        itemId = line.ItemId,
                        itemCode = item?.ItemCode ?? "UNKNOWN",
                        itemName = item?.ItemName ?? "Unknown Item",
                        uomCode = item?.UOM?.UOMCode ?? "EA",
                        quantity = line.Quantity,
                        direction = line.Direction,
                        directionText = line.Direction == 1 ? "INWARD" : line.Direction == -1 ? "OUTWARD" : "ADJUSTMENT",
                        adjustmentReasonId = line.AdjustmentReasonId,
                        reasonText = reason?.ReasonText,
                        unitPrice = line.UnitPrice,
                        totalAmount = line.TotalAmount,
                        remarks = line.Remarks
                    });
                }

                var result = new
                {
                    txnId = transaction.TxnId,
                    txnTypeId = transaction.TxnTypeId,
                    txnTypeCode = transaction.TxnType?.TxnTypeCode ?? "N/A",
                    txnTypeDescription = transaction.TxnType?.Description ?? "N/A",
                    txnDate = transaction.TxnDate,
                    referenceNo = transaction.ReferenceNo,
                    remarks = transaction.Remarks,
                    createdAt = transaction.CreatedAt,
                    createdBy = transaction.CreatedBy,
                    lines = lines,
                    lineCount = lines.Count
                };

                return Ok(result);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new
                {
                    message = "Error fetching transaction details",
                    error = ex.Message,
                    innerError = ex.InnerException?.Message
                });
            }
        }

        // POST: api/StockTransactions/Inward
        [HttpPost("Inward")]
        public async Task<ActionResult> CreateInwardTransaction(StockTransactionDto dto)
        {
            try
            {
                // ✅ VALIDATION: Check if transaction has items
                if (dto.Lines == null || dto.Lines.Count == 0)
                {
                    return BadRequest(new { message = "Transaction must have at least one item" });
                }

                // Get Inward transaction type
                var inwardType = await _context.TxnTypes
                    .FirstOrDefaultAsync(t => t.TxnTypeCode == "INWARD");

                if (inwardType == null)
                {
                    return BadRequest(new { message = "Inward transaction type not found in database" });
                }

                // Start database transaction
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

                    // Create lines and update stock
                    foreach (var lineDto in dto.Lines)
                    {
                        // Validate item exists and is active
                        var item = await _context.Items.FindAsync(lineDto.ItemId);
                        
                        if (item == null)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = $"Item with ID {lineDto.ItemId} not found" });
                        }

                        if (!item.Status)
                        {
                            await transaction.RollbackAsync();
                            return BadRequest(new { message = $"Item {item.ItemCode} is inactive and cannot be used" });
                        }

                        // Create transaction line
                        var line = new StockTransactionLine
                        {
                            TxnId = stockTxn.TxnId,
                            ItemId = lineDto.ItemId,
                            Quantity = lineDto.Quantity,
                            Direction = 1, // Inward = +1
                            UnitPrice = lineDto.UnitPrice,
                            TotalAmount = lineDto.Quantity * (lineDto.UnitPrice ?? 0),
                            Remarks = lineDto.Remarks,
                            CreatedAt = DateTime.Now,
                            CreatedBy = 1
                        };

                        _context.StockTransactionLines.Add(line);

                        // Update or create current stock
                        var currentStock = await _context.CurrentStock
                            .FirstOrDefaultAsync(cs => cs.ItemId == lineDto.ItemId);

                        if (currentStock == null)
                        {
                            // Create new stock record
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
                            // Update existing stock
                            currentStock.QtyOnHand += lineDto.Quantity;
                            currentStock.UpdatedAt = DateTime.Now;
                        }
                    }

                    // Save all changes
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new 
                    { 
                        txnId = stockTxn.TxnId, 
                        message = "Inward transaction created successfully" 
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, new 
                    { 
                        message = "Error saving transaction to database", 
                        error = ex.Message,
                        innerError = ex.InnerException?.Message
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new 
                { 
                    message = "Error creating inward transaction", 
                    error = ex.Message 
                });
            }
        }

        // POST: api/StockTransactions/Outward
        [HttpPost("Outward")]
        public async Task<ActionResult> CreateOutwardTransaction(StockTransactionDto dto)
        {
            try
            {
                // ✅ VALIDATION: Check if transaction has items
                if (dto.Lines == null || dto.Lines.Count == 0)
                {
                    return BadRequest(new { message = "Transaction must have at least one item" });
                }

                // Get Outward transaction type
                var outwardType = await _context.TxnTypes
                    .FirstOrDefaultAsync(t => t.TxnTypeCode == "OUTWARD");

                if (outwardType == null)
                {
                    return BadRequest(new { message = "Outward transaction type not found in database" });
                }

                // ✅ VALIDATE STOCK AVAILABILITY FOR ALL ITEMS FIRST
                foreach (var lineDto in dto.Lines)
                {
                    var item = await _context.Items.FindAsync(lineDto.ItemId);
                    
                    if (item == null)
                    {
                        return BadRequest(new { message = $"Item with ID {lineDto.ItemId} not found" });
                    }

                    if (!item.Status)
                    {
                        return BadRequest(new { message = $"Item {item.ItemCode} is inactive and cannot be used" });
                    }

                    // Check current stock
                    var currentStock = await _context.CurrentStock
                        .FirstOrDefaultAsync(cs => cs.ItemId == lineDto.ItemId);

                    var availableQty = currentStock?.QtyOnHand ?? 0;

                    if (availableQty < lineDto.Quantity)
                    {
                        return BadRequest(new
                        {
                            message = $"Insufficient stock for item {item.ItemCode} - {item.ItemName}. Available: {availableQty}, Requested: {lineDto.Quantity}"
                        });
                    }
                }

                // Start database transaction
                using var transaction = await _context.Database.BeginTransactionAsync();

                try
                {
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

                        // Create transaction line
                        var line = new StockTransactionLine
                        {
                            TxnId = stockTxn.TxnId,
                            ItemId = lineDto.ItemId,
                            Quantity = lineDto.Quantity,
                            Direction = -1, // Outward = -1
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

                        if (currentStock != null)
                        {
                            currentStock.QtyOnHand -= lineDto.Quantity;
                            currentStock.UpdatedAt = DateTime.Now;

                            // ✅ CREATE ALERT IF STOCK FALLS BELOW MINIMUM
                            if (currentStock.QtyOnHand < item!.MinStockLevel)
                            {
                                var existingAlert = await _context.StockAlerts
                                    .FirstOrDefaultAsync(sa => sa.ItemId == lineDto.ItemId && !sa.IsAcknowledged);

                                if (existingAlert == null)
                                {
                                    // Create new alert
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
                                else
                                {
                                    // Update existing alert
                                    existingAlert.QtyOnHand = currentStock.QtyOnHand;
                                    existingAlert.AlertDate = DateTime.Now;
                                }
                            }
                        }
                    }

                    // Save all changes
                    await _context.SaveChangesAsync();
                    await transaction.CommitAsync();

                    return Ok(new 
                    { 
                        txnId = stockTxn.TxnId, 
                        message = "Outward transaction created successfully" 
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, new 
                    { 
                        message = "Error saving transaction to database", 
                        error = ex.Message,
                        innerError = ex.InnerException?.Message
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new 
                { 
                    message = "Error creating outward transaction", 
                    error = ex.Message 
                });
            }
        }

        // POST: api/StockTransactions/Adjustment
        [HttpPost("Adjustment")]
        public async Task<ActionResult> CreateAdjustmentTransaction(StockTransactionDto dto)
        {
            try
            {
                // ✅ VALIDATION: Check if transaction has items
                if (dto.Lines == null || dto.Lines.Count == 0)
                {
                    return BadRequest(new { message = "Transaction must have at least one item" });
                }

                // Get Adjustment transaction type
                var adjustType = await _context.TxnTypes
                    .FirstOrDefaultAsync(t => t.TxnTypeCode == "ADJUST");

                if (adjustType == null)
                {
                    return BadRequest(new { message = "Adjustment transaction type not found in database" });
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

                        // Determine direction (1 for increase, -1 for decrease)
                        sbyte direction = 1;
                        
                        if (lineDto.Remarks?.ToLower().Contains("decrease") == true ||
                            lineDto.Remarks?.ToLower().Contains("reduce") == true ||
                            lineDto.Remarks?.ToLower().Contains("loss") == true)
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

                    return Ok(new 
                    { 
                        txnId = stockTxn.TxnId, 
                        message = "Adjustment transaction created successfully" 
                    });
                }
                catch (Exception ex)
                {
                    await transaction.RollbackAsync();
                    return StatusCode(500, new 
                    { 
                        message = "Error creating adjustment", 
                        error = ex.Message 
                    });
                }
            }
            catch (Exception ex)
            {
                return StatusCode(500, new 
                { 
                    message = "Error creating adjustment transaction", 
                    error = ex.Message 
                });
            }
        }
    }
}
