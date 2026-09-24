using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class StaffCx : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DiscountRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiscountCodeId = table.Column<int>(type: "int", nullable: false),
                    RequesterType = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Organization = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ProgramId = table.Column<int>(type: "int", nullable: false),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: false),
                    MaxUses = table.Column<int>(type: "int", nullable: true),
                    Stackable = table.Column<bool>(type: "bit", nullable: false),
                    OverridesOtherCodes = table.Column<bool>(type: "bit", nullable: false),
                    RequesterNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    RequestedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Decision = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ReviewNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ReviewedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DiscountRequests_DiscountCodes_DiscountCodeId",
                        column: x => x.DiscountCodeId,
                        principalTable: "DiscountCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiscountRequests_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HouseholdMerges",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurvivorHouseholdId = table.Column<int>(type: "int", nullable: false),
                    MergedHouseholdId = table.Column<int>(type: "int", nullable: false),
                    DetailJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdMerges", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdNotes",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false),
                    Author = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdNotes", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdNotes_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HouseholdVerifications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    ItemKey = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CheckedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CheckedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdVerifications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdVerifications_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TransferRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegistrationId = table.Column<int>(type: "int", nullable: false),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    FromSessionId = table.Column<int>(type: "int", nullable: false),
                    FromPoolId = table.Column<int>(type: "int", nullable: false),
                    ToSessionId = table.Column<int>(type: "int", nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    RequestedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    PriceDifferenceCents = table.Column<int>(type: "int", nullable: true),
                    RefundCents = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TransferRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TransferRequests_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransferRequests_Sessions_FromSessionId",
                        column: x => x.FromSessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_TransferRequests_Sessions_ToSessionId",
                        column: x => x.ToSessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRequests_DiscountCodeId",
                table: "DiscountRequests",
                column: "DiscountCodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRequests_ProgramId",
                table: "DiscountRequests",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMerges_MergedHouseholdId",
                table: "HouseholdMerges",
                column: "MergedHouseholdId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdMerges_SurvivorHouseholdId",
                table: "HouseholdMerges",
                column: "SurvivorHouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdNotes_HouseholdId_CreatedAt",
                table: "HouseholdNotes",
                columns: new[] { "HouseholdId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdVerifications_HouseholdId_ItemKey",
                table: "HouseholdVerifications",
                columns: new[] { "HouseholdId", "ItemKey" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_FromSessionId",
                table: "TransferRequests",
                column: "FromSessionId");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_RegistrationId",
                table: "TransferRequests",
                column: "RegistrationId",
                unique: true,
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_Status_CreatedAt",
                table: "TransferRequests",
                columns: new[] { "Status", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_TransferRequests_ToSessionId",
                table: "TransferRequests",
                column: "ToSessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DiscountRequests");

            migrationBuilder.DropTable(
                name: "HouseholdMerges");

            migrationBuilder.DropTable(
                name: "HouseholdNotes");

            migrationBuilder.DropTable(
                name: "HouseholdVerifications");

            migrationBuilder.DropTable(
                name: "TransferRequests");
        }
    }
}
