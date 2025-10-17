using System;
using System.ComponentModel.DataAnnotations;

namespace MT.Data
{
    public class UserProfile
    {
        public long Id { get; set; }

        [Required, MaxLength(150)]
        public string Name { get; set; } = string.Empty;

        [Required, MaxLength(256)]
        public string Email { get; set; } = string.Empty;

        [MaxLength(11)]
        [RegularExpression(@"^974\d{8}$", ErrorMessage = "Phone must be exactly '974' followed by 8 digits.")]
        public string? Phone { get; set; }

        [MaxLength(50)]
        public string? QID { get; set; }

        // Link to Identity tables
        [Required, MaxLength(450)]
        public string UserId { get; set; } = string.Empty; // FK to AspNetUsers.Id

        [Required, MaxLength(450)]
        public string RoleId { get; set; } = string.Empty; // FK to AspNetRoles.Id

        [Required, MaxLength(256)]
        public string Username { get; set; } = string.Empty;

        [Required]
        public bool IsActive { get; set; } = true;

        // Auditing
        [MaxLength(450)]
        public string? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; }
        [MaxLength(450)]
        public string? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }
    }
}
