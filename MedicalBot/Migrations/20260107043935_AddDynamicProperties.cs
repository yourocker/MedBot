using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalBot.Migrations
{
    /// <inheritdoc />
    public partial class AddDynamicProperties : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Properties",
                table: "StaffAppointments",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Properties",
                table: "Positions",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Properties",
                table: "Departments",
                type: "text",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Properties",
                table: "StaffAppointments");

            migrationBuilder.DropColumn(
                name: "Properties",
                table: "Positions");

            migrationBuilder.DropColumn(
                name: "Properties",
                table: "Departments");
        }
    }
}
