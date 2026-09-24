using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Admittance : Migration
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
                    RegistrationId = table.Column<int>(type: "int", nullable: true),
                    OrderId = table.Column<int>(type: "int", nullable: true),
                    RowVersion = table.Column<byte[]>(type: "rowversion", rowVersion: true, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdmittanceApplications", x => x.Id);
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
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdmittanceApplications");
        }
    }
}
