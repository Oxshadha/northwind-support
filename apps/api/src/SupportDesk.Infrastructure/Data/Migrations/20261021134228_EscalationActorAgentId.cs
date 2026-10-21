using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SupportDesk.Infrastructure.Data.Migrations
{
    /// <inheritdoc />
    public partial class EscalationActorAgentId : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "EscalatedByAgentId",
                table: "TicketEscalations",
                type: "int",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_TicketEscalations_EscalatedByAgentId",
                table: "TicketEscalations",
                column: "EscalatedByAgentId");

            migrationBuilder.AddForeignKey(
                name: "FK_TicketEscalations_Agents_EscalatedByAgentId",
                table: "TicketEscalations",
                column: "EscalatedByAgentId",
                principalTable: "Agents",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_TicketEscalations_Agents_EscalatedByAgentId",
                table: "TicketEscalations");

            migrationBuilder.DropIndex(
                name: "IX_TicketEscalations_EscalatedByAgentId",
                table: "TicketEscalations");

            migrationBuilder.DropColumn(
                name: "EscalatedByAgentId",
                table: "TicketEscalations");
        }
    }
}
