using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MedicalBot.Migrations
{
    /// <inheritdoc />
    public partial class AddEntityCodeToAppDefinitions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_AppDefinitions_AppCategories_CategoryId",
                table: "AppDefinitions");

            migrationBuilder.DropTable(
                name: "AppCategories");

            migrationBuilder.DropIndex(
                name: "IX_AppDefinitions_CategoryId",
                table: "AppDefinitions");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "AppDefinitions");

            migrationBuilder.DropColumn(
                name: "GroupName",
                table: "AppDefinitions");

            migrationBuilder.RenameColumn(
                name: "SystemCode",
                table: "AppDefinitions",
                newName: "EntityCode");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "EntityCode",
                table: "AppDefinitions",
                newName: "SystemCode");

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                table: "AppDefinitions",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "GroupName",
                table: "AppDefinitions",
                type: "text",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "AppCategories",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Icon = table.Column<string>(type: "text", nullable: true),
                    Name = table.Column<string>(type: "text", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppCategories", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AppDefinitions_CategoryId",
                table: "AppDefinitions",
                column: "CategoryId");

            migrationBuilder.AddForeignKey(
                name: "FK_AppDefinitions_AppCategories_CategoryId",
                table: "AppDefinitions",
                column: "CategoryId",
                principalTable: "AppCategories",
                principalColumn: "Id");
        }
    }
}
