using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CashSessionOneOpenPerCompany : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM public.caixa
                        WHERE data_fechamento IS NULL AND empresa_id IS NOT NULL
                        GROUP BY empresa_id
                        HAVING COUNT(*) > 1
                    ) THEN
                        RAISE EXCEPTION 'Resolve multiple open cash sessions per company before applying this migration.';
                    END IF;
                END $$;
                """);

            migrationBuilder.CreateIndex(
                name: "caixa_one_open_per_company_idx",
                schema: "public",
                table: "caixa",
                column: "empresa_id",
                unique: true,
                filter: "data_fechamento IS NULL");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "caixa_one_open_per_company_idx",
                schema: "public",
                table: "caixa");
        }
    }
}
