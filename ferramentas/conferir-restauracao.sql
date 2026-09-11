-- Confere um banco restaurado a partir de backup.
--
-- POR QUE ISTO EXISTE
--
-- Restauração que perde dado avisa: a tela fica vazia. Restauração que perde
-- `FORCE ROW LEVEL SECURITY` não avisa nada — as políticas continuam todas lá,
-- a aplicação sobe, as telas funcionam, e um escritório passa a enxergar o dado
-- do outro. Acontece sempre que o dump foi tirado com o FORCE desligado, que é
-- a saída que alguém tenta quando o `pg_dump` recusa rodar com o papel da
-- aplicação. Ver BACKUP.md.
--
-- COMO USAR
--
--   psql "URL_DO_BANCO_RESTAURADO" -f ferramentas/conferir-restauracao.sql
--
-- Termina com RESTAURACAO OK, ou lista o que está faltando.

\set ON_ERROR_STOP on

DO $$
DECLARE
    tabelas   int;
    politicas int;
    forcadas  int;
    indice    int;
    faltas    text[] := '{}';
BEGIN
    SELECT count(*) INTO tabelas
      FROM pg_tables WHERE schemaname = 'public';

    SELECT count(*) INTO politicas
      FROM pg_policies WHERE schemaname = 'public';

    SELECT count(*) INTO forcadas
      FROM pg_class c
      JOIN pg_namespace n ON n.oid = c.relnamespace
     WHERE n.nspname = 'public' AND c.relforcerowsecurity;

    SELECT count(*) INTO indice
      FROM pg_indexes
     WHERE schemaname = 'public'
       AND indexname LIKE 'ix_recebiveis_tenant_id_contrato_id_competencia%'
       AND indexdef LIKE '%WHERE (situacao <> 3)%';

    IF tabelas <> 14 THEN
        faltas := faltas || format('tabelas: esperava 14, achei %s', tabelas);
    END IF;

    IF politicas <> 5 THEN
        faltas := faltas || format('políticas de RLS: esperava 5, achei %s', politicas);
    END IF;

    -- A que realmente importa, e a única que some sem sintoma.
    IF forcadas <> 5 THEN
        faltas := faltas || format(
            'FORCE ROW LEVEL SECURITY: esperava 5 tabelas, achei %s. '
            'O isolamento entre escritórios NÃO está valendo neste banco — '
            'o dump foi tirado com o FORCE desligado. Ver BACKUP.md.', forcadas);
    END IF;

    IF indice <> 1 THEN
        faltas := faltas ||
            'índice único parcial dos recebíveis (WHERE situacao <> 3): não encontrado. '
            'Sem ele a mesma mensalidade pode ser cobrada duas vezes.';
    END IF;

    IF array_length(faltas, 1) IS NULL THEN
        RAISE NOTICE 'RESTAURACAO OK: 14 tabelas, 5 políticas, 5 com FORCE, índice parcial no lugar.';
    ELSE
        RAISE EXCEPTION E'RESTAURACAO INCOMPLETA:\n  - %', array_to_string(faltas, E'\n  - ');
    END IF;
END
$$;

-- O conserto do FORCE, se for só isso que faltou. Rode depois de conferir que
-- as políticas estão todas lá.
--
--   ALTER TABLE public.empresas    FORCE ROW LEVEL SECURITY;
--   ALTER TABLE public.pessoas     FORCE ROW LEVEL SECURITY;
--   ALTER TABLE public.clientes    FORCE ROW LEVEL SECURITY;
--   ALTER TABLE public.contratos   FORCE ROW LEVEL SECURITY;
--   ALTER TABLE public.recebiveis  FORCE ROW LEVEL SECURITY;
