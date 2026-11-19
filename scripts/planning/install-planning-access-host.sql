-- Ejecutar en InterDB. Auditoria atomica de permisos; no modifica cuentas ni claves.
BEGIN;
SET LOCAL lock_timeout = '5s';
DO $host$
BEGIN
  IF current_database() <> 'InterDB' THEN
    RAISE EXCEPTION 'Este bloque se ejecuta en InterDB, no en %.', current_database();
  END IF;
END
$host$;
CREATE TABLE IF NOT EXISTS public.planning_access_history (
  id uuid PRIMARY KEY, user_id integer NOT NULL REFERENCES public.usuarios(id),
  actor_id integer NOT NULL REFERENCES public.usuarios(id), at timestamptz NOT NULL DEFAULT now(),
  reason text NOT NULL, before_permissions text[] NOT NULL, after_permissions text[] NOT NULL);
COMMIT;
