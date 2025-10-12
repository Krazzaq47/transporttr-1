using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT.Data.Migrations
{
    /// <inheritdoc />
    public partial class SeedRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Seed roles directly in the database (idempotent)
            migrationBuilder.Sql(@"
                DECLARE @roles TABLE (Name NVARCHAR(256), NormalizedName NVARCHAR(256));
                INSERT INTO @roles (Name, NormalizedName)
                VALUES ('Admin','ADMIN'),
                       ('DocumentVerifier','DOCUMENTVERIFIER'),
                       ('FinalApprover','FINALAPPROVER'),
                       ('MinistryOfficer','MINISTRYOFFICER'),
                       ('Owner','OWNER');

                MERGE AspNetRoles AS target
                USING (SELECT Name, NormalizedName FROM @roles) AS src
                ON target.NormalizedName = src.NormalizedName
                WHEN NOT MATCHED BY TARGET THEN
                  INSERT (Id, Name, NormalizedName, ConcurrencyStamp)
                  VALUES (NEWID(), src.Name, src.NormalizedName, NEWID());
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Remove the seeded roles and any user-role mappings for them
            migrationBuilder.Sql(@"
                ;WITH TargetRoles AS (
                  SELECT Id
                  FROM AspNetRoles
                  WHERE NormalizedName IN ('ADMIN','DOCUMENTVERIFIER','FINALAPPROVER','MINISTRYOFFICER','OWNER')
                )
                DELETE ur FROM AspNetUserRoles ur JOIN TargetRoles r ON ur.RoleId = r.Id;
                DELETE FROM AspNetRoles WHERE NormalizedName IN ('ADMIN','DOCUMENTVERIFIER','FINALAPPROVER','MINISTRYOFFICER','OWNER');
            ");
        }
    }
}

