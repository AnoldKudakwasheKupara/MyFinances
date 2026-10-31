using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MyFinances.Migrations
{
    /// <inheritdoc />
    public partial class ReminderDigests : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateOnly>(
                name: "LastReminderEmailOn",
                table: "UserSettings",
                type: "date",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastReminderEmailOn",
                table: "UserSettings");
        }
    }
}
