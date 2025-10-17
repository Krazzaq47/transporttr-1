using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MT.Data
{
    [Table("Settings")]
    public class Settings
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [Display(Name = "Allowed Truck Number")]
        [Range(0, 10000, ErrorMessage = "The field {0} must be between {1} and {2}.")]
        [Required]
        public int AllowedTruckNumber { get; set; }

        [Display(Name = "Allowed Water Tanker Number")]
        [Range(0, 10000, ErrorMessage = "The field {0} must be between {1} and {2}.")]
        [Required]
        public int AllowedTankerNumber { get; set; }

        [Display(Name = "Auto-Cancellation Period (days)")]
        [Range(0, 365, ErrorMessage = "The field {0} must be between {1} and {2}.")]
        [Required]
        public int AutoCancellationDays { get; set; }

        [Display(Name = "Created At")]
        [Required]
        public DateTime CreatedAt { get; set; } = DateTime.Now;

        [Display(Name = "Updated At")]
        public DateTime? UpdatedAt { get; set; }
    }
}
