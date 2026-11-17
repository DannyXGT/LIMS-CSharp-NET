namespace Lims.Infrastructure.Planning;

public static class PlanningSchema
{
    public const string Sql = """
        CREATE SCHEMA planning;
        CREATE TABLE planning.revision (singleton boolean PRIMARY KEY DEFAULT true CHECK(singleton), value bigint NOT NULL DEFAULT 0);
        INSERT INTO planning.revision DEFAULT VALUES;
        CREATE TABLE planning.batches (
          id uuid PRIMARY KEY, fingerprint text NOT NULL UNIQUE, status text NOT NULL CHECK(status IN ('Preview','Applied')),
          actor_id integer NOT NULL REFERENCES public.usuarios(id), base_revision bigint NOT NULL,
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
          actor_id integer NOT NULL REFERENCES public.usuarios(id), at timestamptz NOT NULL DEFAULT now(),
          reason text NOT NULL, details jsonb NOT NULL);
        CREATE INDEX planning_history_episode ON planning.history(episode_id,at DESC,id);
        """;
}
