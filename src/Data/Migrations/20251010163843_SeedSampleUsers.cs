using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.AspNetCore.Identity;

#nullable disable

namespace MT.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedSampleUsers : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seed sample users for each role (without password hash). Use password reset to set passwords.
            migrationBuilder.Sql(@"
                -- Insert users if they don't exist
                IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE NormalizedEmail = 'DOCVERIFIER@MT.LOCAL')
                BEGIN
                  INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
                    PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled,
                    LockoutEnabled, AccessFailedCount)
                  VALUES (NEWID(), 'docverifier@mt.local', 'DOCVERIFIER@MT.LOCAL', 'docverifier@mt.local', 'DOCVERIFIER@MT.LOCAL', 1,
                    NULL, NEWID(), NEWID(), 0, 0, 1, 0);
                END

                IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE NormalizedEmail = 'FINALAPPROVER@MT.LOCAL')
                BEGIN
                  INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
                    PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled,
                    LockoutEnabled, AccessFailedCount)
                  VALUES (NEWID(), 'finalapprover@mt.local', 'FINALAPPROVER@MT.LOCAL', 'finalapprover@mt.local', 'FINALAPPROVER@MT.LOCAL', 1,
                    NULL, NEWID(), NEWID(), 0, 0, 1, 0);
                END

                IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE NormalizedEmail = 'MINISTRY@MT.LOCAL')
                BEGIN
                  INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
                    PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled,
                    LockoutEnabled, AccessFailedCount)
                  VALUES (NEWID(), 'ministry@mt.local', 'MINISTRY@MT.LOCAL', 'ministry@mt.local', 'MINISTRY@MT.LOCAL', 1,
                    NULL, NEWID(), NEWID(), 0, 0, 1, 0);
                END

                IF NOT EXISTS (SELECT 1 FROM AspNetUsers WHERE NormalizedEmail = 'OWNER@MT.LOCAL')
                BEGIN
                  INSERT INTO AspNetUsers (Id, UserName, NormalizedUserName, Email, NormalizedEmail, EmailConfirmed,
                    PasswordHash, SecurityStamp, ConcurrencyStamp, PhoneNumberConfirmed, TwoFactorEnabled,
                    LockoutEnabled, AccessFailedCount)
                  VALUES (NEWID(), 'owner@mt.local', 'OWNER@MT.LOCAL', 'owner@mt.local', 'OWNER@MT.LOCAL', 1,
                    NULL, NEWID(), NEWID(), 0, 0, 1, 0);
                END

                -- Assign roles idempotently
                DECLARE @DocUserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = 'DOCVERIFIER@MT.LOCAL');
                DECLARE @FinUserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = 'FINALAPPROVER@MT.LOCAL');
                DECLARE @MinUserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = 'MINISTRY@MT.LOCAL');
                DECLARE @OwnUserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = 'OWNER@MT.LOCAL');

                DECLARE @DocRoleId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetRoles WHERE NormalizedName = 'DOCUMENTVERIFIER');
                DECLARE @FinRoleId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetRoles WHERE NormalizedName = 'FINALAPPROVER');
                DECLARE @MinRoleId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetRoles WHERE NormalizedName = 'MINISTRYOFFICER');
                DECLARE @OwnRoleId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetRoles WHERE NormalizedName = 'OWNER');

                IF @DocUserId IS NOT NULL AND @DocRoleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE UserId=@DocUserId AND RoleId=@DocRoleId)
                    INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES (@DocUserId, @DocRoleId);
                IF @FinUserId IS NOT NULL AND @FinRoleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE UserId=@FinUserId AND RoleId=@FinRoleId)
                    INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES (@FinUserId, @FinRoleId);
                IF @MinUserId IS NOT NULL AND @MinRoleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE UserId=@MinUserId AND RoleId=@MinRoleId)
                    INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES (@MinUserId, @MinRoleId);
                IF @OwnUserId IS NOT NULL AND @OwnRoleId IS NOT NULL AND NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE UserId=@OwnUserId AND RoleId=@OwnRoleId)
                    INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES (@OwnUserId, @OwnRoleId);
            ");

            // Set PasswordHash for all seeded users (and known accounts) to 'Pass@1234'
            var hasher = new PasswordHasher<IdentityUser>();
            string pwd = "Pass@1234";

            var docHash = hasher.HashPassword(new IdentityUser { UserName = "docverifier@mt.local", Email = "docverifier@mt.local" }, pwd);
            var finHash = hasher.HashPassword(new IdentityUser { UserName = "finalapprover@mt.local", Email = "finalapprover@mt.local" }, pwd);
            var minHash = hasher.HashPassword(new IdentityUser { UserName = "ministry@mt.local", Email = "ministry@mt.local" }, pwd);
            var ownHash = hasher.HashPassword(new IdentityUser { UserName = "owner@mt.local", Email = "owner@mt.local" }, pwd);
            var superHash = hasher.HashPassword(new IdentityUser { UserName = "info@lumen-path.com", Email = "info@lumen-path.com" }, pwd);
            var adminHash = hasher.HashPassword(new IdentityUser { UserName = "admin@mt.local", Email = "admin@mt.local" }, pwd);

            migrationBuilder.Sql($@"
                UPDATE AspNetUsers SET PasswordHash = '{docHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'DOCVERIFIER@MT.LOCAL';
                UPDATE AspNetUsers SET PasswordHash = '{finHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'FINALAPPROVER@MT.LOCAL';
                UPDATE AspNetUsers SET PasswordHash = '{minHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'MINISTRY@MT.LOCAL';
                UPDATE AspNetUsers SET PasswordHash = '{ownHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'OWNER@MT.LOCAL';
                UPDATE AspNetUsers SET PasswordHash = '{superHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'INFO@LUMEN-PATH.COM';
                UPDATE AspNetUsers SET PasswordHash = '{adminHash.Replace("'","''")}', SecurityStamp = NEWID() WHERE NormalizedEmail = 'ADMIN@MT.LOCAL';
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                -- Remove role mappings first
                DECLARE @DocUserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = 'DOCVERIFIER@MT.LOCAL');
                DECLARE @FinUserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = 'FINALAPPROVER@MT.LOCAL');
                DECLARE @MinUserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = 'MINISTRY@MT.LOCAL');
                DECLARE @OwnUserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = 'OWNER@MT.LOCAL');

                DELETE FROM AspNetUserRoles WHERE UserId IN (@DocUserId, @FinUserId, @MinUserId, @OwnUserId);

                -- Remove the users
                DELETE FROM AspNetUsers WHERE NormalizedEmail IN ('DOCVERIFIER@MT.LOCAL','FINALAPPROVER@MT.LOCAL','MINISTRY@MT.LOCAL','OWNER@MT.LOCAL');
            ");
        }
    }
}
