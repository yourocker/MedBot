using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalBot.Migrations
{
    /// <inheritdoc />
    public partial class AddAppCategories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "AppCategoryId",
                table: "AppDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "text", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDefinitions_AppCategoryId",
                table: "AppDefinitions",
                column: "AppCategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppDefinitions_AppCategories_AppCategoryId",
                table: "AppDefinitions",
                column: "AppCategoryId",
                principalTable: "AppCategories",
                principalColumn: "Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppDefinitions_AppCategories_AppCategoryId",
                table: "AppDefinitions");

            migrationBuilder.DropTable(
                name: "AppCategories");

            migrationBuilder.DropIndex(
                name: "IX_AppDefinitions_AppCategoryId",
                table: "AppDefinitions");

            migrationBuilder.DropColumn(
                name: "AppCategoryId",
                table: "AppDefinitions");
        }
    }
}
