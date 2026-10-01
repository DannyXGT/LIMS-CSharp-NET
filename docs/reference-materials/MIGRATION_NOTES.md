# Notas de esquema de Estándares

La migración EF `FinalizeReferenceMaterialCatalogs` crea y siembra `reference_methods`, `reference_units` y `reference_locations`, agrega sus FKs a `reference_materials`, incorpora `available_quantity`, reemplazo y el campo textual `storage_temperature`, y retira los campos transitorios de texto.

No se importan datos MySQL. Como protección, la migración falla si `reference_materials` contiene filas; no intenta transformar registros existentes ni continuar parcialmente. La migración inicial de Estándares constaba como no aplicada al preparar este cambio y ninguna migración fue ejecutada contra PostgreSQL en esta entrega.

El artefacto `artifacts/sql/reference_material_catalogs.sql` es idempotente y sirve para revisar los catálogos. No sustituye el historial EF ni fue ejecutado.
