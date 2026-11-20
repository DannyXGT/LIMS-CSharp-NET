-- Additive equivalent of 20261119180000_AddExecutionReportList.
-- Apply only to the authorized Planning database after SeparatePlanningIdentity.
BEGIN;
SET LOCAL lock_timeout='5s';
DO $migration$
BEGIN
  IF to_regclass('planning.actors') IS NULL THEN
    RAISE EXCEPTION 'Apply SeparatePlanningIdentity before Report List';
  END IF;
  IF NOT EXISTS(SELECT 1 FROM public."__EFMigrationsHistory" WHERE "MigrationId"='20261119180000_AddExecutionReportList') THEN
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
    INSERT INTO public."__EFMigrationsHistory"("MigrationId","ProductVersion") VALUES('20261119180000_AddExecutionReportList','10.0.10');
  END IF;
END $migration$;
COMMIT;
