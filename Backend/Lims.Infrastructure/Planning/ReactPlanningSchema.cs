namespace Lims.Infrastructure.Planning;

/// <summary>Explicit, additive installation. Never executed by an import or API startup.</summary>
public static class ReactPlanningSchema
{
    public const string Sql="""
        CREATE TABLE IF NOT EXISTS planning.react_execution_links (
          execution_id uuid PRIMARY KEY REFERENCES planning.executions(id),
          react_row_id bigint REFERENCES public.planificacion_activa(id) ON DELETE SET NULL,
          disposition text NOT NULL, reason text NOT NULL, rule text,
          policy_version text NOT NULL, batch_id uuid REFERENCES planning.batches(id),
          updated_at timestamptz NOT NULL DEFAULT now());
        CREATE INDEX IF NOT EXISTS planning_react_disposition ON planning.react_execution_links(disposition,execution_id);
        CREATE INDEX IF NOT EXISTS planning_react_row ON planning.react_execution_links(react_row_id);
        ALTER TABLE planning.react_execution_links DROP CONSTRAINT IF EXISTS react_execution_links_react_row_id_fkey;
        ALTER TABLE planning.react_execution_links ADD CONSTRAINT react_execution_links_react_row_id_fkey
          FOREIGN KEY(react_row_id) REFERENCES public.planificacion_activa(id) ON DELETE SET NULL;
        CREATE TABLE IF NOT EXISTS public.planificacion_test_map_pendiente (
          id bigserial PRIMARY KEY,pending_key text NOT NULL UNIQUE,nombre_tecnico text NOT NULL,
          nombre_tecnico_norm text NOT NULL,row_payload jsonb NOT NULL,
          created_at timestamptz NOT NULL DEFAULT now(),updated_at timestamptz NOT NULL DEFAULT now());
        CREATE INDEX IF NOT EXISTS ix_planificacion_test_map_pendiente_tecnico
          ON public.planificacion_test_map_pendiente(nombre_tecnico_norm);
        ALTER TABLE public.planificacion_activa ADD COLUMN IF NOT EXISTS manual_pinned boolean NOT NULL DEFAULT false;
        ALTER TABLE planning.source_rows DROP CONSTRAINT IF EXISTS source_rows_file_check;
        ALTER TABLE planning.source_rows ADD CONSTRAINT source_rows_file_check CHECK(file IN ('Summary','Report List','Side2','React Active'));
        """;
}
