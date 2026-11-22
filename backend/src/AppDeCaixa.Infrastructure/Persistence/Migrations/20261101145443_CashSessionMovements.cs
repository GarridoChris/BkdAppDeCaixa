using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashSessionMovements : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "caixa_movimento",
                schema: "public",
                columns: table => new
                {
                    caixa_movimento_id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.SerialColumn),
                    caixa_id = table.Column<int>(type: "integer", nullable: false),
                    tipo = table.Column<string>(type: "character varying(16)", nullable: false),
                    valor = table.Column<int>(type: "integer", nullable: false),
                    observacao = table.Column<string>(type: "text", nullable: false),
                    usuario_id = table.Column<int>(type: "integer", nullable: false),
                    data_hora = table.Column<DateTime>(type: "timestamp without time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("caixa_movimento_pkey", x => x.caixa_movimento_id);
                    table.CheckConstraint("caixa_movimento_observacao_check", "length(btrim(observacao)) BETWEEN 1 AND 500");
                    table.CheckConstraint("caixa_movimento_tipo_check", "tipo IN ('CashIn', 'CashOut')");
                    table.CheckConstraint("caixa_movimento_valor_check", "valor > 0");
                    table.ForeignKey(
                        name: "caixa_movimento_caixa_id_fkey",
                        column: x => x.caixa_id,
                        principalSchema: "public",
                        principalTable: "caixa",
                        principalColumn: "caixa_id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "caixa_movimento_usuario_id_fkey",
                        column: x => x.usuario_id,
                        principalSchema: "public",
                        principalTable: "usuario",
                        principalColumn: "usuario_id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "caixa_movimento_caixa_id_data_hora_idx",
                schema: "public",
                table: "caixa_movimento",
                columns: new[] { "caixa_id", "data_hora" });

            migrationBuilder.CreateIndex(
                name: "IX_caixa_movimento_usuario_id",
                schema: "public",
                table: "caixa_movimento",
                column: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "caixa_movimento",
                schema: "public");
        }
    }
}
