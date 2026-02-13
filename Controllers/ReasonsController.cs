using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;
using InventoryManagementAPI.Models;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ReasonsController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public ReasonsController(InventoryDbContext context)
        {
            _context = context;
        }

        // GET: api/Reasons
        [HttpGet]
        public async Task<ActionResult<IEnumerable<Reason>>> GetReasons()
        {
            return await _context.Reasons
                .Where(r => r.IsActive)
                .OrderBy(r => r.ReasonText)
                .ToListAsync();
        }

        // GET: api/Reasons/5
        [HttpGet("{id}")]
        public async Task<ActionResult<Reason>> GetReason(int id)
        {
            var reason = await _context.Reasons.FindAsync(id);

            if (reason == null)
            {
                return NotFound(new { message = "Reason not found" });
            }

            return reason;
        }

        // POST: api/Reasons
        [HttpPost]
        public async Task<ActionResult<Reason>> CreateReason(Reason reason)
        {
            // Check if reason text already exists
            var exists = await _context.Reasons
                .AnyAsync(r => r.ReasonText == reason.ReasonText);

            if (exists)
            {
                return BadRequest(new { message = "Reason already exists" });
            }

            reason.CreatedAt = DateTime.Now;
            _context.Reasons.Add(reason);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetReason), new { id = reason.ReasonId }, reason);
        }

        // PUT: api/Reasons/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateReason(int id, Reason reason)
        {
            if (id != reason.ReasonId)
            {
                return BadRequest(new { message = "Reason ID mismatch" });
            }

            // Check if reason text exists for another record
            var exists = await _context.Reasons
                .AnyAsync(r => r.ReasonText == reason.ReasonText && r.ReasonId != id);

            if (exists)
            {
                return BadRequest(new { message = "Reason already exists" });
            }

            reason.ModifiedAt = DateTime.Now;
            _context.Entry(reason).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ReasonExists(id))
                {
                    return NotFound(new { message = "Reason not found" });
                }
                throw;
            }

            return NoContent();
        }

        // DELETE: api/Reasons/5 (Soft delete)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteReason(int id)
        {
            var reason = await _context.Reasons.FindAsync(id);
            if (reason == null)
            {
                return NotFound(new { message = "Reason not found" });
            }

            // Check if reason is being used in any transactions
            var isUsed = await _context.StockTransactionLines
                .AnyAsync(stl => stl.AdjustmentReasonId == id);

            if (isUsed)
            {
                return BadRequest(new { message = "Cannot delete reason that is being used in transactions" });
            }

            reason.IsActive = false;
            reason.ModifiedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private async Task<bool> ReasonExists(int id)
        {
            return await _context.Reasons.AnyAsync(e => e.ReasonId == id);
        }
    }
}

/*Safety Check Added:

✅ Prevents deletion if reason is used in stock transactions
This protects data integrity!

Same Pattern as Before:

GET all → filtered by IsActive
GET by ID → FindAsync
POST → check duplicates, create
PUT → check duplicates, update
DELETE → soft delete with validation*/
