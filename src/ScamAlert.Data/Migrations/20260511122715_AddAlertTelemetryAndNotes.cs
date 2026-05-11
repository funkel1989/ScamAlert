using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ScamAlert.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddAlertTelemetryAndNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "DecisionReason",
                table: "AlertEvents",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "DestinationIp",
                table: "AlertEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Direction",
                table: "AlertEvents",
                type: "TEXT",
                maxLength: 16,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Notes",
                table: "AlertEvents",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ObservedBy",
                table: "AlertEvents",
                type: "TEXT",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "RuleApplied",
                table: "AlertEvents",
                type: "TEXT",
                maxLength: 128,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Transport",
                table: "AlertEvents",
                type: "TEXT",
                maxLength: 16,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DecisionReason",
                table: "AlertEvents");

            migrationBuilder.DropColumn(
                name: "DestinationIp",
                table: "AlertEvents");

            migrationBuilder.DropColumn(
                name: "Direction",
                table: "AlertEvents");

            migrationBuilder.DropColumn(
                name: "Notes",
                table: "AlertEvents");

            migrationBuilder.DropColumn(
                name: "ObservedBy",
                table: "AlertEvents");

            migrationBuilder.DropColumn(
                name: "RuleApplied",
                table: "AlertEvents");

            migrationBuilder.DropColumn(
                name: "Transport",
                table: "AlertEvents");
        }
    }
}
