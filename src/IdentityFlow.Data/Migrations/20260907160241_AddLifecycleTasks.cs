using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace IdentityFlow.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddLifecycleTasks : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "LifecycleTaskExecutions",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    LifecycleTaskId = table.Column<int>(type: "int", nullable: false),
                    EmployeeCode = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    TaskType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    ExecutedAt = table.Column<DateTimeOffset>(type: "datetimeoffset", nullable: false),
                    Success = table.Column<bool>(type: "bit", nullable: false),
                    Detail = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifecycleTaskExecutions", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "LifecycleTasks",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Trigger = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    TaskType = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    DayOffset = table.Column<int>(type: "int", nullable: false),
                    Enabled = table.Column<bool>(type: "bit", nullable: false),
                    Notes = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_LifecycleTasks", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_LifecycleTaskExecutions_LifecycleTaskId_EmployeeCode",
                table: "LifecycleTaskExecutions",
                columns: new[] { "LifecycleTaskId", "EmployeeCode" });

            migrationBuilder.CreateIndex(
                name: "IX_LifecycleTasks_Trigger_TaskType",
                table: "LifecycleTasks",
                columns: new[] { "Trigger", "TaskType" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "LifecycleTaskExecutions");

            migrationBuilder.DropTable(
                name: "LifecycleTasks");
        }
    }
}
