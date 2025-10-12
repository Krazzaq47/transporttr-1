using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Identity;

#nullable disable

namespace MT.Data.Migrations
{
    /// <inheritdoc />
    public partial class SetSeedPasswords : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Compute Identity v3 hashes for Pass@1234 and update users.
            var hasher = new PasswordHasher<IdentityUser>();
            const string pwd = "Pass@1234";

            var adminHash = hasher.HashPassword(new IdentityUser { UserName = "admin@mt.local", Email = "admin@mt.local" }, pwd);
            var superHash = hasher.HashPassword(new IdentityUser { UserName = "info@lumen-path.com", Email = "info@lumen-path.com" }, pwd);
            var docHash = hasher.HashPassword(new IdentityUser { UserName = "docverifier@mt.local", Email = "docverifier@mt.local" }, pwd);
            var finHash = hasher.HashPassword(new IdentityUser { UserName = "finalapprover@mt.local", Email = "finalapprover@mt.local" }, pwd);
            var minHash = hasher.HashPassword(new IdentityUser { UserName = "ministry@mt.local", Email = "ministry@mt.local" }, pwd);
            var ownHash = hasher.HashPassword(new IdentityUser { UserName = "owner@mt.local", Email = "owner@mt.local" }, pwd);

            migrationBuilder.Sql($@"
                UPDATE AspNetUsers SET PasswordHash = '{adminHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'ADMIN@MT.LOCAL';
                UPDATE AspNetUsers SET PasswordHash = '{superHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'INFO@LUMEN-PATH.COM';
                UPDATE AspNetUsers SET PasswordHash = '{docHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'DOCVERIFIER@MT.LOCAL';
                UPDATE AspNetUsers SET PasswordHash = '{finHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'FINALAPPROVER@MT.LOCAL';
                UPDATE AspNetUsers SET PasswordHash = '{minHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'MINISTRY@MT.LOCAL';
                UPDATE AspNetUsers SET PasswordHash = '{ownHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'OWNER@MT.LOCAL';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Revert only non-admin seeded users to NULL hashes to force reset if rolled back
            migrationBuilder.Sql(@"
                UPDATE AspNetUsers SET PasswordHash = NULL WHERE NormalizedEmail IN (
                    'DOCVERIFIER@MT.LOCAL','FINALAPPROVER@MT.LOCAL','MINISTRY@MT.LOCAL','OWNER@MT.LOCAL'
                );
            ");
        }
    }
}
