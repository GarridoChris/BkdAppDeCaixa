using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class OrderPaymentRecording : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "usuario_id_registro",
                schema: "public",
                table: "comanda_pagamento",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "observacao",
                schema: "public",
                table: "comanda_pagamento",
                type: "text",
                nullable: true);

            migrationBuilder.AddForeignKey(
                name: "comanda_pagamento_usuario_id_registro_fkey",
                schema: "public",
                table: "comanda_pagamento",
                column: "usuario_id_registro",
                principalSchema: "public",
                principalTable: "usuario",
                principalColumn: "usuario_id");

            migrationBuilder.Sql("""
                INSERT INTO public.forma_pagamento (descricao)
                SELECT method.descricao
                FROM (VALUES
                    ('Pix'),
                    ('Dinheiro'),
                    ('Cartão de débito'),
                    ('Cartão de crédito'),
                    ('Fiado')
                ) AS method(descricao)
                WHERE NOT EXISTS (
                    SELECT 1
                    FROM public.forma_pagamento AS existing
                    WHERE lower(btrim(existing.descricao)) = lower(btrim(method.descricao))
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "comanda_pagamento_usuario_id_registro_fkey",
                schema: "public",
                table: "comanda_pagamento");

            migrationBuilder.DropColumn(
                name: "observacao",
                schema: "public",
                table: "comanda_pagamento");

            migrationBuilder.DropColumn(
                name: "usuario_id_registro",
                schema: "public",
                table: "comanda_pagamento");
        }
    }
}
