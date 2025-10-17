using System;
using System.ComponentModel.DataAnnotations;

namespace MT.Data
{
    public class OtpAudit
    {
        public long Id { get; set; }

        [StringLength(450)]
        public string? UserId { get; set; }

        [StringLength(50)]
        public string? Phone { get; set; }

        [StringLength(100)]
        public string? Role { get; set; }

        // issued, resend, verified, failed
        [StringLength(30)]
        public string Event { get; set; } = "issued";

        public DateTime AtUtc { get; set; } = DateTime.UtcNow;

        [StringLength(50)]
        public string? Ip { get; set; }

        [StringLength(256)]
        public string? UserAgent { get; set; }

        public bool Success { get; set; }

        [StringLength(20)]
        public string? CodeMasked { get; set; } // e.g., ****** or last 2
    }
}
