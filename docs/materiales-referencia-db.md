# Modelo de datos de Estándares

## Alcance

Trazalab se usa únicamente como referencia funcional para identificar campos, relaciones y catálogos. No se importan estándares, existencias, preparaciones, alertas, compras, inventario ni usuarios históricos. El modelo no contiene `legacy_id`, excepciones de autoría legacy ni `porcentaje_faltante`.

## Confirmación de ubicación

El código PySide6 de Trazalab confirma que el editor de la columna Ubicación consulta `tabla_ubicaciones` (`SELECT * FROM tabla_ubicaciones ORDER BY 1`) y llena un `QComboBox`. El formulario de alta asigna inicialmente `Laboratorio`.

El dump contiene `Laboratorio`, `Bodega` y `Proceso de Compra`. En el nuevo LIMS sólo `Laboratorio` y `Bodega` son ubicaciones físicas. `Proceso de Compra` es workflow, no se incluye en `reference_locations` y no se implementa en esta fase.

## Catálogos

### `reference_methods`

| Columna | Tipo | Regla |
|---|---|---|
| `id` | `integer identity` | PK |
| `name` | `varchar(120)` | NOT NULL, texto recortado/no vacío, UNIQUE sobre `lower(name)` |
| `is_active` | `boolean` | NOT NULL |

Seeds: los 23 nombres literales de `tabla_metodo`: Azodyes, APEOs, Disperse dyes, Phtalatos, PCP, OPP, Organotin, PAHs, SCCP/MCCP, Retardantes de Flama, DMFA/DMFU, VOC, AEEA, Bisphenol, Halogenated, Tiourea, PFC, COC, Cresoles, UV, Glicoles, Metales y AMB.

### `reference_units`

| Columna | Tipo | Regla |
|---|---|---|
| `id` | `integer identity` | PK |
| `name` | `varchar(80)` | NOT NULL, texto recortado/no vacío, UNIQUE sobre `lower(name)` |
| `symbol` | `varchar(16)` | NOT NULL, texto recortado/no vacío, UNIQUE sobre `lower(symbol)` |
| `is_active` | `boolean` | NOT NULL |

Seeds activos: `mL`, `g`, `mg`, `µg`, recuperados de `tabla_presentaciones` y normalizando únicamente `ml` a `mL`.

### `reference_locations`

| Columna | Tipo | Regla |
|---|---|---|
| `id` | `integer identity` | PK |
| `name` | `varchar(120)` | NOT NULL, texto recortado/no vacío, UNIQUE sobre `lower(name)` |
| `is_active` | `boolean` | NOT NULL |

Seeds: `Laboratorio`, `Bodega`.

No existe `reference_storage_temperatures`.

## `reference_materials`

| Columna | Tipo | Null | Regla |
|---|---|---:|---|
| `id` | `uuid` | NO | PK |
| `name` | `varchar(200)` | NO | texto no vacío |
| `cas_number` | `varchar(80)` | SÍ | CAS opcional validado en dominio |
| `catalog_number` | `varchar(80)` | SÍ | identificador opcional |
| `method_id` | `integer` | NO | FK `reference_methods(id)` RESTRICT |
| `purity_percent` | `numeric(7,4)` | NO | `> 0 AND <= 100` |
| `lot` | `varchar(120)` | NO | texto no vacío |
| `brand` | `varchar(120)` | NO | texto no vacío |
| `received_date` | `date` | NO | fecha calendario |
| `expiration_date` | `date` | NO | `>= received_date` |
| `presentation_quantity` | `numeric(18,6)` | NO | `> 0` |
| `unit_id` | `integer` | NO | FK `reference_units(id)` RESTRICT |
| `package_count` | `integer` | NO | `> 0` |
| `available_quantity` | `numeric(18,6)` | NO | entre 0 y presentación × envases |
| `location_id` | `integer` | NO | FK `reference_locations(id)` RESTRICT |
| `storage_temperature` | `varchar(160)` | NO | texto directo, por ejemplo `4 °C`, `-20 °C`, `T ambiente`, `2-8 °C` |
| `status` | `varchar(24)` | NO | vocabulario controlado |
| `created_by_user_id` | `integer` | NO | FK `usuarios(id)` RESTRICT |
| `updated_by_user_id` | `integer` | NO | FK `usuarios(id)` RESTRICT |
| `created_at`, `updated_at` | `timestamptz` | NO | auditoría |
| `archived_by_user_id`, `archived_at`, `archive_reason` | varios | SÍ | archivado lógico |
| `replaced_by_material_id` | `uuid` | SÍ | FK autorreferente RESTRICT |
| `version` | `uuid` | NO | concurrencia optimista |

La cantidad inicial es `presentation_quantity * package_count`. `available_quantity` guarda el saldo en la unidad elegida. El porcentaje restante se deriva y no se persiste.

## SQL y migración

- `artifacts/sql/reference_material_catalogs.sql`: creación y seeds idempotentes de los tres catálogos.
- `20261001225215_FinalizeReferenceMaterialCatalogs`: migración EF del modelo nuevo. Se detiene si encuentra filas en `reference_materials`; deliberadamente no importa ni transforma registros operativos.

La migración no fue aplicada a PostgreSQL.
