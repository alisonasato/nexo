-- Cria o papel que a aplicação usa, sem poder atravessar a RLS.
--
-- POR QUE ISTO EXISTE
--
-- O banco que um PaaS entrega vem com um papel administrativo — na Railway,
-- `postgres`, que é superusuário e tem BYPASSRLS. Superusuário ignora a
-- política de isolamento por completo, inclusive com FORCE ROW LEVEL SECURITY.
-- Nesse estado um escritório enxerga o dado do outro, e nada acusa.
--
-- COMO USAR
--
--   1. Gere uma senha. Não invente uma:
--        node -e "console.log(require('crypto').randomBytes(24).toString('base64url'))"
--
--   2. Rode conectado como o papel administrativo, passando a senha:
--        psql "URL_DO_BANCO" -v senha="'a-senha-gerada'" -f ferramentas/papel-da-aplicacao.sql
--
--   3. Troque a string de conexão da aplicação para usar nexo_app com essa senha.
--
--   Se o papel já existir, a criação falha e o resto não roda. Nesse caso use
--   ALTER ROLE nexo_app WITH PASSWORD '...' NOSUPERUSER NOBYPASSRLS; e rode de
--   novo comentando a linha do CREATE.
--
-- POR QUE TRANSFERIR A POSSE
--
-- As migrações rodam na subida, com a conexão da própria aplicação: ela precisa
-- poder criar e alterar tabelas. Ser dona resolve isso, e não abre brecha —
-- as migrações marcam as tabelas com FORCE ROW LEVEL SECURITY, que faz a
-- política valer inclusive para o dono. Foi assim que este projeto sempre
-- rodou em desenvolvimento.

\set ON_ERROR_STOP on

CREATE ROLE nexo_app LOGIN PASSWORD :senha NOSUPERUSER NOBYPASSRLS NOCREATEDB NOCREATEROLE;

GRANT USAGE, CREATE ON SCHEMA public TO nexo_app;

-- Posse das tabelas que já existem.
DO $$
DECLARE
    alvo record;
BEGIN
    FOR alvo IN SELECT tablename FROM pg_tables WHERE schemaname = 'public' LOOP
        EXECUTE format('ALTER TABLE public.%I OWNER TO nexo_app', alvo.tablename);
    END LOOP;
END
$$;

-- Sequências, se houver alguma.
DO $$
DECLARE
    alvo record;
BEGIN
    FOR alvo IN SELECT sequencename FROM pg_sequences WHERE schemaname = 'public' LOOP
        EXECUTE format('ALTER SEQUENCE public.%I OWNER TO nexo_app', alvo.sequencename);
    END LOOP;
END
$$;

-- Conferência. Os dois precisam ser `f`.
SELECT rolname, rolsuper, rolbypassrls FROM pg_roles WHERE rolname = 'nexo_app';
