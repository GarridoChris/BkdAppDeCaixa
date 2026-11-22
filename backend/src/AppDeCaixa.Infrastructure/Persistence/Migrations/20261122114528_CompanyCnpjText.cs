using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class CompanyCnpjText : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("ALTER TABLE public.empresa ALTER COLUMN cnpj TYPE text USING cnpj::text;");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DO $$
                BEGIN
                    IF EXISTS (
                        SELECT 1
                        FROM public.empresa
                        WHERE cnpj !~ '^[0-9]+$'
                    ) THEN
                        RAISE EXCEPTION 'Cannot convert CNPJ values back to integer because at least one value contains nonnumeric characters.';
                    END IF;

                    IF EXISTS (
                        SELECT 1
                        FROM public.empresa
                        WHERE length(cnpj) > 10
                           OR cnpj::numeric > 2147483647
                    ) THEN
                        RAISE EXCEPTION 'Cannot convert CNPJ values back to integer because at least one value is outside the integer range.';
                    END IF;
                END
                $$;
                """);

            migrationBuilder.Sql("ALTER TABLE public.empresa ALTER COLUMN cnpj TYPE integer USING cnpj::integer;");
        }
    }
}
