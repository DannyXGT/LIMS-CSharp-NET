namespace Lims.Infrastructure.Planning;

public static class ReportClosureSchema
{
    public const string Sql="""
        CREATE TABLE planning.report_closure_followups (
          job text NOT NULL,fingerprint text NOT NULL,snapshot jsonb NOT NULL,
          state text NOT NULL CHECK(state IN ('Pending','Seen','Archived')),
          responsible text,notes text,follow_up_on date,version bigint NOT NULL DEFAULT 1,
          updated_by integer NOT NULL REFERENCES planning.actors(id),updated_at timestamptz NOT NULL DEFAULT clock_timestamp(),
          PRIMARY KEY(job,fingerprint));
        CREATE INDEX report_closure_followups_date ON planning.report_closure_followups(updated_at DESC,job,fingerprint);
        CREATE TABLE planning.report_closure_requests (
          id uuid PRIMARY KEY,actor_id integer NOT NULL REFERENCES planning.actors(id),request_hash text NOT NULL,result jsonb NOT NULL);
        """;
}
