# Modelo de datos de Materiales de Referencia

## Alcance y límite de evidencia

Este documento separa tres cosas: el esquema legado observado estáticamente, el esquema PostgreSQL propuesto para el flujo completo y las tablas que ya tienen entidad .NET en esta entrega.

No se consultó ni modificó la instancia productiva de InterDB y no se ejecutó SQL. El repositorio de TraceLab no contiene DDL ni migraciones MySQL; por eso los nombres de columnas legacy provienen del SQL embebido en Python y los tipos/constraints físicos de MySQL siguen pendientes de `SHOW CREATE TABLE`, `SHOW INDEX` y `SHOW TRIGGERS` en una copia autorizada. En InterDB sí se confirmó por el modelo existente que `usuarios.id` es `integer`; se reutiliza esa identidad y no se crea otra tabla de usuarios.

## Fase A — auditoría

### Tablas legacy confirmadas

El código de TraceLab usa estas tablas reales: `roles`, `usuarios`, `tabla_metodo`, `tabla_presentaciones`, `tabla_ubicaciones`, `tabla_estandares`, `tabla_stock`, `tabla_intermedia`, `tabla_intermedia_works`, `tabla_aqs`, `tabla_alertas`, `historial_compras`, `tabla_actividad` y `registro_envio_diario`.

La cadena operativa comprobada es:

```text
usuarios
   1
   |
   N
tabla_estandares
   1
   |
   N
tabla_stock
   1
   |
   N
tabla_intermedia
   1
   +---------- N tabla_intermedia_works  (Curvas)
   |
   +---------- N tabla_aqs
```

`tabla_intermedia`, `tabla_intermedia_works` y `tabla_aqs` guardan una fila por componente. El código de solución y los datos de cabecera se repiten entre filas; la relación de familia se reconstruye por código.

### Columnas legacy observadas

| Tabla | Columnas leídas/escritas por TraceLab |
|---|---|
| `tabla_estandares` | `idTabla_estandares`, `nombre_estandar`, `cas`, `numero_catalogo`, `metodo`, `pureza`, `lote`, `marca`, `fechaingreso`, `fechaexpiracion`, `presentacion`, `medida`, `unidades`, `porcentaje_faltante`, `concionesdealmacenamiento`, `fechadeingreso`, `ubicacion`, `estado`, `usuario` |
| `tabla_stock` | `idStock`, `tabla_estandares`, `peso_tomado`, `volumen_final`, `concentracion`, `fecha_preparacion`, `fecha_expiracion`, `grados`, `compuesto`, `usuario`, `peso_a_tomar_stock`, `fechadeingreso`, `estado` |
| `tabla_intermedia` | `idTabla_intermedia`, `codigo_solucion_intermedia`, `tabla_stock`, `volumen_tomado`, `volumen_aforo`, `concetrancion_final`, `fecha_preparacion`, `fecha_expira`, `usuario`, `id_intermedio_origen`, `fechadeingreso`, `estado` |
| `tabla_intermedia_works` | `idtabla_intermedia_works`, `tabla_intermedia`, `codigo_solucion`, `volumen_tomado_solucion`, `volumen_aforo`, `concentracion_final`, `fecha_preparacion`, `fecha_expiracion`, `grados`, `compuesto`, `tabla_usuario`, `id_intermedio_origen`, `fechadeingreso` |
| `tabla_aqs` | `idtabla_aqs`, `tabla_intermedia`, `codigo_solucion`, `volumen_tomado`, `volumen_aforo`, `concentracion_final`, `fecha_preparacion`, `fecha_expiracion`, `usuario`, `fecha_ingreso`, `estado` |

### Reglas y fórmulas confirmadas

- Total del estándar: `presentación por unidad × número de unidades`.
- Pureza válida: `0 < pureza <= 100`.
- Consumos de masa se normalizan a gramos; consumos de volumen a mililitros.
- Peso objetivo general: `(volumen_mL / 1000) × 100 / pureza`.
- Disperse Dyes aplica adicionalmente `/ 5`; la regla existe en el legacy, pero su justificación metrológica sigue pendiente de confirmación del laboratorio.
- Concentración de stock observada: `((masa_g × 1000) / (volumen_mL / 1000)) × (pureza / 100)`.
- Dilución de Intermedia, Curva y AQS: `Cfinal = Corigen × Vtomado / Vaforo`.
- Saldos: total de origen menos consumos acumulados; no se permite consumir más que el saldo.
- El legacy usa vigencias por defecto de 366 días para Estándar/Stock, 188 para Intermedia/Curva y 2 para AQS. Son valores observados, no parámetros metrológicos aprobados.

### Problemas del modelo anterior que no se copian

- No existe DDL versionado que demuestre todas las FKs, checks e índices.
- `autocommit=True` invalida la atomicidad de varias operaciones y permite lotes parciales.
- El saldo se calcula de formas diferentes entre pantallas; existe riesgo de sobreconsumo concurrente.
- Intermedia, Curva y AQS mezclan cabecera y componentes en la misma tabla.
- `porcentaje_faltante` duplica una verdad derivable y puede quedar desincronizado.
- El reemplazo automático CAS+catálogo puede reemplazar registros no relacionados cuando ambos identificadores están vacíos.
- `ubicacion` y `estado` mezclan condición operativa, archivo, Compras y Bodega.
- Curvas no validan consistentemente vencimiento, archivo ni saldo; Intermedias pueden quedar parcialmente guardadas.
- Los consumos de masa sufren una doble conversión en una ruta del código legacy.
- El audit trail guarda texto libre y no demuestra before/after ni identidad inmutable.

## Fase B — modelo PostgreSQL final propuesto

### Diagrama textual

```text
usuarios (reutilizada de InterDB)
   1 |------------------------------- N reference_materials
                                              1
                                              |
                                              N
                                      reference_stocks
                                              1
                                              |
                                              N
                            reference_intermediate_components
                                              N
                                              |
                                              1
                                  reference_intermediates
                                      1              1
                                      |              |
                                      N              N
                         reference_curve_components  reference_aqs_components
                                      N              N
                                      |              |
                                      1              1
                              reference_curves   reference_aqs

reference_materials N -- 0..1 reference_materials (replaced_by_material_id)
reference_intermediates N -- 0..1 reference_intermediates (source_intermediate_id)
```

Los componentes se separan de las cabeceras para que una preparación multicomponente sea una sola unidad transaccional, sin repetir código, fechas ni aforo.

Convención física de auditoría para cada cabecera de preparación: `created_by_user_id integer NOT NULL`, `created_at timestamptz NOT NULL`, `updated_by_user_id integer NOT NULL`, `updated_at timestamptz NOT NULL`, `archived_by_user_id integer NULL`, `archived_at timestamptz NULL`, `archive_reason varchar(500) NULL` y `version uuid NOT NULL`. Los tres IDs de actor son FKs a `usuarios(id)` con `RESTRICT`. Cada componente guarda `created_by_user_id integer NOT NULL` y `created_at timestamptz NOT NULL`.

### `usuarios` — reutilizada

Propósito: identidad y autorización existentes del LIMS. No se crea ni migra desde TraceLab.

| Columna usada | Tipo PostgreSQL | Nulabilidad | Regla |
|---|---|---|---|
| `id` | `integer` | NOT NULL | PK existente; destino de todos los actores |

Entidad .NET: `Lims.Domain.Identity.User`. Regla de borrado desde tablas de Materiales: `RESTRICT`.

### `reference_materials` — implementada

Origen legacy: `tabla_estandares`. Entidad .NET: `ReferenceMaterial`.

| Columna | Tipo | Null | Clave/regla |
|---|---|---:|---|
| `id` | `uuid` | NO | PK |
| `legacy_id` | `bigint` | SÍ | UNIQUE; ID MySQL durante migración |
| `name` | `varchar(200)` | NO | texto no vacío |
| `cas_number` | `varchar(80)` | SÍ | CAS opcional; checksum validado en dominio |
| `catalog_number` | `varchar(80)` | SÍ | identificador opcional |
| `method` | `varchar(120)` | NO | snapshot del método legacy; catálogo real pendiente de extracción |
| `purity_percent` | `numeric(7,4)` | NO | CHECK `> 0 AND <= 100` |
| `lot` | `varchar(120)` | NO | texto no vacío |
| `brand` | `varchar(120)` | NO | texto no vacío |
| `received_date` | `date` | NO |  |
| `expiration_date` | `date` | NO | CHECK `>= received_date` |
| `presentation_quantity` | `numeric(18,6)` | NO | CHECK `> 0` |
| `unit` | `varchar(24)` | NO | CHECK de unidad permitida |
| `package_count` | `integer` | NO | CHECK `> 0` |
| `available_quantity` | `numeric(18,6)` | NO | misma unidad del material; CHECK `0..presentación×unidades` |
| `storage_conditions` | `varchar(500)` | NO |  |
| `storage_location` | `varchar(160)` | NO | ubicación física, no estado de compra |
| `status` | `varchar(24)` | NO | CHECK de vocabulario |
| `created_by_user_id` | `integer` | NO | FK `usuarios(id)` RESTRICT |
| `created_at` | `timestamptz` | NO |  |
| `updated_by_user_id` | `integer` | NO | FK `usuarios(id)` RESTRICT |
| `updated_at` | `timestamptz` | NO |  |
| `archived_by_user_id` | `integer` | SÍ | FK `usuarios(id)` RESTRICT |
| `archived_at` | `timestamptz` | SÍ |  |
| `archive_reason` | `varchar(500)` | SÍ | motivo de archivo/reemplazo |
| `replaced_by_material_id` | `uuid` | SÍ | FK a la misma tabla RESTRICT |
| `version` | `uuid` | NO | concurrencia optimista |

Índices: nombre, método, estado, expiración, CAS+catálogo, reemplazo y las FKs de actor. No se impone UNIQUE CAS+catálogo porque el legacy admite identificadores vacíos y no define si el lote participa. El reemplazo es explícito y enlaza el registro sucesor.

`Expired` y `Depleted` se presentan como estados efectivos derivados cuando el registro operativo sigue `Active`, evitando depender de un job para impedir su uso por Stock.

### `reference_stocks` — arquitectura preparada

Origen legacy: `tabla_stock`. Entidad .NET prevista: `ReferenceStock`.

| Columna | Tipo | Null | Clave/regla |
|---|---|---:|---|
| `id` | `uuid` | NO | PK |
| `legacy_id` | `bigint` | SÍ | UNIQUE |
| `reference_material_id` | `uuid` | NO | FK `reference_materials(id)` RESTRICT |
| `source_quantity` | `numeric(18,6)` | NO | cantidad tomada en la unidad del estándar; CHECK `> 0` |
| `target_source_quantity` | `numeric(18,6)` | SÍ | peso objetivo legado; CHECK `> 0` si existe |
| `final_volume_ml` | `numeric(18,6)` | NO | CHECK `> 0` |
| `available_volume_ml` | `numeric(18,6)` | NO | CHECK `0..final_volume_ml` |
| `concentration_mg_l` | `numeric(24,9)` | NO | CHECK `> 0` |
| `preparation_date`, `expiration_date` | `date` | NO | expiración >= preparación |
| `temperature_c` | `numeric(8,3)` | SÍ | reemplaza texto libre `grados` |
| `compound` | `varchar(120)` | SÍ |  |
| `status` | `varchar(24)` | NO | vocabulario controlado |
| `created_by_user_id`, `updated_by_user_id` | `integer` | NO | FKs `usuarios(id)` RESTRICT |
| `created_at`, `updated_at` | `timestamptz` | NO | auditoría |
| `archived_by_user_id`, `archived_at`, `archive_reason` | `integer`, `timestamptz`, `varchar(500)` | SÍ | FK de actor RESTRICT y disposición lógica |
| `version` | `uuid` | NO | concurrencia optimista |

Índices: origen, estado, expiración y preparación. Crear Stock, descontar `reference_materials.available_quantity` y persistir Stock deben ocurrir en una transacción.

### `reference_intermediates` y `reference_intermediate_components` — arquitectura preparada

Origen legacy: filas agrupadas de `tabla_intermedia`. Entidades previstas: `ReferenceIntermediate` y `ReferenceIntermediateComponent`.

Cabecera: `id uuid PK`, `legacy_group_key varchar(160) NULL UNIQUE`, `code varchar(160) NOT NULL`, `final_volume_ml numeric(18,6) NOT NULL`, `available_volume_ml numeric(18,6) NOT NULL`, `preparation_date date NOT NULL`, `expiration_date date NOT NULL`, `source_intermediate_id uuid NULL`, `status varchar(24) NOT NULL` y las columnas de auditoría de cabecera. `source_intermediate_id` es FK autorreferente RESTRICT. La unicidad de códigos operativos se difiere hasta perfilar duplicados legacy.

Componente: `id uuid PK`, `intermediate_id uuid NOT NULL FK RESTRICT`, `stock_id uuid NOT NULL FK RESTRICT`, `sequence_no integer NOT NULL`, `volume_taken_ml numeric(18,6) NOT NULL`, `source_concentration_mg_l numeric(24,9) NOT NULL`, `result_concentration_mg_l numeric(24,9) NOT NULL`, `created_by_user_id integer NOT NULL` y `created_at timestamptz NOT NULL`. Cantidades/concentraciones son positivas; UNIQUE `(intermediate_id, sequence_no)`.

Crear la cabecera, insertar todos los componentes, descontar cada Stock y dejar la trazabilidad se ejecuta todo-o-nada.

### `reference_curves` y `reference_curve_components` — arquitectura preparada

Origen legacy: filas agrupadas de `tabla_intermedia_works`. Entidades previstas: `ReferenceCurve` y `ReferenceCurveComponent`.

Cabecera: `id uuid PK`, `legacy_group_key varchar(160) NULL UNIQUE`, `code varchar(160) NOT NULL`, `final_volume_ml numeric(18,6) NOT NULL`, `preparation_date date NOT NULL`, `expiration_date date NOT NULL`, `temperature_c numeric(8,3) NULL`, `compound varchar(120) NULL`, `status varchar(24) NOT NULL` y auditoría de cabecera. Componente: `id uuid PK`, `curve_id uuid NOT NULL`, `intermediate_id uuid NOT NULL`, `sequence_no integer NOT NULL`, `volume_taken_ml numeric(18,6) NOT NULL`, `source_concentration_mg_l numeric(24,9) NOT NULL`, `result_concentration_mg_l numeric(24,9) NOT NULL`, actor y timestamp. Todas las cantidades/concentraciones son positivas; UNIQUE `(curve_id, sequence_no)`; FKs `RESTRICT`.

### `reference_aqs` y `reference_aqs_components` — arquitectura preparada

Origen legacy: filas agrupadas de `tabla_aqs`. Entidades previstas: `ReferenceAqsPreparation` y `ReferenceAqsComponent`.

Cabecera: `id uuid PK`, `legacy_group_key varchar(160) NULL UNIQUE`, `code varchar(160) NOT NULL`, `final_volume_ml numeric(18,6) NOT NULL`, `preparation_date date NOT NULL`, `expiration_date date NOT NULL`, `status varchar(24) NOT NULL` y auditoría de cabecera. Componente: `id uuid PK`, `aqs_id uuid NOT NULL`, `intermediate_id uuid NOT NULL`, `sequence_no integer NOT NULL`, `volume_taken_ml numeric(18,6) NOT NULL`, `source_concentration_mg_l numeric(24,9) NOT NULL`, `result_concentration_mg_l numeric(24,9) NOT NULL`, actor y timestamp. UNIQUE `(aqs_id, sequence_no)`; FKs `RESTRICT`; cantidades y concentraciones positivas.

### Reglas de borrado

- No existe `DELETE` en los casos de uso operativos; se archiva o reemplaza.
- Todas las relaciones de trazabilidad y actores usan `ON DELETE RESTRICT`.
- No se usa `CASCADE` para descendencia química porque borraría evidencia.
- No se usa `SET NULL` para orígenes obligatorios. `source_intermediate_id` es nullable solo porque una intermedia normal no es copia; si existe, su FK también es `RESTRICT`.

### Constraints e índices transversales

- Cantidades, volúmenes y concentraciones estrictamente positivas donde representan una entrada.
- Saldos `>= 0` y `<=` cantidad/volumen inicial.
- Fechas de expiración no anteriores a ingreso/preparación.
- Estados y unidades limitados por CHECK.
- Sin preparaciones huérfanas: FKs NOT NULL desde cada componente a su origen y cabecera.
- Sin componentes duplicados por posición: UNIQUE `(cabecera_id, sequence_no)`.
- `legacy_id` o `legacy_group_key` permiten migración idempotente sin convertirlos en PK del dominio nuevo.
- Índices sobre todas las FKs, estado, expiración, código y campos principales de búsqueda.

## Fase C — SQL revisable

El DDL completo está en `artifacts/sql/materiales_referencia.sql`. No contiene `DROP`, `TRUNCATE` ni `DELETE`; no se ejecutó. `usuarios` se reutiliza. Las tablas futuras incluidas en el archivo son contrato de arquitectura y todavía no se agregan al `LimsDbContext` hasta implementar su vertical.

## Transacciones y ExecutionStrategy

Para Stock, Intermedia, Curva y AQS el caso de uso deberá ejecutar una unidad de trabajo que:

1. use `Database.CreateExecutionStrategy()`;
2. abra la transacción dentro de `strategy.ExecuteAsync(...)`;
3. bloquee o actualice condicionalmente el saldo del origen;
4. inserte cabecera y todos los componentes;
5. escriba actores/timestamps;
6. llame una vez a `SaveChangesAsync` y confirme;
7. revierta todo ante cualquier error.

El reemplazo de Estándar ya cambia el origen y crea el sucesor en un único `SaveChangesAsync`; no abre una transacción manual, por lo que la estrategia de reintento configurada por Npgsql puede reintentar la unidad atómica sin repetir una transacción de usuario externa.

## Plan de migración legacy (no ejecutado)

1. Extraer DDL y catálogos desde una copia MySQL autorizada.
2. Perfilar duplicados CAS/catálogo/lote, códigos repetidos, huérfanos, estados, unidades y saldos negativos.
3. Crear tablas de staging separadas; conservar cada `legacy_id`.
4. Migrar Estándares y resolver usuarios contra `usuarios`; los autores sin correspondencia requieren una identidad técnica aprobada, no IDs inventados.
5. Migrar Stock y validar que cada consumo no exceda la presentación normalizada.
6. Agrupar filas repetidas de Intermedia/Curva/AQS en cabecera+componentes y validar aforos/concentraciones.
7. Conciliar saldos reconstruidos contra los porcentajes/estados legacy; registrar excepciones.
8. Ejecutar conteos, FKs, checks y muestreo funcional en Testing.
9. Solicitar aprobación separada antes de cualquier aplicación a InterDB.
