using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class ServiceOrderPriceAdjustments : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "valor_tabela",
                schema: "public",
                table: "comanda_servico",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "motivo_ajuste",
                schema: "public",
                table: "comanda_servico",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "usuario_id_ajuste",
                schema: "public",
                table: "comanda_servico",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "data_hora_ajuste",
                schema: "public",
                table: "comanda_servico",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.Sql("UPDATE public.comanda_servico SET valor_tabela = valor_unitario WHERE valor_tabela IS NULL;");

            migrationBuilder.AlterColumn<int>(
                name: "valor_tabela",
                schema: "public",
                table: "comanda_servico",
                type: "integer",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer",
                oldNullable: true);

            migrationBuilder.AddForeignKey(
                name: "comanda_servico_usuario_id_ajuste_fkey",
                schema: "public",
                table: "comanda_servico",
                column: "usuario_id_ajuste",
                principalSchema: "public",
                principalTable: "usuario",
                principalColumn: "usuario_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "comanda_servico_usuario_id_ajuste_fkey",
                schema: "public",
                table: "comanda_servico");

            migrationBuilder.DropColumn(
                name: "data_hora_ajuste",
                schema: "public",
                table: "comanda_servico");

            migrationBuilder.DropColumn(
                name: "motivo_ajuste",
                schema: "public",
                table: "comanda_servico");

            migrationBuilder.DropColumn(
                name: "usuario_id_ajuste",
                schema: "public",
                table: "comanda_servico");

            migrationBuilder.DropColumn(
                name: "valor_tabela",
                schema: "public",
                table: "comanda_servico");
        }
    }
}
