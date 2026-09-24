using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Finance : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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

            migrationBuilder.CreateIndex(
                name: "IX_FinanceCardsOnFile_OrderId",
                table: "FinanceCardsOnFile",
                column: "OrderId",
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FinanceCardsOnFile");

            migrationBuilder.DropTable(
                name: "InstallmentFailures");

            migrationBuilder.DropTable(
                name: "JournalEvent");

            migrationBuilder.DropTable(
                name: "ScholarshipAwardLines");

            migrationBuilder.DropTable(
                name: "ScholarshipDocuments");

            migrationBuilder.DropTable(
                name: "SettlementLines");

            migrationBuilder.DropTable(
                name: "JournalBatches");

            migrationBuilder.DropTable(
                name: "ScholarshipApplications");

            migrationBuilder.DropTable(
                name: "SettlementBatches");
        }
    }
}
