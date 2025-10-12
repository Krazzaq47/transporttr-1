using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedSuperAdminRole : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1) Ensure SuperAdmin role exists (idempotent)
            migrationBuilder.Sql(@"
                IF NOT EXISTS (SELECT 1 FROM AspNetRoles WHERE NormalizedName = 'SUPERADMIN')
                BEGIN
                    INSERT INTO AspNetRoles (Id, Name, NormalizedName, ConcurrencyStamp)
                    VALUES (NEWID(), 'SuperAdmin', 'SUPERADMIN', NEWID());
                END
            ");

            // 2) Assign SuperAdmin role to the specified user if the user exists
            migrationBuilder.Sql(@"
                DECLARE @Email NVARCHAR(256) = 'info@lumen-path.com';
                DECLARE @UserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = UPPER(@Email));
                DECLARE @RoleId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetRoles WHERE NormalizedName = 'SUPERADMIN');

                IF @UserId IS NOT NULL AND @RoleId IS NOT NULL
                BEGIN
                    IF NOT EXISTS (SELECT 1 FROM AspNetUserRoles WHERE UserId = @UserId AND RoleId = @RoleId)
                    BEGIN
                        INSERT INTO AspNetUserRoles (UserId, RoleId) VALUES (@UserId, @RoleId);
                    END
                END
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove SuperAdmin assignment for the specific user
            migrationBuilder.Sql(@"
                DECLARE @Email NVARCHAR(256) = 'info@lumen-path.com';
                DECLARE @UserId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetUsers WHERE NormalizedEmail = UPPER(@Email));
                DECLARE @RoleId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetRoles WHERE NormalizedName = 'SUPERADMIN');
                IF @UserId IS NOT NULL AND @RoleId IS NOT NULL
                BEGIN
                    DELETE FROM AspNetUserRoles WHERE UserId = @UserId AND RoleId = @RoleId;
                END
            ");

            // Remove SuperAdmin role
            migrationBuilder.Sql(@"
                DECLARE @RoleId NVARCHAR(450) = (SELECT TOP 1 Id FROM AspNetRoles WHERE NormalizedName = 'SUPERADMIN');
                IF @RoleId IS NOT NULL
                BEGIN
                    DELETE FROM AspNetUserRoles WHERE RoleId = @RoleId;
                    DELETE FROM AspNetRoles WHERE Id = @RoleId;
                END
            ");
        }
    }
}
