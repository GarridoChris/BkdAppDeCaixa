using AppDeCaixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261101173000_DebtSettlementPayments")]
public partial class DebtSettlementPayments : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.AddColumn<int>(
            name: "caixa_id",
            schema: "public",
            table: "comanda_pagamento",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<bool>(
            name: "liquidacao_fiado",
            schema: "public",
            table: "comanda_pagamento",
            type: "boolean",
            nullable: false,
            defaultValue: false);

        migrationBuilder.Sql("""
            UPDATE public.comanda_pagamento AS payment
            SET caixa_id = order_record.caixa_id
            FROM public.comanda AS order_record
            WHERE order_record.comanda_id = payment.comanda_id
                AND payment.caixa_id IS NULL
                AND order_record.caixa_id IS NOT NULL;
            """);

        migrationBuilder.AddCheckConstraint(
            name: "comanda_pagamento_liquidacao_fiado_caixa_check",
            schema: "public",
            table: "comanda_pagamento",
            sql: "NOT liquidacao_fiado OR caixa_id IS NOT NULL");

        migrationBuilder.AddForeignKey(
            name: "comanda_pagamento_caixa_id_fkey",
            schema: "public",
            table: "comanda_pagamento",
            column: "caixa_id",
            principalSchema: "public",
            principalTable: "caixa",
            principalColumn: "caixa_id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateIndex(
            name: "comanda_pagamento_caixa_id_idx",
            schema: "public",
            table: "comanda_pagamento",
            column: "caixa_id");

        migrationBuilder.CreateIndex(
            name: "comanda_pagamento_comanda_id_liquidacao_fiado_idx",
            schema: "public",
            table: "comanda_pagamento",
            columns: new[] { "comanda_id", "liquidacao_fiado" });
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "comanda_pagamento_comanda_id_liquidacao_fiado_idx",
            schema: "public",
            table: "comanda_pagamento");

        migrationBuilder.DropIndex(
            name: "comanda_pagamento_caixa_id_idx",
            schema: "public",
            table: "comanda_pagamento");

        migrationBuilder.DropForeignKey(
            name: "comanda_pagamento_caixa_id_fkey",
            schema: "public",
            table: "comanda_pagamento");

        migrationBuilder.DropCheckConstraint(
            name: "comanda_pagamento_liquidacao_fiado_caixa_check",
            schema: "public",
            table: "comanda_pagamento");

        migrationBuilder.DropColumn(
            name: "caixa_id",
            schema: "public",
            table: "comanda_pagamento");

        migrationBuilder.DropColumn(
            name: "liquidacao_fiado",
            schema: "public",
            table: "comanda_pagamento");
    }
}
