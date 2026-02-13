using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;
using InventoryManagementAPI.Models;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class ItemsController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public ItemsController(InventoryDbContext context)
        {
            _context = context;
        }

        // GET: api/Items
        [HttpGet]
        public async Task<ActionResult<IEnumerable<object>>> GetItems(
            [FromQuery] string? search = null,
            [FromQuery] int? categoryId = null,
            [FromQuery] bool? activeOnly = true)
        {
            var query = _context.Items
                .Include(i => i.Category)
                .Include(i => i.UOM)
                .AsQueryable();

            // Filter by active status
            if (activeOnly == true)
            {
                query = query.Where(i => i.Status);
            }

            // Filter by category
            if (categoryId.HasValue)
            {
                query = query.Where(i => i.CategoryId == categoryId.Value);
            }

            // Search by code or name
            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(i =>
                    i.ItemCode.Contains(search) ||
                    i.ItemName.Contains(search));
            }

            var items = await query
                .OrderBy(i => i.ItemCode)
                .Select(i => new
                {
                    i.ItemId,
                    i.ItemCode,
                    i.ItemName,
                    i.CategoryId,
                    CategoryName = i.Category!.CategoryName,
                    i.UOMId,
                    UOMCode = i.UOM!.UOMCode,
                    UOMDescription = i.UOM!.UOMDescription,
                    i.MinStockLevel,
                    i.Status,
                    i.CreatedAt,
                    i.ModifiedAt
                })
                .ToListAsync();

            return Ok(items);
        }

        // GET: api/Items/5
        [HttpGet("{id}")]
        public async Task<ActionResult<object>> GetItem(int id)
        {
            var item = await _context.Items
                .Include(i => i.Category)
                .Include(i => i.UOM)
                .Where(i => i.ItemId == id)
                .Select(i => new
                {
                    i.ItemId,
                    i.ItemCode,
                    i.ItemName,
                    i.CategoryId,
                    CategoryName = i.Category!.CategoryName,
                    i.UOMId,
                    UOMCode = i.UOM!.UOMCode,
                    UOMDescription = i.UOM!.UOMDescription,
                    i.MinStockLevel,
                    i.Status,
                    i.CreatedAt,
                    i.CreatedBy,
                    i.ModifiedAt,
                    i.ModifiedBy
                })
                .FirstOrDefaultAsync();

            if (item == null)
            {
                return NotFound(new { message = "Item not found" });
            }

            return Ok(item);
        }

        // GET: api/Items/5/stock
        [HttpGet("{id}/stock")]
        public async Task<ActionResult<object>> GetItemStock(int id)
        {
            var item = await _context.Items
                .Include(i => i.UOM)
                .FirstOrDefaultAsync(i => i.ItemId == id);

            if (item == null)
            {
                return NotFound(new { message = "Item not found" });
            }

            var currentStock = await _context.CurrentStock
                .FirstOrDefaultAsync(cs => cs.ItemId == id);

            var result = new
            {
                item.ItemId,
                item.ItemCode,
                item.ItemName,
                UOMCode = item.UOM!.UOMCode,
                QtyOnHand = currentStock?.QtyOnHand ?? 0,
                item.MinStockLevel,
                StockStatus = (currentStock?.QtyOnHand ?? 0) == 0 ? "OUT OF STOCK" :
                             (currentStock?.QtyOnHand ?? 0) < item.MinStockLevel ? "LOW STOCK" :
                             "IN STOCK"
            };

            return Ok(result);
        }

        // POST: api/Items
        [HttpPost]
        public async Task<ActionResult<Item>> CreateItem(Item item)
        {
            // Validate Item Code is unique
            var codeExists = await _context.Items
                .AnyAsync(i => i.ItemCode == item.ItemCode);

            if (codeExists)
            {
                return BadRequest(new { message = "Item code already exists" });
            }

            // Validate Category exists
            var categoryExists = await _context.Categories
                .AnyAsync(c => c.CategoryId == item.CategoryId && c.IsActive);

            if (!categoryExists)
            {
                return BadRequest(new { message = "Invalid category" });
            }

            // Validate UOM exists
            var uomExists = await _context.UOM
                .AnyAsync(u => u.UOMId == item.UOMId && u.IsActive);

            if (!uomExists)
            {
                return BadRequest(new { message = "Invalid UOM" });
            }

            // Validate MinStockLevel
            if (item.MinStockLevel < 0)
            {
                return BadRequest(new { message = "Minimum stock level cannot be negative" });
            }

            item.CreatedAt = DateTime.Now;
            _context.Items.Add(item);
            await _context.SaveChangesAsync();

            // Initialize CurrentStock for new item
            var currentStock = new CurrentStock
            {
                ItemId = item.ItemId,
                QtyOnHand = 0,
                UpdatedAt = DateTime.Now
            };
            _context.CurrentStock.Add(currentStock);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetItem), new { id = item.ItemId }, item);
        }

        // PUT: api/Items/5
        [HttpPut("{id}")]
        public async Task<IActionResult> UpdateItem(int id, Item item)
        {
            if (id != item.ItemId)
            {
                return BadRequest(new { message = "Item ID mismatch" });
            }

            // Check if item code exists for another record
            var codeExists = await _context.Items
                .AnyAsync(i => i.ItemCode == item.ItemCode && i.ItemId != id);

            if (codeExists)
            {
                return BadRequest(new { message = "Item code already exists" });
            }

            // Validate Category
            var categoryExists = await _context.Categories
                .AnyAsync(c => c.CategoryId == item.CategoryId && c.IsActive);

            if (!categoryExists)
            {
                return BadRequest(new { message = "Invalid category" });
            }

            // Validate UOM
            var uomExists = await _context.UOM
                .AnyAsync(u => u.UOMId == item.UOMId && u.IsActive);

            if (!uomExists)
            {
                return BadRequest(new { message = "Invalid UOM" });
            }

            item.ModifiedAt = DateTime.Now;
            _context.Entry(item).State = EntityState.Modified;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!await ItemExists(id))
                {
                    return NotFound(new { message = "Item not found" });
                }
                throw;
            }

            return NoContent();
        }

        // DELETE: api/Items/5 (Soft delete with stock check)
        [HttpDelete("{id}")]
        public async Task<IActionResult> DeleteItem(int id)
        {
            var item = await _context.Items.FindAsync(id);
            if (item == null)
            {
                return NotFound(new { message = "Item not found" });
            }

            // Check if item has stock
            var currentStock = await _context.CurrentStock
                .FirstOrDefaultAsync(cs => cs.ItemId == id);

            if (currentStock != null && currentStock.QtyOnHand > 0)
            {
                return BadRequest(new { message = $"Cannot delete item with stock on hand ({currentStock.QtyOnHand} units)" });
            }

            // Check if item is used in any transactions
            var hasTransactions = await _context.StockTransactionLines
                .AnyAsync(stl => stl.ItemId == id);

            if (hasTransactions)
            {
                return BadRequest(new { message = "Cannot delete item that has transaction history. Consider marking as inactive instead." });
            }

            item.Status = false;
            item.ModifiedAt = DateTime.Now;
            await _context.SaveChangesAsync();

            return NoContent();
        }

        private async Task<bool> ItemExists(int id)
        {
            return await _context.Items.AnyAsync(e => e.ItemId == id);
        }
    }
}
