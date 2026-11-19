namespace Lims.Infrastructure.Planning;

public static class PlanningOfficialSchema
{
    public const string Sql="""
        ALTER TABLE planning.episodes ADD COLUMN official_fields jsonb NOT NULL DEFAULT '{}';
        ALTER TABLE planning.incidents ADD COLUMN manual_resolved boolean NOT NULL DEFAULT false;
        CREATE INDEX planning_episodes_sample_status ON planning.episodes((official_fields->>'sampleStatus'));
        UPDATE planning.revision SET value=value+1 WHERE singleton;
        """;
}
