using System;
using System.ComponentModel.DataAnnotations;

namespace MT.Data
{
    public class VehicleRegistration
    {
        public long Id { get; set; }

        [Required, StringLength(20)]
        public string VehicleType { get; set; } = "";  // "Tank" or "Truck"

        [Required, StringLength(50)]
        public string OwnerPhone { get; set; } = "";

        [Required, StringLength(150)]
        public string VehicleOwnerName { get; set; } = "";

        [Required, StringLength(50)]
        public string DriverPhone { get; set; } = "";

        [Required, StringLength(150)]
        public string DriverName { get; set; } = "";

        // ===== Common (Tank) file paths =====
        public string? IdCardBothSidesPath { get; set; }
        public string? TankerFormBothSidesPath { get; set; }
        public string? IbanCertificatePath { get; set; }
        public string? TankCapacityCertPath { get; set; }
        public string? LandfillWorksPath { get; set; }
        public string? SignedRegistrationFormPath { get; set; }
        public string? ReleaseFormPath { get; set; }

        // ===== Truck-specific file paths =====
        public string? Truck_IdCardPath { get; set; }
        public string? Truck_TrailerRegistrationPath { get; set; }
        public string? Truck_TrafficCertificatePath { get; set; }
        public string? Truck_IbanCertificatePath { get; set; }
        public string? Truck_VehicleRegFormPath { get; set; }
        public string? Truck_ReleaseFormPath { get; set; }

        // ===== Metadata =====
        [StringLength(30)]
        public string Status { get; set; } = "Pending";

        public DateTime SubmittedDate { get; set; } = DateTime.Now;

        // Rejection audit (optional)
        [StringLength(1000)]
        public string? RejectReason { get; set; }

        [StringLength(450)]
        public string? RejectedByUserId { get; set; }

        [StringLength(256)]
        public string? RejectedByName { get; set; }

        [StringLength(100)]
        public string? RejectedByRole { get; set; }

        public DateTime? RejectedAt { get; set; }

        // Approval audit (optional)
        [StringLength(1000)]
        public string? ApproveComment { get; set; }

        public DateTime? ApprovedAt { get; set; }

        [StringLength(450)]
        public string? ApprovedByUserId { get; set; }

        [StringLength(256)]
        public string? ApprovedByName { get; set; }

        [StringLength(100)]
        public string? ApprovedByRole { get; set; }

        [StringLength(50)]
        public string? ClientIP { get; set; }   // Auto-filled from HttpContext
        [StringLength(20)]
        public string? UniqueToken { get; set; }

        // Used to restore original status after a SuperAdmin hides/unhides a record
        [StringLength(100)]
        public string? PreviousStatus { get; set; }
        [StringLength(20)]
        public string? VehicleNumber { get; set; }
    }
}
