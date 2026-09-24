using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Setup : Migration
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
                name: "IX_SessionSetups_SessionId",
                table: "SessionSetups",
                column: "SessionId",
                unique: true);

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

            // K12 / FR-76: audit rows are append-only. Nothing in the app updates or deletes them,
            // and the database refuses it too.
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
                name: "ProgramApprovalSteps");

            migrationBuilder.DropTable(
                name: "RefundTiers");

            migrationBuilder.DropTable(
                name: "SessionSetups");

            migrationBuilder.DropTable(
                name: "WaiverVersions");

            migrationBuilder.DropTable(
                name: "ProgramSetups");
        }
    }
}
