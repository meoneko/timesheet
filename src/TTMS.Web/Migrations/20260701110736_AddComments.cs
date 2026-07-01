using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTMS.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddComments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ActualBehaviorHtml",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Environment",
                table: "Tasks",
                type: "TEXT",
                maxLength: 500,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "ExpectedBehaviorHtml",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "ItemType",
                table: "Tasks",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "RelatedWorkItemId",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Severity",
                table: "Tasks",
                type: "INTEGER",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StepsToReproduceHtml",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "StepsToReproduceText",
                table: "Tasks",
                type: "TEXT",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "Comments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    EntityType = table.Column<int>(type: "INTEGER", nullable: false),
                    EntityId = table.Column<int>(type: "INTEGER", nullable: false),
                    ParentCommentId = table.Column<int>(type: "INTEGER", nullable: true),
                    AuthorId = table.Column<string>(type: "TEXT", nullable: false),
                    ContentHtml = table.Column<string>(type: "TEXT", nullable: false),
                    ContentText = table.Column<string>(type: "TEXT", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "TEXT", nullable: false),
                    IsDeleted = table.Column<bool>(type: "INTEGER", nullable: false),
                    DeletedAt = table.Column<DateTime>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Comments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Comments_AspNetUsers_AuthorId",
                        column: x => x.AuthorId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Comments_Comments_ParentCommentId",
                        column: x => x.ParentCommentId,
                        principalTable: "Comments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_WorkDate_UserId",
                table: "TimeEntries",
                columns: new[] { "WorkDate", "UserId" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ItemType_IsDeleted",
                table: "Tasks",
                columns: new[] { "ItemType", "IsDeleted" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_RelatedWorkItemId",
                table: "Tasks",
                column: "RelatedWorkItemId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_AuthorId",
                table: "Comments",
                column: "AuthorId");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_EntityType_EntityId",
                table: "Comments",
                columns: new[] { "EntityType", "EntityId" });

            migrationBuilder.CreateIndex(
                name: "IX_Comments_IsDeleted",
                table: "Comments",
                column: "IsDeleted");

            migrationBuilder.CreateIndex(
                name: "IX_Comments_ParentCommentId",
                table: "Comments",
                column: "ParentCommentId");

            migrationBuilder.AddForeignKey(
                name: "FK_Tasks_Tasks_RelatedWorkItemId",
                table: "Tasks",
                column: "RelatedWorkItemId",
                principalTable: "Tasks",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Tasks_Tasks_RelatedWorkItemId",
                table: "Tasks");

            migrationBuilder.DropTable(
                name: "Comments");

            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_WorkDate_UserId",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_ItemType_IsDeleted",
                table: "Tasks");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_RelatedWorkItemId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ActualBehaviorHtml",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Environment",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ExpectedBehaviorHtml",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "ItemType",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "RelatedWorkItemId",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "Severity",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "StepsToReproduceHtml",
                table: "Tasks");

            migrationBuilder.DropColumn(
                name: "StepsToReproduceText",
                table: "Tasks");
        }
    }
}
