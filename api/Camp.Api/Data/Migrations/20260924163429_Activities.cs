using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Activities : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Activity",
                table: "OpsPlacements");

            migrationBuilder.CreateTable(
                name: "Activities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    Category = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    WhatToBring = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    ImageUrl = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    GradeMin = table.Column<int>(type: "int", nullable: false),
                    GradeMax = table.Column<int>(type: "int", nullable: false),
                    DefaultCapacity = table.Column<int>(type: "int", nullable: false),
                    StaffRatio = table.Column<int>(type: "int", nullable: false),
                    Instructor = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Space = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Activities", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ActivityBlocks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    GradeMin = table.Column<int>(type: "int", nullable: false),
                    GradeMax = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityBlocks", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityBlocks_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "CabinmateRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    RegistrationId = table.Column<int>(type: "int", nullable: false),
                    FriendName = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Contact = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    MatchedRegistrationId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CabinmateRequests", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CabinmateRequests_Registrations_MatchedRegistrationId",
                        column: x => x.MatchedRegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_CabinmateRequests_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ActivityPreferences",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegistrationId = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    Rank = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityPreferences", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityPreferences_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActivityPreferences_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ActivitySlots",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    BlockId = table.Column<int>(type: "int", nullable: false),
                    ActivityId = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    Capacity = table.Column<int>(type: "int", nullable: false),
                    Assigned = table.Column<int>(type: "int", nullable: false),
                    Instructor = table.Column<string>(type: "nvarchar(80)", maxLength: 80, nullable: false),
                    Space = table.Column<string>(type: "nvarchar(60)", maxLength: 60, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivitySlots", x => x.Id);
                    table.CheckConstraint("CK_ActivitySlot_Period", "[Period] BETWEEN 1 AND 3");
                    table.ForeignKey(
                        name: "FK_ActivitySlots_Activities_ActivityId",
                        column: x => x.ActivityId,
                        principalTable: "Activities",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActivitySlots_ActivityBlocks_BlockId",
                        column: x => x.BlockId,
                        principalTable: "ActivityBlocks",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActivitySlots_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "ActivityAssignments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    RegistrationId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    SlotId = table.Column<int>(type: "int", nullable: false),
                    Period = table.Column<int>(type: "int", nullable: false),
                    Source = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    AssignedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ActivityAssignments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_ActivitySlots_SlotId",
                        column: x => x.SlotId,
                        principalTable: "ActivitySlots",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ActivityAssignments_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Activities_Name",
                table: "Activities",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_RegistrationId_Period",
                table: "ActivityAssignments",
                columns: new[] { "RegistrationId", "Period" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_SessionId",
                table: "ActivityAssignments",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityAssignments_SlotId",
                table: "ActivityAssignments",
                column: "SlotId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityBlocks_SessionId_SortOrder",
                table: "ActivityBlocks",
                columns: new[] { "SessionId", "SortOrder" });

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPreferences_ActivityId",
                table: "ActivityPreferences",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivityPreferences_RegistrationId_Period_Rank",
                table: "ActivityPreferences",
                columns: new[] { "RegistrationId", "Period", "Rank" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySlots_ActivityId",
                table: "ActivitySlots",
                column: "ActivityId");

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySlots_BlockId_ActivityId_Period",
                table: "ActivitySlots",
                columns: new[] { "BlockId", "ActivityId", "Period" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ActivitySlots_SessionId",
                table: "ActivitySlots",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_CabinmateRequests_MatchedRegistrationId",
                table: "CabinmateRequests",
                column: "MatchedRegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_CabinmateRequests_RegistrationId",
                table: "CabinmateRequests",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_CabinmateRequests_SessionId",
                table: "CabinmateRequests",
                column: "SessionId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ActivityAssignments");

            migrationBuilder.DropTable(
                name: "ActivityPreferences");

            migrationBuilder.DropTable(
                name: "CabinmateRequests");

            migrationBuilder.DropTable(
                name: "ActivitySlots");

            migrationBuilder.DropTable(
                name: "Activities");

            migrationBuilder.DropTable(
                name: "ActivityBlocks");

            migrationBuilder.AddColumn<string>(
                name: "Activity",
                table: "OpsPlacements",
                type: "nvarchar(400)",
                maxLength: 400,
                nullable: true);
        }
    }
}
