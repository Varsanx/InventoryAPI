using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;
using InventoryManagementAPI.Models;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UOMController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public UOMController(InventoryDbContext context)
        {
            _context = context;
        }

        // GET: api/UOM
        [HttpGet]
        public async Task<ActionResult<IEnumerable<UOM>>> GetUOMs()
        {
            return await _context.UOM
                .Where(u => u.IsActive)
                .OrderBy(u => u.UOMCode)
                .ToListAsync();
        }

        // GET: api/UOM/5
        [HttpGet("{id}")]
        public async Task<ActionResult<UOM>> GetUOM(int id)
        {
            var uom = await _context.UOM.FindAsync(id);

            if (uom == null)
            {
                return NotFound(new { message = "UOM not found" });
            }

            return uom;
        }

        // POST: api/UOM
        [HttpPost]
        public async Task<ActionResult<UOM>> CreateUOM(UOM uom)
        {
            // Check if UOM code already exists
            var codeExists = await _context.UOM
                .AnyAsync(u => u.UOMCode == uom.UOMCode);

            if (codeExists)
            {
                return BadRequest(new { message = "UOM code already exists" });
            }

            // Check if description already exists
            var descExists = await _context.UOM
                .AnyAsync(u => u.UOMDescription == uom.UOMDescription);

            if (descExists)
            {
                return BadRequest(new { message = "UOM description already exists" });
            }

            uom.CreatedAt = DateTime.Now;
            _context.UOM.Add(uom);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetUOM), new { id = uom.UOMId }, uom);
        }

        // PUT: api/UOM/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateUOM(int id, UOM uom)
        {
            if (id != uom.UOMId)
            {
                return BadRequest(new { message = "UOM ID mismatch" });
            }

            // Check if UOM code exists for another record
            var codeExists = await _context.UOM
                .AnyAsync(u => u.UOMCode == uom.UOMCode && u.UOMId != id);

            if (codeExists)
            {
                return BadRequest(new { message = "UOM code already exists" });
            }

            uom.ModifiedAt = DateTime.Now;
            _context.Entry(uom).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await UOMExists(id))
                {
                    return NotFound(new { message = "UOM not found" });
                }
                throw;
            }

            return NoContent();
        }

        // DELETE: api/UOM/5 (Soft delete)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteUOM(int id)
        {
            var uom = await _context.UOM.FindAsync(id);
            if (uom == null)
            {
                return NotFound(new { message = "UOM not found" });
            }

            // TODO: Check if UOM is being used by any items
            var isUsed = await _context.Items.AnyAsync(i => i.UOMId == id);
            if (isUsed)
            {
                return BadRequest(new { message = "Cannot delete UOM that is being used by items" });
            }

            uom.IsActive = false;
            uom.ModifiedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private async Task<bool> UOMExists(int id)
        {
            return await _context.UOM.AnyAsync(e => e.UOMId == id);
        }
    }
}


/*✅ Checks both UOMCode AND UOMDescription for duplicates
✅ Prevents deletion if UOM is used by items
✅ Orders by UOMCode instead of name*/
