using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Host : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
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
                name: "HostInvoiceLine",
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
                    table.PrimaryKey("PK_HostInvoiceLine", x => x.Id);
                    table.ForeignKey(
                        name: "FK_HostInvoiceLine_HostInvoices_InvoiceId",
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
                name: "IX_HostEvents_HostOrganizationId",
                table: "HostEvents",
                column: "HostOrganizationId");

            migrationBuilder.CreateIndex(
                name: "IX_HostEvents_SessionId",
                table: "HostEvents",
                column: "SessionId",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_HostInvoiceLine_InvoiceId",
                table: "HostInvoiceLine",
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
                name: "IX_VolunteerUploadRows_UploadId_RowNumber",
                table: "VolunteerUploadRows",
                columns: new[] { "UploadId", "RowNumber" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_VolunteerUploads_HostOrganizationId",
                table: "VolunteerUploads",
                column: "HostOrganizationId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "HostInvoiceLine");

            migrationBuilder.DropTable(
                name: "HostInvoicePayments");

            migrationBuilder.DropTable(
                name: "HostMembers");

            migrationBuilder.DropTable(
                name: "HostVolunteers");

            migrationBuilder.DropTable(
                name: "VolunteerUploadRows");

            migrationBuilder.DropTable(
                name: "HostInvoices");

            migrationBuilder.DropTable(
                name: "VolunteerUploads");

            migrationBuilder.DropTable(
                name: "HostEvents");

            migrationBuilder.DropTable(
                name: "HostOrganizations");
        }
    }
}
