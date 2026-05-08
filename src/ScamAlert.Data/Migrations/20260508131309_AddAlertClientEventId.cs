using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScamAlert.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertClientEventId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "ClientEventId",
                table: "AlertEvents",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_AlertEvents_DeviceId_ClientEventId",
                table: "AlertEvents",
                columns: new[] { "DeviceId", "ClientEventId" },
                unique: true,
                filter: "\"ClientEventId\" IS NOT NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_AlertEvents_DeviceId_ClientEventId",
                table: "AlertEvents");

            migrationBuilder.DropColumn(
                name: "ClientEventId",
                table: "AlertEvents");
        }
    }
}
