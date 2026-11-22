using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class AddServiceCatalogName : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "nome",
                schema: "public",
                table: "servicos",
                type: "text",
                nullable: true);

            migrationBuilder.Sql("UPDATE public.servicos SET nome = descricao WHERE nome IS NULL;");

            migrationBuilder.AlterColumn<string>(
                name: "nome",
                schema: "public",
                table: "servicos",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text",
                oldNullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "nome",
                schema: "public",
                table: "servicos");
        }
    }
}
