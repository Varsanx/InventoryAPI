using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;
using InventoryManagementAPI.Models;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AlertsController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public AlertsController(InventoryDbContext context)
        {
            _context = context;
        }

        // GET: api/Alerts
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetAlerts(
            [FromQuery] bool? acknowledged = null,
            [FromQuery] DateTime? fromDate = null,
            [FromQuery] DateTime? toDate = null)
        {
            var query = _context.StockAlerts
                .Include(sa => sa.Item)
                    .ThenInclude(i => i!.Category)
                .Include(sa => sa.Item)
                    .ThenInclude(i => i!.UOM)
                .Include(sa => sa.AcknowledgedByUser)
                .AsQueryable();

            // Filter by acknowledged status
            if (acknowledged.HasValue)
            {
                query = query.Where(sa => sa.IsAcknowledged == acknowledged.Value);
            }

            // Filter by date range
            if (fromDate.HasValue)
            {
                query = query.Where(sa => sa.AlertDate >= fromDate.Value);
            }

            if (toDate.HasValue)
            {
                query = query.Where(sa => sa.AlertDate <= toDate.Value);
            }

            var alerts = await query
                .OrderByDescending(sa => sa.AlertDate)
                .Select(sa => new
                {
                    sa.AlertId,
                    sa.ItemId,
                    ItemCode = sa.Item!.ItemCode,
                    ItemName = sa.Item!.ItemName,
                    CategoryName = sa.Item!.Category!.CategoryName,
                    UOMCode = sa.Item!.UOM!.UOMCode,
                    sa.QtyOnHand,
                    sa.MinStockLevel,
                    Shortage = sa.MinStockLevel - sa.QtyOnHand,
                    sa.AlertDate,
                    sa.IsAcknowledged,
                    sa.AcknowledgedBy,
                    AcknowledgedByName = sa.AcknowledgedByUser != null ? sa.AcknowledgedByUser.FullName : null,
                    sa.AcknowledgedAt,
                    AlertAge = (DateTime.Now - sa.AlertDate).Days
                })
                .ToListAsync();

            return Ok(alerts);
        }

        // GET: api/Alerts/5
        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetAlert(int id)
        {
            var alert = await _context.StockAlerts
                .Include(sa => sa.Item)
                    .ThenInclude(i => i!.Category)
                .Include(sa => sa.Item)
                    .ThenInclude(i => i!.UOM)
                .Include(sa => sa.AcknowledgedByUser)
                .Where(sa => sa.AlertId == id)
                .Select(sa => new
                {
                    sa.AlertId,
                    sa.ItemId,
                    ItemCode = sa.Item!.ItemCode,
                    ItemName = sa.Item!.ItemName,
                    CategoryName = sa.Item!.Category!.CategoryName,
                    UOMCode = sa.Item!.UOM!.UOMCode,
                    sa.QtyOnHand,
                    sa.MinStockLevel,
                    Shortage = sa.MinStockLevel - sa.QtyOnHand,
                    sa.AlertDate,
                    sa.IsAcknowledged,
                    sa.AcknowledgedBy,
                    AcknowledgedByName = sa.AcknowledgedByUser != null ? sa.AcknowledgedByUser.FullName : null,
                    sa.AcknowledgedAt
                })
                .FirstOrDefaultAsync();

            if (alert == null)
            {
                return NotFound(new { message = "Alert not found" });
            }

            return Ok(alert);
        }

        // POST: api/Alerts/Acknowledge/5
        [HttpPost("Acknowledge/{id}")]
        public async Task<IActionResult> AcknowledgeAlert(int id, [FromBody] int acknowledgedBy)
        {
            var alert = await _context.StockAlerts.FindAsync(id);

            if (alert == null)
            {
                return NotFound(new { message = "Alert not found" });
            }

            if (alert.IsAcknowledged)
            {
                return BadRequest(new { message = "Alert already acknowledged" });
            }

            // Verify user exists
            var userExists = await _context.Users.AnyAsync(u => u.UserId == acknowledgedBy);
            if (!userExists)
            {
                return BadRequest(new { message = "Invalid user" });
            }

            alert.IsAcknowledged = true;
            alert.AcknowledgedBy = acknowledgedBy;
            alert.AcknowledgedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Alert acknowledged successfully" });
        }

        // POST: api/Alerts/AcknowledgeMultiple
        [HttpPost("AcknowledgeMultiple")]
        public async Task<IActionResult> AcknowledgeMultipleAlerts([FromBody] AcknowledgeMultipleDto dto)
        {
            if (dto.AlertIds == null || !dto.AlertIds.Any())
            {
                return BadRequest(new { message = "No alert IDs provided" });
            }

            // Verify user exists
            var userExists = await _context.Users.AnyAsync(u => u.UserId == dto.AcknowledgedBy);
            if (!userExists)
            {
                return BadRequest(new { message = "Invalid user" });
            }

            var alerts = await _context.StockAlerts
                .Where(sa => dto.AlertIds.Contains(sa.AlertId) && !sa.IsAcknowledged)
                .ToListAsync();

            if (!alerts.Any())
            {
                return NotFound(new { message = "No unacknowledged alerts found" });
            }

            foreach (var alert in alerts)
            {
                alert.IsAcknowledged = true;
                alert.AcknowledgedBy = dto.AcknowledgedBy;
                alert.AcknowledgedAt = DateTime.Now;
            }

            await _context.SaveChangesAsync();

            return Ok(new { message = $"{alerts.Count} alerts acknowledged successfully", count = alerts.Count });
        }

        // POST: api/Alerts/Generate
        [HttpPost("Generate")]
        public async Task<ActionResult<object>> GenerateAlerts()
        {
            var lowStockItems = await _context.CurrentStock
                .Include(cs => cs.Item)
                .Where(cs => cs.Item!.Status && cs.QtyOnHand < cs.Item.MinStockLevel)
                .ToListAsync();

            int newAlertsCount = 0;

            foreach (var stock in lowStockItems)
            {
                // Check if alert already exists for this item
                var existingAlert = await _context.StockAlerts
                    .Where(sa => sa.ItemId == stock.ItemId && !sa.IsAcknowledged)
                    .FirstOrDefaultAsync();

                if (existingAlert == null)
                {
                    var newAlert = new StockAlert
                    {
                        ItemId = stock.ItemId,
                        QtyOnHand = stock.QtyOnHand,
                        MinStockLevel = stock.Item!.MinStockLevel,
                        AlertDate = DateTime.Now,
                        IsAcknowledged = false
                    };

                    _context.StockAlerts.Add(newAlert);
                    newAlertsCount++;
                }
                else
                {
                    // Update existing alert with current quantities
                    existingAlert.QtyOnHand = stock.QtyOnHand;
                    existingAlert.MinStockLevel = stock.Item!.MinStockLevel;
                    existingAlert.AlertDate = DateTime.Now;
                }
            }

            await _context.SaveChangesAsync();

            return Ok(new
            {
                message = "Alerts generated successfully",
                newAlerts = newAlertsCount,
                totalLowStockItems = lowStockItems.Count
            });
        }

        // DELETE: api/Alerts/5
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteAlert(int id)
        {
            var alert = await _context.StockAlerts.FindAsync(id);
            if (alert == null)
            {
                return NotFound(new { message = "Alert not found" });
            }

            _context.StockAlerts.Remove(alert);
            await _context.SaveChangesAsync();

            return NoContent();
        }

        // GET: api/Alerts/Summary
        [HttpGet("Summary")]
        public async Task<ActionResult<object>> GetAlertsSummary()
        {
            var totalAlerts = await _context.StockAlerts.CountAsync();
            var unacknowledged = await _context.StockAlerts.CountAsync(sa => !sa.IsAcknowledged);
            var acknowledged = await _context.StockAlerts.CountAsync(sa => sa.IsAcknowledged);
            
            var oldestUnacknowledged = await _context.StockAlerts
                .Where(sa => !sa.IsAcknowledged)
                .OrderBy(sa => sa.AlertDate)
                .Select(sa => sa.AlertDate)
                .FirstOrDefaultAsync();

            var criticalAlerts = await _context.StockAlerts
                .Where(sa => !sa.IsAcknowledged && sa.QtyOnHand == 0)
                .CountAsync();

            return Ok(new
            {
                totalAlerts,
                unacknowledged,
                acknowledged,
                criticalAlerts,
                oldestUnacknowledgedDate = oldestUnacknowledged,
                oldestAlertAge = oldestUnacknowledged != default ? (DateTime.Now - oldestUnacknowledged).Days : 0
            });
        }
    }

    // DTO for acknowledging multiple alerts
    public class AcknowledgeMultipleDto
    {
        public List<int> AlertIds { get; set; } = new();
        public int AcknowledgedBy { get; set; }
    }
}

