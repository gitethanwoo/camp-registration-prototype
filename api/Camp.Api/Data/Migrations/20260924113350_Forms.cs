using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Camp.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Forms : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FormVersions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProgramId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<int>(type: "int", nullable: false),
                    Status = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    ChangeNote = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: false),
                    CreatedByEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    SubmittedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SubmittedByEmail = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    SubmittedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    ReturnNote = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    ApprovedBy = table.Column<string>(type: "nvarchar(400)", maxLength: 400, nullable: true),
                    ApprovedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    PublishedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    RetiredAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormVersions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormVersions_Programs_ProgramId",
                        column: x => x.ProgramId,
                        principalTable: "Programs",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormQuestions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    FormVersionId = table.Column<int>(type: "int", nullable: false),
                    Key = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Label = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    HelpText = table.Column<string>(type: "nvarchar(300)", maxLength: 300, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Scope = table.Column<string>(type: "nvarchar(32)", maxLength: 32, nullable: false),
                    Required = table.Column<bool>(type: "bit", nullable: false),
                    Options = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    ShowWhenKey = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: true),
                    ShowWhenValue = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    SortOrder = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormQuestions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormQuestions_FormVersions_FormVersionId",
                        column: x => x.FormVersionId,
                        principalTable: "FormVersions",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FormAnswers",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    OrderId = table.Column<int>(type: "int", nullable: false),
                    RegistrationId = table.Column<int>(type: "int", nullable: true),
                    FormVersionId = table.Column<int>(type: "int", nullable: false),
                    FormQuestionId = table.Column<int>(type: "int", nullable: false),
                    Value = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FormAnswers", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FormAnswers_FormQuestions_FormQuestionId",
                        column: x => x.FormQuestionId,
                        principalTable: "FormQuestions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FormAnswers_FormVersions_FormVersionId",
                        column: x => x.FormVersionId,
                        principalTable: "FormVersions",
                        principalColumn: "Id");
                    table.ForeignKey(
                        name: "FK_FormAnswers_PaymentOrders_OrderId",
                        column: x => x.OrderId,
                        principalTable: "PaymentOrders",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FormAnswers_Registrations_RegistrationId",
                        column: x => x.RegistrationId,
                        principalTable: "Registrations",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_FormAnswers_FormQuestionId",
                table: "FormAnswers",
                column: "FormQuestionId");

            migrationBuilder.CreateIndex(
                name: "IX_FormAnswers_FormVersionId",
                table: "FormAnswers",
                column: "FormVersionId");

            migrationBuilder.CreateIndex(
                name: "IX_FormAnswers_OrderId_RegistrationId",
                table: "FormAnswers",
                columns: new[] { "OrderId", "RegistrationId" });

            migrationBuilder.CreateIndex(
                name: "IX_FormAnswers_RegistrationId",
                table: "FormAnswers",
                column: "RegistrationId");

            migrationBuilder.CreateIndex(
                name: "IX_FormQuestions_FormVersionId_Key",
                table: "FormQuestions",
                columns: new[] { "FormVersionId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FormVersions_OneLive",
                table: "FormVersions",
                column: "ProgramId",
                unique: true,
                filter: "[Status] = 'Published'");

            migrationBuilder.CreateIndex(
                name: "IX_FormVersions_OneOpen",
                table: "FormVersions",
                column: "ProgramId",
                unique: true,
                filter: "[Status] IN ('Draft', 'PendingApproval')");

            migrationBuilder.CreateIndex(
                name: "IX_FormVersions_ProgramId_Version",
                table: "FormVersions",
                columns: new[] { "ProgramId", "Version" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FormAnswers");

            migrationBuilder.DropTable(
                name: "FormQuestions");

            migrationBuilder.DropTable(
                name: "FormVersions");
        }
    }
}
