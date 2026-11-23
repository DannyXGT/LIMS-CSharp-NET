namespace Lims.Infrastructure.Planning;

public static class ReportScheduleSchema
{
    public const string Sql="""
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
        """;
}
