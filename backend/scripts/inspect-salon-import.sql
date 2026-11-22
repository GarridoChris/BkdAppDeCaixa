-- Run in pgAdmin once in postgres and once in organizaeh.
-- Read the Messages tab. The report contains schema information and counts only.
BEGIN READ ONLY;

DO $report$
DECLARE
    target_table text;
    table_id regclass;
    row_count bigint;
    migration_ids text;
    role_counts text;
    cnpj_type text;
BEGIN
    RAISE NOTICE 'Database: %', current_database();

    FOREACH target_table IN ARRAY ARRAY[
        'empresa', 'usuario', 'colaborador', 'servicos', 'forma_pagamento',
        'caixa', 'caixa_movimento', 'comanda', 'comanda_servico',
        'comanda_pagamento', 'pagamento_colaborador', 'pagamento_colaborador_item',
        'entrada'
    ]
    LOOP
        table_id := to_regclass(format('public.%I', target_table));

        IF table_id IS NULL THEN
            RAISE NOTICE '%: table absent', target_table;
        ELSE
            EXECUTE format('SELECT count(*) FROM %s', table_id) INTO row_count;
            RAISE NOTICE '%: % rows', target_table, row_count;
        END IF;
    END LOOP;

    IF to_regclass('public."__EFMigrationsHistory"') IS NULL THEN
        RAISE NOTICE 'EF migration history: table absent';
    ELSE
        SELECT string_agg("MigrationId", ', ' ORDER BY "MigrationId")
        INTO migration_ids
        FROM public."__EFMigrationsHistory";
        RAISE NOTICE 'EF migration history: %', coalesce(migration_ids, 'empty');
    END IF;

    IF to_regclass('public.caixa') IS NOT NULL THEN
        SELECT count(*) INTO row_count FROM public.caixa WHERE data_fechamento IS NULL;
        RAISE NOTICE 'Open cash sessions: %', row_count;
    END IF;

    IF to_regclass('public.usuario') IS NOT NULL THEN
        SELECT string_agg(format('%s=%s', role_name, total), ', ' ORDER BY role_name)
        INTO role_counts
        FROM (
            SELECT coalesce(to_jsonb(u) ->> 'role', '(no role)') AS role_name, count(*) AS total
            FROM public.usuario AS u
            GROUP BY 1
        ) AS roles;
        RAISE NOTICE 'Accounts by role: %', coalesce(role_counts, 'empty');
    END IF;

    IF to_regclass('public.empresa') IS NOT NULL THEN
        SELECT data_type INTO cnpj_type
        FROM information_schema.columns
        WHERE table_schema = 'public' AND table_name = 'empresa' AND column_name = 'cnpj';
        RAISE NOTICE 'Company CNPJ column type: %', coalesce(cnpj_type, 'absent');
    END IF;
END;
$report$;

COMMIT;
