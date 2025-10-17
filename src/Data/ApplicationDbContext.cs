using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace MT.Data
{
    public class ApplicationDbContext : IdentityDbContext
    {
        public ApplicationDbContext(DbContextOptions<ApplicationDbContext> options)
            : base(options)
        {
        }

        public DbSet<VehicleRegistration> VehicleRegistrations { get; set; }
        public DbSet<UserProfile> UserProfiles { get; set; }
        public DbSet<OtpAudit> OtpAudits { get; set; }
        public DbSet<Settings> Settings { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // Unique index on UserProfiles.Phone (allow multiple NULLs by filtering to NOT NULL)
            modelBuilder.Entity<UserProfile>()
                .HasIndex(p => p.Phone)
                .IsUnique()
                .HasFilter("[Phone] IS NOT NULL");

            // Unique index on UserProfiles.Email (filtered)
            modelBuilder.Entity<UserProfile>()
                .HasIndex(p => p.Email)
                .IsUnique()
                .HasFilter("[Email] IS NOT NULL");

            // Unique index on UserProfiles.QID (filtered) — ensures non-null QIDs are unique
            modelBuilder.Entity<UserProfile>()
                .HasIndex(p => p.QID)
                .IsUnique()
                .HasFilter("[QID] IS NOT NULL");

            // Unique index on AspNetUsers.PhoneNumber (filtered)
            modelBuilder.Entity<IdentityUser>()
                .HasIndex(u => u.PhoneNumber)
                .IsUnique()
                .HasFilter("[PhoneNumber] IS NOT NULL");
        }
    }
}
