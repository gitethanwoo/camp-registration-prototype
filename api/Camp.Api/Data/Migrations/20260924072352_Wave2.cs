using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Wave2 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AuditChanges",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    AuditEventId = table.Column<long>(type: "bigint", nullable: false),
                    Field = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Before = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true),
                    After = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AuditChanges", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AuditChanges_AuditEvents_AuditEventId",
                        column: x => x.AuditEventId,
                        principalTable: "AuditEvents",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "DiscountRules",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    DiscountCodeId = table.Column<int>(type: "int", nullable: false),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ProgramId = table.Column<int>(type: "int", nullable: true),
                    SessionId = table.Column<int>(type: "int", nullable: true),
                    ValidFrom = table.Column<DateOnly>(type: "date", nullable: false),
                    ValidTo = table.Column<DateOnly>(type: "date", nullable: false),
                    MaxUses = table.Column<int>(type: "int", nullable: true),
                    Uses = table.Column<int>(type: "int", nullable: false),
                    Stackable = table.Column<bool>(type: "bit", nullable: false),
                    Active = table.Column<bool>(type: "bit", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DiscountRules", x => x.Id);
                    table.CheckConstraint("CK_DiscountRule_Uses", "[Uses] >= 0 AND ([MaxUses] IS NULL OR [Uses] <= [MaxUses])");
                    table.ForeignKey(
                        name: "FK_DiscountRules_DiscountCodes_DiscountCodeId",
                        column: x => x.DiscountCodeId,
                        principalTable: "DiscountCodes",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DiscountRules_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_DiscountRules_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "FinanceCardsOnFile",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    Brand = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Last4 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    VaultRef = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FinanceCardsOnFile", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FinanceCardsOnFile_PaymentOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostOrganizations",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    City = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostOrganizations", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "InstallmentFailures",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InstallmentId = table.Column<int>(type: "int", nullable: false),
                    FailedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    DeclineReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    NextRetryOn = table.Column<DateOnly>(type: "date", nullable: true),
                    LastContactedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InstallmentFailures", x => x.Id);
                    table.ForeignKey(
                        name: "FK_InstallmentFailures_Installments_InstallmentId",
                        column: x => x.InstallmentId,
                        principalTable: "Installments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

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
                name: "ProgramSetups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProgramId = table.Column<int>(type: "int", nullable: false),
                    State = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SubmittedByEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramSetups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgramSetups_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RefundTiers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    DaysBefore = table.Column<int>(type: "int", nullable: false),
                    RefundPercent = table.Column<int>(type: "int", nullable: false),
                    Basis = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AdminFeeCents = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RefundTiers", x => x.Id);
                    table.CheckConstraint("CK_RefundTier_Percent", "[RefundPercent] BETWEEN 0 AND 100 AND [DaysBefore] >= 0 AND [AdminFeeCents] >= 0");
                    table.ForeignKey(
                        name: "FK_RefundTiers_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScholarshipApplications",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HouseholdId = table.Column<int>(type: "int", nullable: false),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    RequestedCents = table.Column<int>(type: "int", nullable: false),
                    IncomeBand = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    AwardCents = table.Column<int>(type: "int", nullable: false),
                    DecidedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DecidedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    DecisionNote = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScholarshipApplications", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScholarshipApplications_Households_HouseholdId",
                        column: x => x.HouseholdId,
                        principalTable: "Households",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScholarshipApplications_PaymentOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SessionSetups",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    Location = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    RegistrationOpensAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PriorityOpensAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SessionSetups", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SessionSetups_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SettlementBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SettledOn = table.Column<DateOnly>(type: "date", nullable: false),
                    ReceivedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementBatches", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "WaiverVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    WaiverTemplateId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Body = table.Column<string>(type: "nvarchar(max)", maxLength: 8000, nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    RetiredDate = table.Column<DateOnly>(type: "date", nullable: true),
                    CreatedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SubmittedByEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WaiverVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_WaiverVersions_WaiverTemplates_WaiverTemplateId",
                        column: x => x.WaiverTemplateId,
                        principalTable: "WaiverTemplates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostEvents",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HostOrganizationId = table.Column<int>(type: "int", nullable: false),
                    SessionId = table.Column<int>(type: "int", nullable: false),
                    LastYearRegistrations = table.Column<int>(type: "int", nullable: false),
                    VettingDeadline = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostEvents_HostOrganizations_HostOrganizationId",
                        column: x => x.HostOrganizationId,
                        principalTable: "HostOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_HostEvents_Sessions_SessionId",
                        column: x => x.SessionId,
                        principalTable: "Sessions",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "HostMembers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HostOrganizationId = table.Column<int>(type: "int", nullable: false),
                    Email = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Title = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostMembers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostMembers_HostOrganizations_HostOrganizationId",
                        column: x => x.HostOrganizationId,
                        principalTable: "HostOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostVolunteers",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HostOrganizationId = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DateOfBirth = table.Column<DateOnly>(type: "date", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    VettingStatus = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UploadRowId = table.Column<int>(type: "int", nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostVolunteers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostVolunteers_HostOrganizations_HostOrganizationId",
                        column: x => x.HostOrganizationId,
                        principalTable: "HostOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VolunteerUploads",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HostOrganizationId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    UploadedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastSubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VolunteerUploads", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VolunteerUploads_HostOrganizations_HostOrganizationId",
                        column: x => x.HostOrganizationId,
                        principalTable: "HostOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
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

            migrationBuilder.CreateTable(
                name: "ProgramApprovalSteps",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProgramSetupId = table.Column<int>(type: "int", nullable: false),
                    Sequence = table.Column<int>(type: "int", nullable: false),
                    Role = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ApprovedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ApprovedByEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProgramApprovalSteps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProgramApprovalSteps_ProgramSetups_ProgramSetupId",
                        column: x => x.ProgramSetupId,
                        principalTable: "ProgramSetups",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScholarshipAwardLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    RegistrationId = table.Column<int>(type: "int", nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScholarshipAwardLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScholarshipAwardLines_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_ScholarshipAwardLines_ScholarshipApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "ScholarshipApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ScholarshipDocuments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ApplicationId = table.Column<int>(type: "int", nullable: false),
                    FileName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    ContentType = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SizeBytes = table.Column<int>(type: "int", nullable: false),
                    Content = table.Column<byte[]>(type: "varbinary(max)", nullable: false),
                    UploadedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ScholarshipDocuments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ScholarshipDocuments_ScholarshipApplications_ApplicationId",
                        column: x => x.ApplicationId,
                        principalTable: "ScholarshipApplications",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalBatches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Reference = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    SettlementBatchId = table.Column<int>(type: "int", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ErrorDetail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalBatches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalBatches_SettlementBatches_SettlementBatchId",
                        column: x => x.SettlementBatchId,
                        principalTable: "SettlementBatches",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateTable(
                name: "SettlementLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    BatchId = table.Column<int>(type: "int", nullable: false),
                    Kind = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProcessorRef = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    TransactedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CardholderName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CardLast4 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PaymentOperationId = table.Column<int>(type: "int", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    UnmatchedReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Resolution = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: true),
                    ResolutionNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ResolvedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ResolvedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SettlementLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SettlementLines_PaymentOperations_PaymentOperationId",
                        column: x => x.PaymentOperationId,
                        principalTable: "PaymentOperations",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_SettlementLines_SettlementBatches_BatchId",
                        column: x => x.BatchId,
                        principalTable: "SettlementBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostInvoices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    HostOrganizationId = table.Column<int>(type: "int", nullable: false),
                    HostEventId = table.Column<int>(type: "int", nullable: true),
                    Number = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Period = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    IssuedOn = table.Column<DateOnly>(type: "date", nullable: false),
                    DueDate = table.Column<DateOnly>(type: "date", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostInvoices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostInvoices_HostEvents_HostEventId",
                        column: x => x.HostEventId,
                        principalTable: "HostEvents",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_HostInvoices_HostOrganizations_HostOrganizationId",
                        column: x => x.HostOrganizationId,
                        principalTable: "HostOrganizations",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "VolunteerUploadRows",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    UploadId = table.Column<int>(type: "int", nullable: false),
                    RowNumber = table.Column<int>(type: "int", nullable: false),
                    FirstName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    LastName = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Email = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Phone = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    DateOfBirth = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Role = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Issue = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Detail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    Field = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_VolunteerUploadRows", x => x.Id);
                    table.ForeignKey(
                        name: "FK_VolunteerUploadRows_VolunteerUploads_UploadId",
                        column: x => x.UploadId,
                        principalTable: "VolunteerUploads",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "JournalEvent",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    JournalBatchId = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    At = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_JournalEvent", x => x.Id);
                    table.ForeignKey(
                        name: "FK_JournalEvent_JournalBatches_JournalBatchId",
                        column: x => x.JournalBatchId,
                        principalTable: "JournalBatches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostInvoiceLines",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    Description = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostInvoiceLines", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostInvoiceLines_HostInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "HostInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "HostInvoicePayments",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    InvoiceId = table.Column<int>(type: "int", nullable: false),
                    IdempotencyKey = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AmountCents = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ProcessorRef = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CardLast4 = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    DeclineReason = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    PaidBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_HostInvoicePayments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostInvoicePayments_HostInvoices_InvoiceId",
                        column: x => x.InvoiceId,
                        principalTable: "HostInvoices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AuditChanges_AuditEventId",
                table: "AuditChanges",
                column: "AuditEventId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRules_DiscountCodeId",
                table: "DiscountRules",
                column: "DiscountCodeId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRules_ProgramId",
                table: "DiscountRules",
                column: "ProgramId");

            migrationBuilder.CreateIndex(
                name: "IX_DiscountRules_SessionId",
                table: "DiscountRules",
                column: "SessionId");

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCardsOnFile_OrderId",
                table: "FinanceCardsOnFile",
                column: "OrderId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostEvents_HostOrganizationId",
                table: "HostEvents",
                column: "HostOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_HostEvents_SessionId",
                table: "HostEvents",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostInvoiceLines_InvoiceId",
                table: "HostInvoiceLines",
                column: "InvoiceId");

            migrationBuilder.CreateIndex(
                name: "IX_HostInvoicePayments_IdempotencyKey",
                table: "HostInvoicePayments",
                column: "IdempotencyKey",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostInvoicePayments_InvoiceId",
                table: "HostInvoicePayments",
                column: "InvoiceId",
                unique: true,
                filter: "[Status] = 'Pending'");

            migrationBuilder.CreateIndex(
                name: "IX_HostInvoices_HostEventId",
                table: "HostInvoices",
                column: "HostEventId");

            migrationBuilder.CreateIndex(
                name: "IX_HostInvoices_HostOrganizationId",
                table: "HostInvoices",
                column: "HostOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_HostInvoices_Number",
                table: "HostInvoices",
                column: "Number",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostMembers_Email",
                table: "HostMembers",
                column: "Email",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostMembers_HostOrganizationId",
                table: "HostMembers",
                column: "HostOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_HostOrganizations_Name",
                table: "HostOrganizations",
                column: "Name",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostVolunteers_HostOrganizationId_Email",
                table: "HostVolunteers",
                columns: new[] { "HostOrganizationId", "Email" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InstallmentFailures_InstallmentId",
                table: "InstallmentFailures",
                column: "InstallmentId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalBatches_Reference",
                table: "JournalBatches",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalBatches_SettlementBatchId",
                table: "JournalBatches",
                column: "SettlementBatchId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_JournalEvent_JournalBatchId",
                table: "JournalEvent",
                column: "JournalBatchId");

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

            migrationBuilder.CreateIndex(
                name: "IX_ProgramApprovalSteps_ProgramSetupId_Sequence",
                table: "ProgramApprovalSteps",
                columns: new[] { "ProgramSetupId", "Sequence" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProgramSetups_ProgramId",
                table: "ProgramSetups",
                column: "ProgramId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_RefundTiers_SessionId_DaysBefore",
                table: "RefundTiers",
                columns: new[] { "SessionId", "DaysBefore" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScholarshipApplications_HouseholdId",
                table: "ScholarshipApplications",
                column: "HouseholdId");

            migrationBuilder.CreateIndex(
                name: "IX_ScholarshipApplications_OrderId",
                table: "ScholarshipApplications",
                column: "OrderId",
                unique: true,
                filter: "[Status] = 'Submitted'");

            migrationBuilder.CreateIndex(
                name: "IX_ScholarshipApplications_Status_SubmittedAt",
                table: "ScholarshipApplications",
                columns: new[] { "Status", "SubmittedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_ScholarshipAwardLines_ApplicationId_RegistrationId",
                table: "ScholarshipAwardLines",
                columns: new[] { "ApplicationId", "RegistrationId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ScholarshipAwardLines_RegistrationId",
                table: "ScholarshipAwardLines",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_ScholarshipDocuments_ApplicationId",
                table: "ScholarshipDocuments",
                column: "ApplicationId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SessionSetups_SessionId",
                table: "SessionSetups",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatches_Reference",
                table: "SettlementBatches",
                column: "Reference",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_SettlementBatches_SettledOn",
                table: "SettlementBatches",
                column: "SettledOn");

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_BatchId_Status",
                table: "SettlementLines",
                columns: new[] { "BatchId", "Status" });

            migrationBuilder.CreateIndex(
                name: "IX_SettlementLines_PaymentOperationId",
                table: "SettlementLines",
                column: "PaymentOperationId",
                unique: true,
                filter: "[PaymentOperationId] IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "IX_VolunteerUploadRows_UploadId_RowNumber",
                table: "VolunteerUploadRows",
                columns: new[] { "UploadId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VolunteerUploads_HostOrganizationId",
                table: "VolunteerUploads",
                column: "HostOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_WaiverVersions_OneOpenDraft",
                table: "WaiverVersions",
                column: "WaiverTemplateId",
                unique: true,
                filter: "[Status] IN ('Draft', 'PendingApproval')");

            migrationBuilder.CreateIndex(
                name: "IX_WaiverVersions_WaiverTemplateId_Version",
                table: "WaiverVersions",
                columns: new[] { "WaiverTemplateId", "Version" },
                unique: true);

            // K12 / FR-76 (carried from the setup slice's migration): audit rows are append-only.
            // Nothing in the app updates or deletes them, and the database refuses it too.
            migrationBuilder.Sql("""
                CREATE TRIGGER TR_AuditEvents_Immutable ON AuditEvents
                INSTEAD OF UPDATE, DELETE
                AS
                BEGIN
                    SET NOCOUNT ON;
                    THROW 51000, 'Audit rows can''t be changed or deleted.', 1;
                END
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER IF EXISTS TR_AuditEvents_Immutable;");

            migrationBuilder.DropTable(
                name: "AuditChanges");

            migrationBuilder.DropTable(
                name: "DiscountRules");

            migrationBuilder.DropTable(
                name: "FinanceCardsOnFile");

            migrationBuilder.DropTable(
                name: "HostInvoiceLines");

            migrationBuilder.DropTable(
                name: "HostInvoicePayments");

            migrationBuilder.DropTable(
                name: "HostMembers");

            migrationBuilder.DropTable(
                name: "HostVolunteers");

            migrationBuilder.DropTable(
                name: "InstallmentFailures");

            migrationBuilder.DropTable(
                name: "JournalEvent");

            migrationBuilder.DropTable(
                name: "OpsBuddyRequests");

            migrationBuilder.DropTable(
                name: "OpsPickupAdults");

            migrationBuilder.DropTable(
                name: "OpsPlacements");

            migrationBuilder.DropTable(
                name: "OpsRoomingReviews");

            migrationBuilder.DropTable(
                name: "ProgramApprovalSteps");

            migrationBuilder.DropTable(
                name: "RefundTiers");

            migrationBuilder.DropTable(
                name: "ScholarshipAwardLines");

            migrationBuilder.DropTable(
                name: "ScholarshipDocuments");

            migrationBuilder.DropTable(
                name: "SessionSetups");

            migrationBuilder.DropTable(
                name: "SettlementLines");

            migrationBuilder.DropTable(
                name: "VolunteerUploadRows");

            migrationBuilder.DropTable(
                name: "WaiverVersions");

            migrationBuilder.DropTable(
                name: "HostInvoices");

            migrationBuilder.DropTable(
                name: "JournalBatches");

            migrationBuilder.DropTable(
                name: "OpsCabins");

            migrationBuilder.DropTable(
                name: "OpsGroups");

            migrationBuilder.DropTable(
                name: "ProgramSetups");

            migrationBuilder.DropTable(
                name: "ScholarshipApplications");

            migrationBuilder.DropTable(
                name: "VolunteerUploads");

            migrationBuilder.DropTable(
                name: "HostEvents");

            migrationBuilder.DropTable(
                name: "SettlementBatches");

            migrationBuilder.DropTable(
                name: "HostOrganizations");
        }
    }
}
