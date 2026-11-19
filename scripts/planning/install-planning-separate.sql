-- Centro de Importaciones: instalacion inicial de las seis migraciones de Planning.
-- Abrir en pgAdmin conectado a planificacion_migracion_test y ejecutar TODO el archivo.
-- Requiere PostgreSQL >= 15. Login y permisos permanecen en InterDB.
-- Si Planning ya contiene objetos o historial, se detiene sin sobrescribirlos.
-- Instalador consolidado: estado final equivalente a las seis migraciones, incluida identidad separada.
BEGIN;
SET LOCAL lock_timeout = '5s';
SET LOCAL statement_timeout = '60s';
SET LOCAL search_path = public, pg_catalog;

DO $preflight$
BEGIN
    IF current_database() <> 'planificacion_migracion_test' THEN
        RAISE EXCEPTION 'Base incorrecta: %. Seleccione planificacion_migracion_test.', current_database();
    END IF;
    IF current_setting('server_version_num')::integer < 150000 THEN
        RAISE EXCEPTION 'Se requiere PostgreSQL 15 o posterior (UNIQUE NULLS NOT DISTINCT).';
    END IF;
    PERFORM pg_advisory_xact_lock(18619876, 20261119);

    IF EXISTS (
        SELECT 1 FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
        WHERE n.nspname = 'planning'
    ) THEN
        RAISE EXCEPTION 'Planning ya contiene objetos. Este instalador inicial no altera instalaciones parciales o existentes.';
    END IF;
    IF to_regclass('public."__EFMigrationsHistory"') IS NOT NULL THEN
        IF EXISTS (SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId" LIKE '%Planning%') THEN
            RAISE EXCEPTION 'Ya existe historial Planning. Revisar el esquema antes de continuar.';
        END IF;
    END IF;
END
$preflight$;

CREATE TABLE IF NOT EXISTS public."__EFMigrationsHistory" (
    "MigrationId" character varying(150) NOT NULL,
    "ProductVersion" character varying(32) NOT NULL,
    CONSTRAINT "PK___EFMigrationsHistory" PRIMARY KEY ("MigrationId")
);

-- 20261116190000_AddPlanningWorkspace
CREATE SCHEMA IF NOT EXISTS planning;
-- Identidades de auditoria verificadas por la API; no es una tabla de login.
CREATE TABLE planning.actors (id integer PRIMARY KEY, nombre text NOT NULL);
CREATE TABLE planning.revision (singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton), value bigint NOT NULL DEFAULT 0);
INSERT INTO planning.revision DEFAULT VALUES;
CREATE TABLE planning.batches (
  id uuid PRIMARY KEY, fingerprint text NOT NULL UNIQUE, status text NOT NULL CHECK(status IN ('Preview','Applied')),
  actor_id integer NOT NULL REFERENCES planning.actors(id), base_revision bigint NOT NULL,
  cut_window text NOT NULL CHECK(cut_window IN ('First','Noon','Last')), day date NOT NULL,
  created_at timestamptz NOT NULL DEFAULT now(), applied_at timestamptz,
  summary_name text NOT NULL, report_name text NOT NULL, summary_sha256 text NOT NULL, report_sha256 text NOT NULL,
  summary_data bytea NOT NULL, report_data bytea NOT NULL, input jsonb NOT NULL, plan jsonb NOT NULL, preview jsonb NOT NULL);
CREATE TABLE planning.episodes (
  id uuid PRIMARY KEY, job text NOT NULL CHECK(length(job) BETWEEN 1 AND 200), source_in_date date,
  buyer text, vendor text, service text, in_date date, due_date date,
  amount numeric(18,4) CHECK(amount>=0), currency text, usd numeric(18,4) CHECK(usd>=0),
  manual_fields text[] NOT NULL DEFAULT '{}', version bigint NOT NULL DEFAULT 0,
  source_batch_id uuid REFERENCES planning.batches(id), source_row integer,
  UNIQUE NULLS NOT DISTINCT(job,source_in_date));
CREATE INDEX planning_episodes_due ON planning.episodes(due_date,id);
CREATE INDEX planning_episodes_job ON planning.episodes(job,id);
CREATE TABLE planning.executions (
  id uuid PRIMARY KEY, episode_id uuid NOT NULL REFERENCES planning.episodes(id), original_test text NOT NULL,
  test_key text NOT NULL, equivalence text, laboratory text, no_program boolean NOT NULL DEFAULT false,
  origin text NOT NULL CHECK(origin IN ('Manual','ReportList')), execution_type text,
  digitized_at timestamptz, digitized_by text, tested_on date, version bigint NOT NULL DEFAULT 0);
CREATE INDEX planning_executions_episode ON planning.executions(episode_id,test_key,id);
CREATE TABLE planning.source_rows (
  batch_id uuid NOT NULL REFERENCES planning.batches(id), file text NOT NULL CHECK(file IN ('Summary','Report List')),
  row_number integer NOT NULL, sheet text NOT NULL, job text NOT NULL, original_test text,
  fields jsonb NOT NULL, wip jsonb, episode_id uuid REFERENCES planning.episodes(id), execution_id uuid REFERENCES planning.executions(id),
  PRIMARY KEY(batch_id,file,row_number));
CREATE INDEX planning_source_episode ON planning.source_rows(episode_id,batch_id,file);
CREATE TABLE planning.equivalences (
  test_key text PRIMARY KEY, original_test text NOT NULL, equivalence text, laboratory text,
  no_program boolean NOT NULL, version bigint NOT NULL DEFAULT 0,
  CHECK((no_program AND equivalence IS NULL AND laboratory IS NULL) OR
        (NOT no_program AND length(trim(equivalence))>0 AND length(trim(laboratory))>0)));
CREATE TABLE planning.incidents (
  id uuid PRIMARY KEY, episode_id uuid NOT NULL REFERENCES planning.episodes(id), execution_id uuid REFERENCES planning.executions(id),
  kind text NOT NULL, state text NOT NULL CHECK(state IN ('Pending','Held','Resolved')), reason text, version bigint NOT NULL DEFAULT 0,
  UNIQUE NULLS NOT DISTINCT(episode_id,kind,execution_id));
CREATE INDEX planning_incidents_state ON planning.incidents(state,kind,episode_id);
CREATE TABLE planning.history (
  id uuid PRIMARY KEY, episode_id uuid REFERENCES planning.episodes(id), action text NOT NULL,
  actor_id integer NOT NULL REFERENCES planning.actors(id), at timestamptz NOT NULL DEFAULT now(),
  reason text NOT NULL, details jsonb NOT NULL);
CREATE INDEX planning_history_episode ON planning.history(episode_id,at DESC,id);

-- 20261116220000_AddPlanningExecutionNotes
ALTER TABLE planning.executions ADD COLUMN responsible text CHECK(length(responsible)<=150),
  ADD COLUMN observation text CHECK(length(observation)<=2000),
  ADD COLUMN note_version bigint NOT NULL DEFAULT 0;

-- 20261117220000_AddPlanningJobControls
CREATE TABLE planning.job_controls (
  job text PRIMARY KEY CHECK(job=upper(trim(job)) AND length(job) BETWEEN 1 AND 200),
  on_hold boolean NOT NULL DEFAULT false, label text CHECK(length(label)<=80), hold_reason text,
  version bigint NOT NULL DEFAULT 1 CHECK(version>0),
  updated_at timestamptz NOT NULL DEFAULT now(), updated_by integer NOT NULL REFERENCES planning.actors(id));
CREATE INDEX planning_job_controls_hold ON planning.job_controls(on_hold,job);

-- 20261118010000_AddPlanningOfficialState
ALTER TABLE planning.episodes ADD COLUMN official_fields jsonb NOT NULL DEFAULT '{}';
ALTER TABLE planning.incidents ADD COLUMN manual_resolved boolean NOT NULL DEFAULT false;
CREATE INDEX planning_episodes_sample_status ON planning.episodes((official_fields->>'sampleStatus'));
UPDATE planning.revision SET value=value+1 WHERE singleton;

-- 20261118180000_AddPlanningReviewDecisions
CREATE TABLE planning.review_decisions(
  case_key text PRIMARY KEY,category text NOT NULL,episode_id uuid REFERENCES planning.episodes(id),
  state text NOT NULL CHECK(state IN ('Pending','Held','Resolved','Excluded')),
  reason text NOT NULL,version bigint NOT NULL DEFAULT 0,actor_id integer NOT NULL REFERENCES planning.actors(id),
  decided_at timestamptz NOT NULL DEFAULT now());
ALTER TABLE planning.incidents DROP CONSTRAINT incidents_state_check;
ALTER TABLE planning.incidents ADD CONSTRAINT incidents_state_check CHECK(state IN ('Pending','Held','Resolved','Excluded'));
CREATE INDEX planning_sources_batch_episode ON planning.source_rows(batch_id,episode_id);
UPDATE planning.revision SET value=value+1 WHERE singleton;

-- Verificacion dentro de la misma transaccion, antes de registrar las migraciones.
DO $verify$
DECLARE missing text;
BEGIN
    SELECT string_agg(name, ', ' ORDER BY name) INTO missing
    FROM unnest(ARRAY['actors','revision','batches','episodes','executions','source_rows',
                     'equivalences','incidents','history','job_controls','review_decisions']) AS t(name)
    WHERE to_regclass(format('planning.%I', name)) IS NULL;
    IF missing IS NOT NULL THEN RAISE EXCEPTION 'Faltan tablas: %', missing; END IF;
    IF (SELECT count(*) FROM planning.revision WHERE singleton AND value = 2) <> 1 THEN
        RAISE EXCEPTION 'No se completo la revision inicial de Planning.';
    END IF;
    IF (SELECT count(*) FROM information_schema.columns WHERE table_schema = 'planning' AND
        ((table_name = 'executions' AND column_name IN ('responsible','observation','note_version')) OR
         (table_name = 'episodes' AND column_name = 'official_fields') OR
         (table_name = 'incidents' AND column_name = 'manual_resolved'))) <> 5 THEN
        RAISE EXCEPTION 'Faltan columnas de las migraciones de Planning.';
    END IF;
END
$verify$;

INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion") VALUES
('20261116190000_AddPlanningWorkspace', '10.0.10'),
('20261116220000_AddPlanningExecutionNotes', '10.0.10'),
('20261117220000_AddPlanningJobControls', '10.0.10'),
('20261118010000_AddPlanningOfficialState', '10.0.10'),
('20261118180000_AddPlanningReviewDecisions', '10.0.10'),
('20261119120000_SeparatePlanningIdentity', '10.0.10');

COMMIT;

-- Resultado esperado: las seis migraciones, ya confirmadas.
SELECT current_database() AS base_actual, "MigrationId", "ProductVersion"
FROM public."__EFMigrationsHistory"
WHERE "MigrationId" IN (
 '20261116190000_AddPlanningWorkspace', '20261116220000_AddPlanningExecutionNotes',
 '20261117220000_AddPlanningJobControls', '20261118010000_AddPlanningOfficialState',
 '20261118180000_AddPlanningReviewDecisions', '20261119120000_SeparatePlanningIdentity')
ORDER BY "MigrationId";