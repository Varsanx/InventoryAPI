using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using InventoryManagementAPI.Data;
using InventoryManagementAPI.Models;
using InventoryManagementAPI.DTOs;
using InventoryManagementAPI.Helpers;
using BCrypt.Net;

namespace InventoryManagementAPI.Controllers
{
    [Route("api/[controller]")]
    [ApiController]
    public class AuthController : ControllerBase
    {
        private readonly InventoryDbContext _context;
        private readonly JwtHelper _jwtHelper;

        public AuthController(InventoryDbContext context, IConfiguration configuration)
        {
            _context = context;
            _jwtHelper = new JwtHelper(configuration);
        }

        // POST: api/Auth/Login
        [HttpPost("Login")]
        public async Task<ActionResult<AuthResponseDto>> Login(LoginDto loginDto)
        {
            // Find user by username
            var user = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == loginDto.Username);

            if (user == null)
                return Unauthorized(new { message = "Invalid username or password" });

            // Verify password
            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(loginDto.Password, user.PasswordHash);

            if (!isPasswordValid)
                return Unauthorized(new { message = "Invalid username or password" });

            // Check if user is active
            if (!user.IsActive)
                return Unauthorized(new { message = "User account is inactive" });

            // Check approval status
            if (user.ApprovalStatus != "Approved")
                return Unauthorized(new { message = "Your account is pending approval. Please contact the administrator." });

            // Generate JWT token
            var token = _jwtHelper.GenerateToken(user.UserId, user.Username, user.Role);

            var response = new AuthResponseDto
            {
                UserId = user.UserId,
                Username = user.Username,
                FullName = user.FullName,
                Role = user.Role,
                Token = token
            };

            return Ok(response);
        }

        // POST: api/Auth/Register
        [HttpPost("Register")]
        public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto registerDto)
        {
            // Check if username already exists
            var existingUser = await _context.Users
                .FirstOrDefaultAsync(u => u.Username == registerDto.Username);

            if (existingUser != null)
                return BadRequest(new { message = "Username already exists" });

            // Hash password
            string passwordHash = BCrypt.Net.BCrypt.HashPassword(registerDto.Password);

            // Create new user (pending approval by default)
            var newUser = new User
            {
                Username = registerDto.Username,
                PasswordHash = passwordHash,
                FullName = registerDto.FullName,
                Email = registerDto.Email,
                Role = registerDto.Role ?? "User",
                IsActive = true,
                CreatedAt = DateTime.Now,
                ApprovalStatus = "Pending"
            };

            _context.Users.Add(newUser);
            await _context.SaveChangesAsync();

            return Ok(new { message = "Registration successful. Please wait for admin approval before logging in." });
        }

        // POST: api/Auth/ChangePassword
        [HttpPost("ChangePassword")]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto changePasswordDto)
        {
            var user = await _context.Users.FindAsync(changePasswordDto.UserId);

            if (user == null)
                return NotFound(new { message = "User not found" });

            // Verify current password
            bool isPasswordValid = BCrypt.Net.BCrypt.Verify(changePasswordDto.CurrentPassword, user.PasswordHash);

            if (!isPasswordValid)
                return BadRequest(new { message = "Current password is incorrect" });

            // Hash and save new password
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(changePasswordDto.NewPassword);
            user.ModifiedAt = DateTime.Now;

            await _context.SaveChangesAsync();

            return Ok(new { message = "Password changed successfully" });
        }
    }

    // DTO for Change Password
    public class ChangePasswordDto
    {
        public int UserId { get; set; }
        public string CurrentPassword { get; set; } = string.Empty;
        public string NewPassword { get; set; } = string.Empty;
    }
}
