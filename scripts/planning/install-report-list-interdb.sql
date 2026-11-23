-- MODIFICA ESQUEMA: las dos migraciones oficiales de Report List.
-- Destino: InterDB. Respaldar antes. No modifica JOBs ni digitaciones existentes.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
SET LOCAL search_path = public, pg_catalog;
DO $preflight$
DECLARE missing text;
BEGIN
    IF current_database() <> 'InterDB' THEN
        RAISE EXCEPTION 'Base incorrecta: %. Seleccione InterDB.', current_database();
    END IF;
    IF to_regclass('planning.actors') IS NULL
       OR to_regclass('planning.episodes') IS NULL
       OR to_regclass('planning.executions') IS NULL
       OR to_regclass('public."__EFMigrationsHistory"') IS NULL THEN
        RAISE EXCEPTION 'Falta la instalacion base de Planning o su historial EF.';
    END IF;
    IF NOT EXISTS (SELECT 1 FROM public."__EFMigrationsHistory"
                   WHERE "MigrationId"='20261119120000_SeparatePlanningIdentity') THEN
        RAISE EXCEPTION 'Falta la migracion previa SeparatePlanningIdentity. Revisar antes de instalar.';
    END IF;
    SELECT string_agg(name, ', ') INTO missing FROM unnest(ARRAY[
        'planning.report_checks','planning.report_blocks','planning.report_close_alerts',
        'planning.report_scan_links','planning.report_sessions',
        'planning.report_session_members','planning.report_schedule_status']) AS t(name)
    WHERE to_regclass(name) IS NOT NULL;
    IF missing IS NOT NULL THEN
        RAISE EXCEPTION 'Ya existen objetos de Report List: %. No repetir sobre una instalacion parcial o completa.', missing;
    END IF;
    IF EXISTS (SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" IN
        ('20261119180000_AddExecutionReportList','20261120120000_AddReportScheduleReview')) THEN
        RAISE EXCEPTION 'El historial registra Report List pero faltan sus tablas. Requiere conciliacion, no marcar migraciones manualmente.';
    END IF;
END $preflight$;
CREATE TABLE planning.report_checks (
  execution_id uuid PRIMARY KEY REFERENCES planning.executions(id),
  keep_in_planning boolean NOT NULL DEFAULT false,
  block_number bigint,
  actor_id integer REFERENCES planning.actors(id),checked_at timestamptz);
CREATE TABLE planning.report_blocks (
  number bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
  actor_id integer NOT NULL REFERENCES planning.actors(id),last_checked_at timestamptz NOT NULL);
CREATE TABLE planning.report_close_alerts (
  episode_id uuid PRIMARY KEY REFERENCES planning.episodes(id),
  execution_id uuid NOT NULL REFERENCES planning.executions(id),
  block_number bigint,actor_id integer NOT NULL REFERENCES planning.actors(id),
  marked_at timestamptz NOT NULL DEFAULT now(),reopened_at timestamptz);
CREATE TABLE planning.report_scan_links (
  tracking_id bigint PRIMARY KEY CHECK(tracking_id>0),
  execution_id uuid NOT NULL REFERENCES planning.executions(id),
  actor_id integer NOT NULL REFERENCES planning.actors(id),
  reason text NOT NULL,created_at timestamptz NOT NULL DEFAULT now());

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261119180000_AddExecutionReportList', '10.0.10');

CREATE TABLE planning.report_sessions (
  id uuid PRIMARY KEY, actor_id integer NOT NULL REFERENCES planning.actors(id),
  operational_at timestamptz NOT NULL, saved_at timestamptz NOT NULL DEFAULT clock_timestamp(),
  request_hash text NOT NULL, version bigint NOT NULL DEFAULT 0,
  reviewed_by integer REFERENCES planning.actors(id),reviewed_at timestamptz);
CREATE TABLE planning.report_session_members (
  session_id uuid NOT NULL REFERENCES planning.report_sessions(id),
  execution_id uuid NOT NULL REFERENCES planning.executions(id),
  digitization_version bigint NOT NULL,PRIMARY KEY(session_id,execution_id));
CREATE INDEX report_sessions_clock ON planning.report_sessions(operational_at DESC,id);
CREATE INDEX report_members_execution ON planning.report_session_members(execution_id,session_id);
CREATE VIEW planning.report_schedule_status AS
  SELECT s.id,s.actor_id,s.operational_at,s.saved_at,s.version,s.reviewed_by,s.reviewed_at,
    count(*)::int AS tests,count(DISTINCT e.job)::int AS jobs,
    count(*) FILTER(WHERE x.version<>m.digitization_version
      OR x.digitized_at IS DISTINCT FROM s.operational_at
      OR x.tested_on IS DISTINCT FROM (s.operational_at AT TIME ZONE 'America/Guatemala')::date)::int AS exceptions
  FROM planning.report_sessions s JOIN planning.report_session_members m ON m.session_id=s.id
  JOIN planning.executions x ON x.id=m.execution_id JOIN planning.episodes e ON e.id=x.episode_id
  GROUP BY s.id;

INSERT INTO "__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261120120000_AddReportScheduleReview', '10.0.10');


COMMIT;
SELECT current_database() AS base, nombre,
       CASE WHEN to_regclass(nombre) IS NULL THEN 'FALTA' ELSE 'EXISTE' END AS estado
FROM (VALUES ('planning.report_checks'),('planning.report_blocks'),
    ('planning.report_close_alerts'),('planning.report_scan_links'),
    ('planning.report_sessions'),('planning.report_session_members'),
    ('planning.report_schedule_status')) AS tablas(nombre);