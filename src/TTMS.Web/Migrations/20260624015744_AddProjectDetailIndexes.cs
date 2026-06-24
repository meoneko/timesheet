using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTMS.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddProjectDetailIndexes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TimeEntries_TaskId_WorkDate",
                table: "TimeEntries",
                columns: new[] { "TaskId", "WorkDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Tasks_ProjectId_IsDeleted_UpdatedAt",
                table: "Tasks",
                columns: new[] { "ProjectId", "IsDeleted", "UpdatedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TimeEntries_TaskId_WorkDate",
                table: "TimeEntries");

            migrationBuilder.DropIndex(
                name: "IX_Tasks_ProjectId_IsDeleted_UpdatedAt",
                table: "Tasks");
        }
    }
}
