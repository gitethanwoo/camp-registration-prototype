using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Wave1 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdmittanceApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    ApplicantPersonId = table.Column<int>(type: "int", nullable: false),
                    SpousePersonId = table.Column<int>(type: "int", nullable: true),
                    SpouseFirstName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SpouseLastName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SpouseEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Stage = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CurrentStep = table.Column<int>(type: "int", nullable: false),
                    AnswersJson = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewStartedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReviewedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InfoRequest = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InfoRequestedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    InfoResponse = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    InfoRespondedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    Hold = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    AuthorizationRef = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CardLast4 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    AuthorizedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    AuthorizationExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    SeatHeld = table.Column<bool>(type: "bit", nullable: false),
                    PoolId = table.Column<int>(type: "int", nullable: true),
                    RegistrationId = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmittanceApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AdmittanceApplications_CapacityPools_PoolId",
                        column: x => x.PoolId,
                        principalTable: "CapacityPools",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdmittanceApplications_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdmittanceApplications_PaymentOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdmittanceApplications_People_ApplicantPersonId",
                        column: x => x.ApplicantPersonId,
                        principalTable: "People",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdmittanceApplications_People_SpousePersonId",
                        column: x => x.SpousePersonId,
                        principalTable: "People",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdmittanceApplications_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_AdmittanceApplications_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "BalancePayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    InstallmentSequence = table.Column<int>(type: "int", nullable: true),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CardLast4 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DeclineReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BalancePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BalancePayments_PaymentOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "HouseholdInvitations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    InvitedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ExpiresAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HouseholdInvitations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HouseholdInvitations_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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
                name: "IX_AdmittanceApplications_ApplicantPersonId",
                table: "AdmittanceApplications",
                column: "ApplicantPersonId");

            migrationBuilder.CreateIndex(
                name: "IX_AdmittanceApplications_HouseholdId_SessionId",
                table: "AdmittanceApplications",
                columns: new[] { "HouseholdId", "SessionId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AdmittanceApplications_OrderId",
                table: "AdmittanceApplications",
                column: "OrderId");

            migrationBuilder.CreateIndex(
                name: "IX_AdmittanceApplications_PoolId",
                table: "AdmittanceApplications",
                column: "PoolId");

            migrationBuilder.CreateIndex(
                name: "IX_AdmittanceApplications_RegistrationId",
                table: "AdmittanceApplications",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_AdmittanceApplications_SessionId_Stage",
                table: "AdmittanceApplications",
                columns: new[] { "SessionId", "Stage" });

            migrationBuilder.CreateIndex(
                name: "IX_AdmittanceApplications_SpousePersonId",
                table: "AdmittanceApplications",
                column: "SpousePersonId");

            migrationBuilder.CreateIndex(
                name: "IX_BalancePayments_IdempotencyKey",
                table: "BalancePayments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BalancePayments_OrderId",
                table: "BalancePayments",
                column: "OrderId",
                unique: true,
                filter: "[Status] = 'Pending'");

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

            migrationBuilder.CreateIndex(
                name: "IX_HouseholdInvitations_HouseholdId_Status",
                table: "HouseholdInvitations",
                columns: new[] { "HouseholdId", "Status" });

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
                name: "AdmittanceApplications");

            migrationBuilder.DropTable(
                name: "BalancePayments");

            migrationBuilder.DropTable(
                name: "DiscountRequests");

            migrationBuilder.DropTable(
                name: "GroupWaiverAcceptances");

            migrationBuilder.DropTable(
                name: "HouseholdInvitations");

            migrationBuilder.DropTable(
                name: "HouseholdMerges");

            migrationBuilder.DropTable(
                name: "HouseholdNotes");

            migrationBuilder.DropTable(
                name: "HouseholdVerifications");

            migrationBuilder.DropTable(
                name: "TransferRequests");

            migrationBuilder.DropTable(
                name: "GroupAttendees");

            migrationBuilder.DropTable(
                name: "GroupRegistrations");
        }
    }
}
