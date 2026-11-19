namespace Lims.Infrastructure.Planning;

public static class PlanningControlsSchema
{
    public const string Sql="""
        CREATE TABLE planning.job_controls (
          job text PRIMARY KEY CHECK(job=upper(trim(job)) AND length(job) BETWEEN 1 AND 200),
          on_hold boolean NOT NULL DEFAULT false, label text CHECK(length(label)<=80), hold_reason text,
          version bigint NOT NULL DEFAULT 1 CHECK(version>0),
          updated_at timestamptz NOT NULL DEFAULT now(), updated_by integer NOT NULL REFERENCES public.usuarios(id));
        CREATE INDEX planning_job_controls_hold ON planning.job_controls(on_hold,job);
        """;
}
