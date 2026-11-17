namespace Lims.Infrastructure.Planning;

public static class PlanningNotesSchema
{
    public const string Sql="""
        ALTER TABLE planning.executions ADD COLUMN responsible text CHECK(length(responsible)<=150),
          ADD COLUMN observation text CHECK(length(observation)<=2000),
          ADD COLUMN note_version bigint NOT NULL DEFAULT 0;
        """;
}
