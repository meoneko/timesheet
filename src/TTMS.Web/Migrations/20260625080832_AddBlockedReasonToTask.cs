using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace TTMS.Web.Migrations
{
    /// <inheritdoc />
    public partial class AddBlockedReasonToTask : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "BlockedReason",
                table: "Tasks",
                type: "TEXT",
                maxLength: 500,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "BlockedReason",
                table: "Tasks");
        }
    }
}
