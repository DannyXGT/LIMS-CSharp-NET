# Notas de migración

## EF Core

`CreateReferenceMaterials` crea `reference_materials`. `CompleteReferenceMaterials` agrega saldo, ID legacy, vínculo de reemplazo, vocabularios, checks e índices. Ambas referencian `usuarios(id)` y no modifican autenticación ni identidad. Las migraciones fueron generadas, no aplicadas a ninguna base.

Antes de aplicar en un ambiente autorizado:

1. confirmar que la migración de autenticación previa ya está registrada;
2. revisar el SQL idempotente generado;
3. verificar que `public.usuarios.id` sea `integer`;
4. respaldar y probar restore;
5. aplicar primero en una base exclusiva de Testing;
6. ejecutar CRUD y concurrencia con usuarios que posean permisos explícitos.

## Trazalab / MySQL

No se migraron datos. La futura migración necesita `SHOW CREATE TABLE`, `SHOW INDEX`, `SHOW TRIGGERS` y datos reales para detectar duplicados, huérfanos, estados no canónicos y saldos inconsistentes. Los IDs y usuarios históricos deberán conservarse como metadata; las contraseñas del sistema antiguo no se migrarán.

La tabla nueva no impone unicidad CAS + catálogo porque lote y múltiples activos siguen siendo una decisión abierta.
