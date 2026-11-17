-- The January archive predates this host dependency. This is only a local restoration prerequisite.
BEGIN;
DO $$ BEGIN
  IF current_database() <> 'lims_planning_dev' OR inet_server_port() <> 55441 OR inet_server_addr() <> '127.0.0.1'::inet THEN
    RAISE EXCEPTION 'Only the isolated restored development database is authorized';
  END IF;
END $$;
CREATE TABLE IF NOT EXISTS public.usuarios_permisos (
  id_usuario integer NOT NULL REFERENCES public.usuarios(id),
  permiso_key varchar(120) NOT NULL,
  PRIMARY KEY(id_usuario,permiso_key)
);
COMMIT;
