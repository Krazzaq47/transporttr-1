using System.ComponentModel.DataAnnotations;

namespace MT.Models
{
    public class SettingsVm
    {
        [Display(Name = "Allowed Truck Number")]
        [Range(0, 10000, ErrorMessage = "The field {0} must be between {1} and {2}.")]
        public int AllowedTruckNumber { get; set; }

        [Display(Name = "Allowed Tanker Number")]
        [Range(0, 10000, ErrorMessage = "The field {0} must be between {1} and {2}.")]
        public int AllowedTankerNumber { get; set; }

        [Display(Name = "Auto-Cancellation Period (days)")]
        [Range(0, 365, ErrorMessage = "The field {0} must be between {1} and {2}.")]
        public int AutoCancellationDays { get; set; }

    }
}