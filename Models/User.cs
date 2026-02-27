namespace InventoryManagementAPI.Models
{
    public class User
    {
        public int UserId { get; set; }
        public string Username { get; set; } = string.Empty;
        public string PasswordHash { get; set; } = string.Empty;
        public string FullName { get; set; } = string.Empty;
        public string? Email { get; set; }
        public string Role { get; set; } = "User";
        public bool IsActive { get; set; } = true;
        public string ApprovalStatus { get; set; } = "Pending"; // Pending, Approved, Rejected
        public int? ApprovedBy { get; set; }
        public DateTime? ApprovedAt { get; set; }
        public string? RejectionReason { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int? CreatedBy { get; set; }
        public DateTime? ModifiedAt { get; set; }
        public int? ModifiedBy { get; set; }
        

    }
}