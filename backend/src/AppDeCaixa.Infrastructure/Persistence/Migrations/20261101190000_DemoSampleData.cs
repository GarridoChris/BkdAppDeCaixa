using AppDeCaixa.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AppDeCaixa.Infrastructure.Persistence.Migrations;

[DbContext(typeof(AppDbContext))]
[Migration("20261101190000_DemoSampleData")]
public partial class DemoSampleData : Migration
{
    protected override void Up(MigrationBuilder migrationBuilder)
    {
        // Demo financial records are opt-in so a production database never
        // receives fictional sales merely because all migrations were applied.
        if (!string.Equals(
                Environment.GetEnvironmentVariable("APPDECAIXA_SEED_DEMO"),
                "true",
                StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        migrationBuilder.Sql("""
            DO $$
            DECLARE
                demo_company_id integer;
                demo_manager_id integer;
                demo_cash_session_id integer;
                demo_collaborator_a_id integer;
                demo_collaborator_b_id integer;
                demo_service_a_id integer;
                demo_service_b_id integer;
                demo_service_c_id integer;
                demo_cash_order_id integer;
                demo_pix_order_id integer;
                demo_fiado_order_id integer;
                demo_cash_item_id integer;
                demo_pix_item_id integer;
                demo_fiado_item_id integer;
                demo_payout_id integer;
                cash_method_id integer;
                pix_method_id integer;
                fiado_method_id integer;
                demo_now timestamp without time zone := timezone('America/Bahia', CURRENT_TIMESTAMP);
            BEGIN
                SELECT company.empresa_id, manager.usuario_id
                INTO demo_company_id, demo_manager_id
                FROM public.empresa AS company
                INNER JOIN public.usuario AS manager
                    ON manager.empresa_id = company.empresa_id
                    AND manager.role = 'Manager'
                    AND manager.ativo = TRUE
                ORDER BY company.empresa_id, manager.usuario_id
                LIMIT 1;

                IF demo_company_id IS NULL OR demo_manager_id IS NULL THEN
                    RAISE NOTICE 'Skipping demo sample data: create a company and an active Manager before applying this migration to a development database.';
                    RETURN;
                END IF;

                SELECT MIN(forma_pagamento_id) FILTER (WHERE lower(btrim(descricao)) = 'dinheiro'),
                       MIN(forma_pagamento_id) FILTER (WHERE lower(btrim(descricao)) = 'pix'),
                       MIN(forma_pagamento_id) FILTER (WHERE lower(btrim(descricao)) = 'fiado')
                INTO cash_method_id, pix_method_id, fiado_method_id
                FROM public.forma_pagamento;

                IF cash_method_id IS NULL OR pix_method_id IS NULL OR fiado_method_id IS NULL THEN
                    RAISE EXCEPTION 'Demo sample data requires the Dinheiro, Pix, and Fiado payment methods.';
                END IF;

                INSERT INTO public.colaborador (empresa_id, nome, cpf, email, ativo)
                VALUES (demo_company_id, '[DEMO 20261101] Collaborator A', NULL, NULL, TRUE)
                RETURNING colaborador_id INTO demo_collaborator_a_id;

                INSERT INTO public.colaborador (empresa_id, nome, cpf, email, ativo)
                VALUES (demo_company_id, '[DEMO 20261101] Collaborator B', NULL, NULL, TRUE)
                RETURNING colaborador_id INTO demo_collaborator_b_id;

                INSERT INTO public.servicos (empresa_id, nome, descricao, porcentagem, valor, ativo)
                VALUES (demo_company_id, '[DEMO 20261101] Corte', 'Fictional service used for demo data.', 40, 80, TRUE)
                RETURNING servicos_id INTO demo_service_a_id;

                INSERT INTO public.servicos (empresa_id, nome, descricao, porcentagem, valor, ativo)
                VALUES (demo_company_id, '[DEMO 20261101] Tratamento', 'Fictional service used for demo data.', 50, 100, TRUE)
                RETURNING servicos_id INTO demo_service_b_id;

                INSERT INTO public.servicos (empresa_id, nome, descricao, porcentagem, valor, ativo)
                VALUES (demo_company_id, '[DEMO 20261101] Escova', 'Fictional service used for demo data.', 40, 120, TRUE)
                RETURNING servicos_id INTO demo_service_c_id;

                INSERT INTO public.caixa (
                    empresa_id, data_abertura, data_fechamento,
                    usuario_id_abertura, usuario_id_fechamento,
                    valor_abertura, valor_fechamento, valor_entradas, valor_saidas, status)
                VALUES (
                    demo_company_id, demo_now::date, demo_now::date,
                    demo_manager_id, demo_manager_id,
                    200, 290, 110, 20, 'Closed')
                RETURNING caixa_id INTO demo_cash_session_id;

                INSERT INTO public.comanda (
                    empresa_id, cliente_nome, data_hora_abertura, data_hora_fechamento,
                    status, valor_total, fiado, usuario_id_abertura, usuario_id_fechamento, caixa_id)
                VALUES (
                    demo_company_id, '[DEMO 20261101] Customer - cash', demo_now - interval '60 minutes',
                    demo_now - interval '50 minutes', 'Closed', 80, FALSE,
                    demo_manager_id, demo_manager_id, demo_cash_session_id)
                RETURNING comanda_id INTO demo_cash_order_id;

                INSERT INTO public.comanda (
                    empresa_id, cliente_nome, data_hora_abertura, data_hora_fechamento,
                    status, valor_total, fiado, usuario_id_abertura, usuario_id_fechamento, caixa_id)
                VALUES (
                    demo_company_id, '[DEMO 20261101] Customer - Pix', demo_now - interval '40 minutes',
                    demo_now - interval '30 minutes', 'Closed', 100, FALSE,
                    demo_manager_id, demo_manager_id, demo_cash_session_id)
                RETURNING comanda_id INTO demo_pix_order_id;

                INSERT INTO public.comanda (
                    empresa_id, cliente_nome, data_hora_abertura, data_hora_fechamento,
                    status, valor_total, fiado, usuario_id_abertura, usuario_id_fechamento, caixa_id)
                VALUES (
                    demo_company_id, '[DEMO 20261101] Customer - Fiado', demo_now - interval '25 minutes',
                    demo_now - interval '20 minutes', 'Closed', 120, TRUE,
                    demo_manager_id, demo_manager_id, demo_cash_session_id)
                RETURNING comanda_id INTO demo_fiado_order_id;

                INSERT INTO public.comanda_servico (
                    comanda_id, servicos_id, colaborador_id, valor_unitario,
                    porcentagem_colaborador, adicional, observacao, pago, valor_tabela,
                    motivo_ajuste, usuario_id_ajuste, data_hora_ajuste)
                VALUES (
                    demo_cash_order_id, demo_service_a_id, demo_collaborator_a_id, 80,
                    40, FALSE, '[DEMO 20261101] Sample cash service', FALSE, 80,
                    NULL, NULL, NULL)
                RETURNING comanda_servico_id INTO demo_cash_item_id;

                INSERT INTO public.comanda_servico (
                    comanda_id, servicos_id, colaborador_id, valor_unitario,
                    porcentagem_colaborador, adicional, observacao, pago, valor_tabela,
                    motivo_ajuste, usuario_id_ajuste, data_hora_ajuste)
                VALUES (
                    demo_pix_order_id, demo_service_b_id, demo_collaborator_b_id, 100,
                    50, FALSE, '[DEMO 20261101] Sample Pix service', TRUE, 100,
                    NULL, NULL, NULL)
                RETURNING comanda_servico_id INTO demo_pix_item_id;

                INSERT INTO public.comanda_servico (
                    comanda_id, servicos_id, colaborador_id, valor_unitario,
                    porcentagem_colaborador, adicional, observacao, pago, valor_tabela,
                    motivo_ajuste, usuario_id_ajuste, data_hora_ajuste)
                VALUES (
                    demo_fiado_order_id, demo_service_c_id, demo_collaborator_a_id, 120,
                    40, FALSE, '[DEMO 20261101] Sample Fiado service', FALSE, 120,
                    NULL, NULL, NULL)
                RETURNING comanda_servico_id INTO demo_fiado_item_id;

                INSERT INTO public.comanda_pagamento (
                    comanda_id, forma_pagamento_id, valor, data_pagamento,
                    usuario_id_registro, observacao, caixa_id, liquidacao_fiado)
                VALUES
                    (demo_cash_order_id, cash_method_id, 80, demo_now - interval '50 minutes',
                     demo_manager_id, '[DEMO 20261101] Sample payment', demo_cash_session_id, FALSE),
                    (demo_pix_order_id, pix_method_id, 100, demo_now - interval '30 minutes',
                     demo_manager_id, '[DEMO 20261101] Sample payment', demo_cash_session_id, FALSE),
                    (demo_fiado_order_id, fiado_method_id, 120, demo_now - interval '20 minutes',
                     demo_manager_id, '[DEMO 20261101] Sample Fiado authorization', demo_cash_session_id, FALSE),
                    (demo_fiado_order_id, cash_method_id, 30, demo_now - interval '10 minutes',
                     demo_manager_id, '[DEMO 20261101] Sample partial debt settlement', demo_cash_session_id, TRUE);

                INSERT INTO public.caixa_movimento (
                    caixa_id, tipo, valor, observacao, usuario_id, data_hora)
                VALUES (
                    demo_cash_session_id, 'CashOut', 20,
                    '[DEMO 20261101] Sample cash withdrawal', demo_manager_id, demo_now - interval '5 minutes');

                INSERT INTO public.pagamento_colaborador (
                    colaborador_id, data_pagamento, valor_total, forma_pagamento,
                    observacao, usuario_id_registro, data_hora_registro)
                VALUES (
                    demo_collaborator_b_id, demo_now::date, 50, 'Pix',
                    '[DEMO 20261101] Sample commission payout', demo_manager_id, demo_now - interval '25 minutes')
                RETURNING pagamento_id INTO demo_payout_id;

                INSERT INTO public.pagamento_colaborador_item (pagamento_id, comanda_servico_id, valor)
                VALUES (demo_payout_id, demo_pix_item_id, 50);
            END
            $$;
            """);
    }

    protected override void Down(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("""
            DO $$
            DECLARE
                demo_company_id integer;
                demo_cash_session_id integer;
            BEGIN
                SELECT order_record.empresa_id, order_record.caixa_id
                INTO demo_company_id, demo_cash_session_id
                FROM public.comanda AS order_record
                WHERE order_record.cliente_nome IN (
                    '[DEMO 20261101] Customer - cash',
                    '[DEMO 20261101] Customer - Pix',
                    '[DEMO 20261101] Customer - Fiado')
                    AND order_record.empresa_id IS NOT NULL
                ORDER BY order_record.comanda_id
                LIMIT 1;

                IF EXISTS (
                    SELECT 1
                    FROM public.comanda_pagamento AS payment
                    INNER JOIN public.comanda AS order_record
                        ON order_record.comanda_id = payment.comanda_id
                    WHERE order_record.cliente_nome IN (
                        '[DEMO 20261101] Customer - cash',
                        '[DEMO 20261101] Customer - Pix',
                        '[DEMO 20261101] Customer - Fiado')
                        AND order_record.empresa_id = demo_company_id
                        AND payment.observacao IS DISTINCT FROM '[DEMO 20261101] Sample payment'
                        AND payment.observacao IS DISTINCT FROM '[DEMO 20261101] Sample Fiado authorization'
                        AND payment.observacao IS DISTINCT FROM '[DEMO 20261101] Sample partial debt settlement'
                ) THEN
                    RAISE EXCEPTION 'Cannot roll back demo sample data because additional payment records were added to a demo order.';
                END IF;

                IF EXISTS (
                    SELECT 1
                    FROM public.pagamento_colaborador_item AS payout_item
                    INNER JOIN public.comanda_servico AS service_item
                        ON service_item.comanda_servico_id = payout_item.comanda_servico_id
                    INNER JOIN public.comanda AS order_record
                        ON order_record.comanda_id = service_item.comanda_id
                    INNER JOIN public.pagamento_colaborador AS payout
                        ON payout.pagamento_id = payout_item.pagamento_id
                    WHERE order_record.cliente_nome IN (
                        '[DEMO 20261101] Customer - cash',
                        '[DEMO 20261101] Customer - Pix',
                        '[DEMO 20261101] Customer - Fiado')
                        AND order_record.empresa_id = demo_company_id
                        AND payout.observacao IS DISTINCT FROM '[DEMO 20261101] Sample commission payout'
                ) THEN
                    RAISE EXCEPTION 'Cannot roll back demo sample data because a demo service item is included in another commission payout.';
                END IF;

                DELETE FROM public.pagamento_colaborador_item AS payout_item
                USING public.pagamento_colaborador AS payout
                WHERE payout_item.pagamento_id = payout.pagamento_id
                    AND payout.observacao = '[DEMO 20261101] Sample commission payout';

                DELETE FROM public.pagamento_colaborador
                WHERE observacao = '[DEMO 20261101] Sample commission payout';

                DELETE FROM public.comanda_pagamento AS payment
                USING public.comanda AS order_record
                WHERE payment.comanda_id = order_record.comanda_id
                    AND order_record.cliente_nome IN (
                        '[DEMO 20261101] Customer - cash',
                        '[DEMO 20261101] Customer - Pix',
                        '[DEMO 20261101] Customer - Fiado')
                    AND order_record.empresa_id = demo_company_id;

                DELETE FROM public.comanda_servico AS service_item
                USING public.comanda AS order_record
                WHERE service_item.comanda_id = order_record.comanda_id
                    AND order_record.cliente_nome IN (
                        '[DEMO 20261101] Customer - cash',
                        '[DEMO 20261101] Customer - Pix',
                        '[DEMO 20261101] Customer - Fiado')
                    AND order_record.empresa_id = demo_company_id;

                DELETE FROM public.comanda
                WHERE cliente_nome IN (
                    '[DEMO 20261101] Customer - cash',
                    '[DEMO 20261101] Customer - Pix',
                    '[DEMO 20261101] Customer - Fiado')
                    AND empresa_id = demo_company_id;

                DELETE FROM public.caixa_movimento
                WHERE caixa_id = demo_cash_session_id
                    AND observacao = '[DEMO 20261101] Sample cash withdrawal';

                DELETE FROM public.caixa AS session
                WHERE session.caixa_id = demo_cash_session_id
                    AND session.empresa_id = demo_company_id
                    AND NOT EXISTS (SELECT 1 FROM public.comanda WHERE caixa_id = session.caixa_id)
                    AND NOT EXISTS (SELECT 1 FROM public.comanda_pagamento WHERE caixa_id = session.caixa_id)
                    AND NOT EXISTS (SELECT 1 FROM public.caixa_movimento WHERE caixa_id = session.caixa_id);

                DELETE FROM public.servicos AS service
                WHERE service.nome IN (
                    '[DEMO 20261101] Corte',
                    '[DEMO 20261101] Tratamento',
                    '[DEMO 20261101] Escova')
                    AND service.empresa_id = demo_company_id
                    AND NOT EXISTS (
                        SELECT 1 FROM public.comanda_servico AS item
                        WHERE item.servicos_id = service.servicos_id);

                DELETE FROM public.colaborador AS collaborator
                WHERE collaborator.nome IN (
                    '[DEMO 20261101] Collaborator A',
                    '[DEMO 20261101] Collaborator B')
                    AND collaborator.empresa_id = demo_company_id
                    AND NOT EXISTS (
                        SELECT 1 FROM public.comanda_servico AS item
                        WHERE item.colaborador_id = collaborator.colaborador_id)
                    AND NOT EXISTS (
                        SELECT 1 FROM public.pagamento_colaborador AS payout
                        WHERE payout.colaborador_id = collaborator.colaborador_id)
                    AND NOT EXISTS (
                        SELECT 1 FROM public.usuario AS user_record
                        WHERE user_record.colaborador_id = collaborator.colaborador_id);
            END
            $$;
            """);
    }
}
