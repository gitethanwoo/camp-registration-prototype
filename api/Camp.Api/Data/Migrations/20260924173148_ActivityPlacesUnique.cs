using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class ActivityPlacesUnique : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityAssignments_RegistrationId_Period",
                table: "ActivityAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_RegistrationId_Period",
                table: "ActivityAssignments",
                columns: new[] { "RegistrationId", "Period" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ActivityAssignments_RegistrationId_Period",
                table: "ActivityAssignments");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_RegistrationId_Period",
                table: "ActivityAssignments",
                columns: new[] { "RegistrationId", "Period" });
        }
    }
}
