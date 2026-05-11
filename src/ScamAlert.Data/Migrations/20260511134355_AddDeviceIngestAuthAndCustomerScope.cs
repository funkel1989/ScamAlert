using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScamAlert.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddDeviceIngestAuthAndCustomerScope : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "IngestApiKeyCreatedUtc",
                table: "Devices",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "IngestApiKeyHash",
                table: "Devices",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "CustomerScopeCsv",
                table: "AuthUserCredentials",
                type: "TEXT",
                maxLength: 2000,
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IngestApiKeyCreatedUtc",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "IngestApiKeyHash",
                table: "Devices");

            migrationBuilder.DropColumn(
                name: "CustomerScopeCsv",
                table: "AuthUserCredentials");
        }
    }
}
