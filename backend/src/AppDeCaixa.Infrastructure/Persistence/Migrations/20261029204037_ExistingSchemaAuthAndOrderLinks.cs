using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations;

public partial class ExistingSchemaAuthAndOrderLinks : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Bootstrap the tables the API needs on a new database. Existing
        // databases may still have the unused legacy entrada table.
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                required_tables constant text[] := ARRAY[
                    'caixa', 'colaborador', 'comanda', 'comanda_pagamento',
                    'comanda_servico', 'empresa', 'forma_pagamento',
                    'pagamento_colaborador', 'pagamento_colaborador_item',
                    'servicos', 'usuario'
                ];
                missing_tables text[];
            BEGIN
                SELECT COALESCE(array_agg(table_name ORDER BY table_name), ARRAY[]::text[])
                INTO missing_tables
                FROM unnest(required_tables) AS tables(table_name)
                WHERE to_regclass('public.' || quote_ident(table_name)) IS NULL;

                IF cardinality(missing_tables) = cardinality(required_tables) THEN
                    CREATE TABLE public.empresa (
                        empresa_id serial CONSTRAINT empresa_pkey PRIMARY KEY,
                        nome text NOT NULL,
                        cnpj integer NOT NULL,
                        ponto_focal text NOT NULL,
                        CONSTRAINT empresa_cnpj_key UNIQUE (cnpj)
                    );

                    CREATE TABLE public.usuario (
                        usuario_id serial CONSTRAINT usuario_pkey PRIMARY KEY,
                        empresa_id integer,
                        nome character varying NOT NULL,
                        cpf character varying,
                        email character varying,
                        senha_hash character varying NOT NULL,
                        ultimo_login timestamp without time zone,
                        ultimo_logout timestamp without time zone,
                        ativo boolean,
                        CONSTRAINT usuario_empresa_id_fkey
                            FOREIGN KEY (empresa_id) REFERENCES public.empresa (empresa_id),
                        CONSTRAINT usuario_cpf_key UNIQUE (cpf),
                        CONSTRAINT usuario_email_key UNIQUE (email)
                    );

                    CREATE TABLE public.colaborador (
                        colaborador_id serial CONSTRAINT colaborador_pkey PRIMARY KEY,
                        empresa_id integer,
                        nome character varying NOT NULL,
                        cpf character varying,
                        email character varying,
                        ativo boolean,
                        CONSTRAINT colaborador_empresa_id_fkey
                            FOREIGN KEY (empresa_id) REFERENCES public.empresa (empresa_id),
                        CONSTRAINT colaborador_cpf_key UNIQUE (cpf)
                    );

                    CREATE TABLE public.caixa (
                        caixa_id serial CONSTRAINT caixa_pkey PRIMARY KEY,
                        empresa_id integer,
                        data_abertura date NOT NULL,
                        data_fechamento date,
                        usuario_id_abertura integer,
                        usuario_id_fechamento integer,
                        valor_abertura integer NOT NULL,
                        valor_fechamento integer,
                        valor_entradas integer,
                        valor_saidas integer,
                        status character varying,
                        CONSTRAINT caixa_empresa_id_fkey
                            FOREIGN KEY (empresa_id) REFERENCES public.empresa (empresa_id),
                        CONSTRAINT caixa_usuario_id_abertura_fkey
                            FOREIGN KEY (usuario_id_abertura) REFERENCES public.usuario (usuario_id),
                        CONSTRAINT caixa_usuario_id_fechamento_fkey
                            FOREIGN KEY (usuario_id_fechamento) REFERENCES public.usuario (usuario_id)
                    );

                    CREATE TABLE public.comanda (
                        comanda_id serial CONSTRAINT comanda_pkey PRIMARY KEY,
                        empresa_id integer,
                        data_hora_abertura timestamp without time zone NOT NULL,
                        data_hora_fechamento timestamp without time zone,
                        status character varying,
                        valor_total integer,
                        fiado boolean,
                        usuario_id_abertura integer,
                        usuario_id_fechamento integer,
                        CONSTRAINT comanda_empresa_id_fkey
                            FOREIGN KEY (empresa_id) REFERENCES public.empresa (empresa_id),
                        CONSTRAINT comanda_usuario_id_abertura_fkey
                            FOREIGN KEY (usuario_id_abertura) REFERENCES public.usuario (usuario_id),
                        CONSTRAINT comanda_usuario_id_fechamento_fkey
                            FOREIGN KEY (usuario_id_fechamento) REFERENCES public.usuario (usuario_id)
                    );

                    CREATE TABLE public.forma_pagamento (
                        forma_pagamento_id serial CONSTRAINT forma_pagamento_pkey PRIMARY KEY,
                        descricao character varying NOT NULL
                    );

                    CREATE TABLE public.comanda_pagamento (
                        comanda_pagamento_id serial CONSTRAINT comanda_pagamento_pkey PRIMARY KEY,
                        comanda_id integer,
                        forma_pagamento_id integer,
                        valor integer NOT NULL,
                        data_pagamento timestamp without time zone,
                        CONSTRAINT comanda_pagamento_comanda_id_fkey
                            FOREIGN KEY (comanda_id) REFERENCES public.comanda (comanda_id),
                        CONSTRAINT comanda_pagamento_forma_pagamento_id_fkey
                            FOREIGN KEY (forma_pagamento_id) REFERENCES public.forma_pagamento (forma_pagamento_id)
                    );

                    CREATE TABLE public.servicos (
                        servicos_id serial CONSTRAINT servicos_pkey PRIMARY KEY,
                        empresa_id integer,
                        descricao text NOT NULL,
                        porcentagem integer NOT NULL,
                        valor integer NOT NULL,
                        ativo boolean,
                        CONSTRAINT servicos_empresa_id_fkey
                            FOREIGN KEY (empresa_id) REFERENCES public.empresa (empresa_id)
                    );

                    CREATE TABLE public.comanda_servico (
                        comanda_servico_id serial CONSTRAINT comanda_servico_pkey PRIMARY KEY,
                        comanda_id integer,
                        servicos_id integer,
                        colaborador_id integer,
                        valor_unitario integer NOT NULL,
                        porcentagem_colaborador integer NOT NULL,
                        adicional boolean,
                        observacao text,
                        pago boolean,
                        CONSTRAINT comanda_servico_comanda_id_fkey
                            FOREIGN KEY (comanda_id) REFERENCES public.comanda (comanda_id),
                        CONSTRAINT comanda_servico_servicos_id_fkey
                            FOREIGN KEY (servicos_id) REFERENCES public.servicos (servicos_id),
                        CONSTRAINT comanda_servico_colaborador_id_fkey
                            FOREIGN KEY (colaborador_id) REFERENCES public.colaborador (colaborador_id)
                    );

                    CREATE TABLE public.pagamento_colaborador (
                        pagamento_id serial CONSTRAINT pagamento_colaborador_pkey PRIMARY KEY,
                        colaborador_id integer,
                        data_pagamento date NOT NULL,
                        valor_total integer NOT NULL,
                        forma_pagamento character varying,
                        observacao text,
                        CONSTRAINT pagamento_colaborador_colaborador_id_fkey
                            FOREIGN KEY (colaborador_id) REFERENCES public.colaborador (colaborador_id)
                    );

                    CREATE TABLE public.pagamento_colaborador_item (
                        item_id serial CONSTRAINT pagamento_colaborador_item_pkey PRIMARY KEY,
                        pagamento_id integer,
                        comanda_servico_id integer,
                        valor integer NOT NULL,
                        CONSTRAINT pagamento_colaborador_item_pagamento_id_fkey
                            FOREIGN KEY (pagamento_id) REFERENCES public.pagamento_colaborador (pagamento_id),
                        CONSTRAINT pagamento_colaborador_item_comanda_servico_id_fkey
                            FOREIGN KEY (comanda_servico_id) REFERENCES public.comanda_servico (comanda_servico_id)
                    );

                ELSIF cardinality(missing_tables) > 0 THEN
                    RAISE EXCEPTION 'The database contains a partial salon schema. Missing required tables: %. Restore the missing tables or use an empty database.',
                        array_to_string(missing_tables, ', ');
                ELSIF EXISTS (
                    SELECT 1
                    FROM information_schema.columns
                    WHERE table_schema = 'public'
                      AND ((table_name = 'usuario' AND column_name IN ('role', 'colaborador_id'))
                           OR (table_name = 'comanda' AND column_name = 'caixa_id'))
                ) THEN
                    RAISE EXCEPTION 'The salon schema already has columns from the initial EF migration, but that migration is not recorded. Restore a backup that includes the matching EF migration history, or migrate an empty database before importing data.';
                END IF;
            END
            $$;
            """);

        migrationBuilder.AddColumn<int>(
            name: "colaborador_id",
            schema: "public",
            table: "usuario",
            type: "integer",
            nullable: true);

        migrationBuilder.AddColumn<string>(
            name: "role",
            schema: "public",
            table: "usuario",
            type: "character varying(16)",
            maxLength: 16,
            nullable: true);

        migrationBuilder.AddColumn<int>(
            name: "caixa_id",
            schema: "public",
            table: "comanda",
            type: "integer",
            nullable: true);

        migrationBuilder.AddCheckConstraint(
            name: "usuario_role_check",
            schema: "public",
            table: "usuario",
            sql: "role IS NULL OR role IN ('Manager', 'Cashier', 'Employee')");

        migrationBuilder.AddForeignKey(
            name: "usuario_colaborador_id_fkey",
            schema: "public",
            table: "usuario",
            column: "colaborador_id",
            principalSchema: "public",
            principalTable: "colaborador",
            principalColumn: "colaborador_id");

        migrationBuilder.AddForeignKey(
            name: "comanda_caixa_id_fkey",
            schema: "public",
            table: "comanda",
            column: "caixa_id",
            principalSchema: "public",
            principalTable: "caixa",
            principalColumn: "caixa_id");

        migrationBuilder.CreateIndex(
            name: "IX_usuario_colaborador_id",
            schema: "public",
            table: "usuario",
            column: "colaborador_id");

        migrationBuilder.CreateIndex(
            name: "IX_comanda_caixa_id",
            schema: "public",
            table: "comanda",
            column: "caixa_id");
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.DropIndex(name: "IX_usuario_colaborador_id", schema: "public", table: "usuario");
        migrationBuilder.DropIndex(name: "IX_comanda_caixa_id", schema: "public", table: "comanda");
        migrationBuilder.DropForeignKey(name: "usuario_colaborador_id_fkey", schema: "public", table: "usuario");
        migrationBuilder.DropForeignKey(name: "comanda_caixa_id_fkey", schema: "public", table: "comanda");
        migrationBuilder.DropCheckConstraint(name: "usuario_role_check", schema: "public", table: "usuario");
        migrationBuilder.DropColumn(name: "colaborador_id", schema: "public", table: "usuario");
        migrationBuilder.DropColumn(name: "role", schema: "public", table: "usuario");
        migrationBuilder.DropColumn(name: "caixa_id", schema: "public", table: "comanda");
    }
}
