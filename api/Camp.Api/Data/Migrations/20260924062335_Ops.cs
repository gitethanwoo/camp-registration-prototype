using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Ops : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OpsBuddyRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    RegistrationId = table.Column<int>(type: "int", nullable: false),
                    RequestedRegistrationId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpsBuddyRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpsBuddyRequests_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpsBuddyRequests_Registrations_RequestedRegistrationId",
                        column: x => x.RequestedRegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OpsCabins",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Gender = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Beds = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpsCabins", x => x.Id);
                    table.CheckConstraint("CK_OpsCabin_Beds", "[Beds] > 0");
                    table.ForeignKey(
                        name: "FK_OpsCabins_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OpsGroups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    PoolId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpsGroups", x => x.Id);
                    table.CheckConstraint("CK_OpsGroup_Capacity", "[Capacity] > 0");
                    table.ForeignKey(
                        name: "FK_OpsGroups_CapacityPools_PoolId",
                        column: x => x.PoolId,
                        principalTable: "CapacityPools",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpsGroups_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OpsPickupAdults",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Relationship = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpsPickupAdults", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpsPickupAdults_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OpsRoomingReviews",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ReviewedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpsRoomingReviews", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpsRoomingReviews_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "OpsPlacements",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegistrationId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    CabinId = table.Column<int>(type: "int", nullable: true),
                    GroupId = table.Column<int>(type: "int", nullable: true),
                    SuggestedGroupId = table.Column<int>(type: "int", nullable: true),
                    SuggestionReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Activity = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    RemindedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckedInAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckedInBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CheckInOverride = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    CheckedOutAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    CheckedOutBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PickedUpBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpsPlacements", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpsPlacements_OpsCabins_CabinId",
                        column: x => x.CabinId,
                        principalTable: "OpsCabins",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpsPlacements_OpsGroups_GroupId",
                        column: x => x.GroupId,
                        principalTable: "OpsGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpsPlacements_OpsGroups_SuggestedGroupId",
                        column: x => x.SuggestedGroupId,
                        principalTable: "OpsGroups",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_OpsPlacements_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_OpsBuddyRequests_RegistrationId_RequestedRegistrationId",
                table: "OpsBuddyRequests",
                columns: new[] { "RegistrationId", "RequestedRegistrationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpsBuddyRequests_RequestedRegistrationId",
                table: "OpsBuddyRequests",
                column: "RequestedRegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_OpsBuddyRequests_SessionId",
                table: "OpsBuddyRequests",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_OpsCabins_SessionId_SortOrder",
                table: "OpsCabins",
                columns: new[] { "SessionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_OpsGroups_PoolId_SortOrder",
                table: "OpsGroups",
                columns: new[] { "PoolId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_OpsGroups_SessionId",
                table: "OpsGroups",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_OpsPickupAdults_HouseholdId",
                table: "OpsPickupAdults",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_OpsPlacements_CabinId",
                table: "OpsPlacements",
                column: "CabinId");

            migrationBuilder.CreateIndex(
                name: "IX_OpsPlacements_GroupId",
                table: "OpsPlacements",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_OpsPlacements_RegistrationId",
                table: "OpsPlacements",
                column: "RegistrationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpsPlacements_SessionId",
                table: "OpsPlacements",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_OpsPlacements_SuggestedGroupId",
                table: "OpsPlacements",
                column: "SuggestedGroupId");

            migrationBuilder.CreateIndex(
                name: "IX_OpsRoomingReviews_SessionId",
                table: "OpsRoomingReviews",
                column: "SessionId",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OpsBuddyRequests");

            migrationBuilder.DropTable(
                name: "OpsPickupAdults");

            migrationBuilder.DropTable(
                name: "OpsPlacements");

            migrationBuilder.DropTable(
                name: "OpsRoomingReviews");

            migrationBuilder.DropTable(
                name: "OpsCabins");

            migrationBuilder.DropTable(
                name: "OpsGroups");
        }
    }
}
