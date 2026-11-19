namespace Lims.Infrastructure.Planning;

public static class PlanningReviewSchema
{
    public const string Sql="""
        CREATE TABLE planning.review_decisions(
          case_key text PRIMARY KEY,category text NOT NULL,episode_id uuid REFERENCES planning.episodes(id),
          state text NOT NULL CHECK(state IN ('Pending','Held','Resolved','Excluded')),
          reason text NOT NULL,version bigint NOT NULL DEFAULT 0,actor_id integer NOT NULL REFERENCES public.usuarios(id),
          decided_at timestamptz NOT NULL DEFAULT now());
        ALTER TABLE planning.incidents DROP CONSTRAINT incidents_state_check;
        ALTER TABLE planning.incidents ADD CONSTRAINT incidents_state_check CHECK(state IN ('Pending','Held','Resolved','Excluded'));
        CREATE INDEX planning_sources_batch_episode ON planning.source_rows(batch_id,episode_id);
        UPDATE planning.revision SET value=value+1 WHERE singleton;
        """;
}
