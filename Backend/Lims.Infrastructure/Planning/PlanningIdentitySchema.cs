namespace Lims.Infrastructure.Planning;

public static class PlanningIdentitySchema
{
    // Upgrade existing shared-database installations without changing prior migrations.
    public const string Sql = """
        CREATE TABLE IF NOT EXISTS planning.actors (
          id integer PRIMARY KEY, nombre text NOT NULL);
        DO $identity$
        DECLARE item record;
        BEGIN
          IF to_regclass('public.usuarios') IS NOT NULL THEN
            INSERT INTO planning.actors(id,nombre)
            SELECT u.id,u.nombre FROM public.usuarios u
            WHERE u.id IN (SELECT actor_id FROM planning.batches UNION SELECT actor_id FROM planning.history
              UNION SELECT updated_by FROM planning.job_controls UNION SELECT actor_id FROM planning.review_decisions)
            ON CONFLICT(id) DO NOTHING;
          END IF;
          FOR item IN SELECT c.conname,c.conrelid::regclass AS relation FROM pg_constraint c
            JOIN pg_namespace n ON n.oid=c.connamespace
            WHERE n.nspname='planning' AND c.contype='f' AND c.confrelid=to_regclass('public.usuarios')
          LOOP
            EXECUTE format('ALTER TABLE %s DROP CONSTRAINT %I',item.relation,item.conname);
          END LOOP;
          FOR item IN SELECT * FROM (VALUES
            ('batches','actor_id'),('history','actor_id'),('job_controls','updated_by'),('review_decisions','actor_id')) t(tab,col)
          LOOP
            IF NOT EXISTS (SELECT 1 FROM pg_constraint c JOIN pg_attribute a ON a.attrelid=c.conrelid AND a.attnum=ANY(c.conkey)
              WHERE c.conrelid=to_regclass('planning.'||item.tab) AND c.contype='f'
                AND c.confrelid='planning.actors'::regclass AND a.attname=item.col) THEN
              EXECUTE format('ALTER TABLE planning.%I ADD CONSTRAINT %I FOREIGN KEY (%I) REFERENCES planning.actors(id)',
                item.tab,item.tab||'_'||item.col||'_actor_fkey',item.col);
            END IF;
          END LOOP;
        END
        $identity$;
        """;

    // Executed in the host database, in the same transaction as permission changes.
    public const string AccessSql = """
        CREATE TABLE IF NOT EXISTS public.planning_access_history (
          id uuid PRIMARY KEY, user_id integer NOT NULL REFERENCES public.usuarios(id),
          actor_id integer NOT NULL REFERENCES public.usuarios(id), at timestamptz NOT NULL DEFAULT now(),
          reason text NOT NULL, before_permissions text[] NOT NULL, after_permissions text[] NOT NULL);
        """;
}
