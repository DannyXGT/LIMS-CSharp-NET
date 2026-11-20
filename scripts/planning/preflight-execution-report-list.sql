-- Read-only checks. Connect to the server's configured Planning database.
BEGIN READ ONLY;
SELECT current_database() AS database_name,inet_server_addr() AS server_address,inet_server_port() AS server_port;
SELECT n.nspname AS schema_name,c.relname AS relation_name,c.relkind
FROM pg_class c JOIN pg_namespace n ON n.oid=c.relnamespace
WHERE n.nspname='planning' AND c.relname IN
 ('episodes','executions','actors','report_checks','report_blocks','report_close_alerts','report_scan_links')
ORDER BY c.relname;
SELECT table_schema,table_name,column_name,data_type
FROM information_schema.columns WHERE table_schema='planning' AND table_name IN ('episodes','executions','report_checks')
ORDER BY table_name,ordinal_position;
SELECT "MigrationId","ProductVersion" FROM public."__EFMigrationsHistory"
WHERE "MigrationId" LIKE '%Planning%' OR "MigrationId" LIKE '%ReportList%' ORDER BY "MigrationId";
ROLLBACK;
