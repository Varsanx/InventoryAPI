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

    }

}
