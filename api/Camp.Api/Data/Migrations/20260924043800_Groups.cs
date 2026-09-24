using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Groups : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "GroupRegistrations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    LeaderHouseholdId = table.Column<int>(type: "int", nullable: false),
                    LeaderName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ConfirmedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupRegistrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupRegistrations_Households_LeaderHouseholdId",
                        column: x => x.LeaderHouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GroupRegistrations_PaymentOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_GroupRegistrations_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "GroupAttendees",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    GroupId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(120)", maxLength: 120, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(254)", maxLength: 254, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false),
                    TokenHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: true),
                    LinkSentAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    FormStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AnswersJson = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false),
                    Withdrawal = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    WithdrawalReason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    WithdrawalRequestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    WithdrawalResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupAttendees", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupAttendees_GroupRegistrations_GroupId",
                        column: x => x.GroupId,
                        principalTable: "GroupRegistrations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "GroupWaiverAcceptances",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AttendeeId = table.Column<int>(type: "int", nullable: false),
                    WaiverTemplateId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    SignerName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    AcceptedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_GroupWaiverAcceptances", x => x.Id);
                    table.ForeignKey(
                        name: "FK_GroupWaiverAcceptances_GroupAttendees_AttendeeId",
                        column: x => x.AttendeeId,
                        principalTable: "GroupAttendees",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_GroupWaiverAcceptances_WaiverTemplates_WaiverTemplateId",
                        column: x => x.WaiverTemplateId,
                        principalTable: "WaiverTemplates",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_GroupAttendees_GroupId",
                table: "GroupAttendees",
                column: "GroupId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupAttendees_TokenHash",
                table: "GroupAttendees",
                column: "TokenHash",
                unique: true,
                filter: "[TokenHash] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_GroupRegistrations_LeaderHouseholdId",
                table: "GroupRegistrations",
                column: "LeaderHouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupRegistrations_OrderId",
                table: "GroupRegistrations",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupRegistrations_SessionId",
                table: "GroupRegistrations",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupWaiverAcceptances_AttendeeId",
                table: "GroupWaiverAcceptances",
                column: "AttendeeId");

            migrationBuilder.CreateIndex(
                name: "IX_GroupWaiverAcceptances_WaiverTemplateId",
                table: "GroupWaiverAcceptances",
                column: "WaiverTemplateId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "GroupWaiverAcceptances");

            migrationBuilder.DropTable(
                name: "GroupAttendees");

            migrationBuilder.DropTable(
                name: "GroupRegistrations");
        }
    }
}
