using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;
using InventoryManagementAPI.Models;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class UserManagementController : ControllerBase
    {
        private readonly InventoryDbContext _context;

        public UserManagementController(InventoryDbContext context)
        {
            _context = context;
        }

        // GET: api/UserManagement/AllUsers
        [HttpGet("AllUsers")]
        public async Task<ActionResult<IEnumerable<object>>> GetAllUsers()
        {
            try
            {
                Console.WriteLine("[USER MGMT] Fetching all users...");

                var users = await _context.Users
                    .OrderByDescending(u => u.CreatedAt)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Username,
                        u.FullName,
                        u.Email,
                        u.Role,
                        u.IsActive,
                        u.ApprovalStatus,
                        u.ApprovedBy,
                        u.ApprovedAt,
                        u.RejectionReason,
                        u.CreatedAt
                    })
                    .ToListAsync();

                Console.WriteLine($"[USER MGMT] Found {users.Count} users");

                return Ok(users);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[USER MGMT] Error: {ex.Message}");
                return StatusCode(500, new { message = "Error fetching users", error = ex.Message });
            }
        }

        // GET: api/UserManagement/PendingUsers
        [HttpGet("PendingUsers")]
        public async Task<ActionResult<IEnumerable<object>>> GetPendingUsers()
        {
            try
            {
                var users = await _context.Users
                    .Where(u => u.ApprovalStatus == "Pending")
                    .OrderBy(u => u.CreatedAt)
                    .Select(u => new
                    {
                        u.UserId,
                        u.Username,
                        u.FullName,
                        u.Email,
                        u.Role,
                        u.ApprovalStatus,
                        u.CreatedAt,
                        WaitingDays = (DateTime.Now - u.CreatedAt).Days
                    })
                    .ToListAsync();

                return Ok(users);
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error fetching pending users", error = ex.Message });
            }
        }

        // POST: api/UserManagement/CreateUser (ADMIN ONLY)
        [HttpPost("CreateUser")]
        public async Task<ActionResult> CreateUser([FromBody] CreateUserRequest request)
        {
            try
            {
                Console.WriteLine($"[USER MGMT] Creating user: {request.Username}");

                // Verify requester is Admin
                var admin = await _context.Users.FindAsync(request.CreatedBy);
                if (admin == null || admin.Role != "Admin")
                {
                    return Unauthorized(new { message = "Only Admin can create users" });
                }

                // Check if username exists
                var usernameExists = await _context.Users
                    .AnyAsync(u => u.Username == request.Username);

                if (usernameExists)
                {
                    return BadRequest(new { message = "Username already exists" });
                }

                // Hash password
                var passwordHash = BCrypt.Net.BCrypt.HashPassword(request.Password);

                var user = new User
                {
                    Username = request.Username,
                    PasswordHash = passwordHash,
                    FullName = request.FullName,
                    Email = request.Email,
                    Role = request.Role,
                    IsActive = true,
                    ApprovalStatus = "Approved", // Admin-created users are auto-approved
                    CreatedAt = DateTime.Now,
                    CreatedBy = request.CreatedBy
                };

                _context.Users.Add(user);
                await _context.SaveChangesAsync();

                Console.WriteLine($"[USER MGMT] User created: {user.Username} (ID: {user.UserId})");

                return Ok(new
                {
                    message = $"User {user.Username} created successfully",
                    userId = user.UserId,
                    username = user.Username,
                    role = user.Role
                });
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[USER MGMT] Error creating user: {ex.Message}");
                return StatusCode(500, new { message = "Error creating user", error = ex.Message });
            }
        }

        // POST: api/UserManagement/UpdateUser
        [HttpPost("UpdateUser")]
        public async Task<ActionResult> UpdateUser([FromBody] UpdateUserRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(request.UserId);
                
                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                // Update fields
                user.FullName = request.FullName;
                user.Email = request.Email;
                user.Role = request.Role;
                user.IsActive = request.IsActive;
                user.ModifiedAt = DateTime.Now;
                user.ModifiedBy = request.ModifiedBy;

                await _context.SaveChangesAsync();

                return Ok(new { message = "User updated successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error updating user", error = ex.Message });
            }
        }

        // POST: api/UserManagement/ResetPassword (ADMIN ONLY)
        [HttpPost("ResetPassword")]
        public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(request.UserId);
                
                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                // Hash new password
                user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(request.NewPassword);
                user.ModifiedAt = DateTime.Now;
                user.ModifiedBy = request.ResetBy;

                await _context.SaveChangesAsync();

                Console.WriteLine($"[USER MGMT] Password reset for user: {user.Username}");

                return Ok(new { message = $"Password reset successfully for {user.Username}" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error resetting password", error = ex.Message });
            }
        }

        // POST: api/UserManagement/ApproveUser
        [HttpPost("ApproveUser")]
        public async Task<ActionResult> ApproveUser([FromBody] ApprovalRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(request.UserId);
                
                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                user.ApprovalStatus = "Approved";
                user.ApprovedBy = request.ApprovedBy;
                user.ApprovedAt = DateTime.Now;
                user.RejectionReason = null;

                await _context.SaveChangesAsync();

                return Ok(new { message = $"User {user.Username} has been approved successfully" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error approving user", error = ex.Message });
            }
        }

        // POST: api/UserManagement/RejectUser
        [HttpPost("RejectUser")]
        public async Task<ActionResult> RejectUser([FromBody] RejectionRequest request)
        {
            try
            {
                var user = await _context.Users.FindAsync(request.UserId);
                
                if (user == null)
                {
                    return NotFound(new { message = "User not found" });
                }

                user.ApprovalStatus = "Rejected";
                user.ApprovedBy = request.RejectedBy;
                user.ApprovedAt = DateTime.Now;
                user.RejectionReason = request.Reason;

                await _context.SaveChangesAsync();

                return Ok(new { message = $"User {user.Username} has been rejected" });
            }
            catch (Exception ex)
            {
                return StatusCode(500, new { message = "Error rejecting user", error = ex.Message });
            }
        }
    }

    // ✅ REQUEST MODELS
    public class CreateUserRequest
    {
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string Role { get; set; } = "Storekeeper";
        public int CreatedBy { get; set; }
    }

    public class UpdateUserRequest
    {
        public int UserId { get; set; }
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string Role { get; set; } = string.Empty;
        public bool IsActive { get; set; }
        public int ModifiedBy { get; set; }
    }

    public class ResetPasswordRequest
    {
        public int UserId { get; set; }
        public string NewPassword { get; set; } = string.Empty;
        public int ResetBy { get; set; }
    }

    public class ApprovalRequest
    {
        public int UserId { get; set; }
        public int ApprovedBy { get; set; }
    }

    public class RejectionRequest
    {
        public int UserId { get; set; }
        public int RejectedBy { get; set; }
        public string Reason { get; set; } = string.Empty;
    }
}