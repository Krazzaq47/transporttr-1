using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MT.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddRejectAuditToVehicleRegistrations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RejectReason",
                table: "VehicleRegistrations",
                type: "nvarchar(1000)",
                maxLength: 1000,
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "RejectedAt",
                table: "VehicleRegistrations",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedByName",
                table: "VehicleRegistrations",
                type: "nvarchar(256)",
                maxLength: 256,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedByRole",
                table: "VehicleRegistrations",
                type: "nvarchar(100)",
                maxLength: 100,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RejectedByUserId",
                table: "VehicleRegistrations",
                type: "nvarchar(450)",
                maxLength: 450,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RejectReason",
                table: "VehicleRegistrations");

            migrationBuilder.DropColumn(
                name: "RejectedAt",
                table: "VehicleRegistrations");

            migrationBuilder.DropColumn(
                name: "RejectedByName",
                table: "VehicleRegistrations");

            migrationBuilder.DropColumn(
                name: "RejectedByRole",
                table: "VehicleRegistrations");

            migrationBuilder.DropColumn(
                name: "RejectedByUserId",
                table: "VehicleRegistrations");
        }
    }
}
