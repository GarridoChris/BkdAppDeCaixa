using AppDeCaixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261101160000_CommissionPayoutIntegrity")]
public partial class CommissionPayoutIntegrity : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            BEGIN
                IF EXISTS (
                    SELECT comanda_servico_id
                    FROM public.pagamento_colaborador_item
                    WHERE comanda_servico_id IS NOT NULL
                    GROUP BY comanda_servico_id
                    HAVING COUNT(*) > 1
                ) THEN
                    RAISE EXCEPTION 'Cannot add commission payout protection: a service item already appears in multiple collaborator payouts.';
                END IF;
            END
            $$;
            """);

        migrationBuilder.AddColumn<int>(
            name: "usuario_id_registro",
            schema: "public",
            table: "pagamento_colaborador",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<DateTime>(
            name: "data_hora_registro",
            schema: "public",
            table: "pagamento_colaborador",
            type: "timestamp without time zone",
            nullable: true);

        migrationBuilder.AddForeignKey(
            name: "pagamento_colaborador_usuario_id_registro_fkey",
            schema: "public",
            table: "pagamento_colaborador",
            column: "usuario_id_registro",
            principalSchema: "public",
            principalTable: "usuario",
            principalColumn: "usuario_id",
            onDelete: ReferentialAction.Restrict);

        migrationBuilder.CreateIndex(
            name: "pagamento_colaborador_item_comanda_servico_id_uidx",
            schema: "public",
            table: "pagamento_colaborador_item",
            column: "comanda_servico_id",
            unique: true);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(
            name: "pagamento_colaborador_item_comanda_servico_id_uidx",
            schema: "public",
            table: "pagamento_colaborador_item");

        migrationBuilder.DropForeignKey(
            name: "pagamento_colaborador_usuario_id_registro_fkey",
            schema: "public",
            table: "pagamento_colaborador");

        migrationBuilder.DropColumn(
            name: "data_hora_registro",
            schema: "public",
            table: "pagamento_colaborador");

        migrationBuilder.DropColumn(
            name: "usuario_id_registro",
            schema: "public",
            table: "pagamento_colaborador");
    }
}
