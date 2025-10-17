using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddApprovalAudit : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ApproveComment",
                table: "VehicleRegistrations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "ApprovedAt",
                table: "VehicleRegistrations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByName",
                table: "VehicleRegistrations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByRole",
                table: "VehicleRegistrations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ApprovedByUserId",
                table: "VehicleRegistrations",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ApproveComment",
                table: "VehicleRegistrations");

            migrationBuilder.DropColumn(
                name: "ApprovedAt",
                table: "VehicleRegistrations");

            migrationBuilder.DropColumn(
                name: "ApprovedByName",
                table: "VehicleRegistrations");

            migrationBuilder.DropColumn(
                name: "ApprovedByRole",
                table: "VehicleRegistrations");

            migrationBuilder.DropColumn(
                name: "ApprovedByUserId",
                table: "VehicleRegistrations");
        }
    }
}
