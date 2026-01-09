using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalBot.Migrations
{
    /// <inheritdoc />
    public partial class AddIsSystemToCategory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsSystem",
                table: "AppCategories",
                type: "boolean",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsSystem",
                table: "AppCategories");
        }
    }
}
