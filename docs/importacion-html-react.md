# Importación: HTML de Consulta de Jobs y tablas LIMS React

## Contrato operativo

La importación conserva fuente e identidades; la admisión decide qué aparece en trabajo activo. La secuencia es HTML primero, mejoras React después y persistencia sobre las tablas compartidas. El host, sesión, permisos y cliente WinUI existentes se conservan.

- `Summary` aporta Buyer, Vendor, servicio, ingreso, vencimiento, importe, moneda, tasa y estados oficiales. Report List aporta las pruebas. Cada celda original queda archivada.
- `Close Date`, `Report Closed` e `Invoice Generated` conservan el registro como histórico y excluyen nuevas admisiones operativas.
- Un guion en el JOB se trata como revisión/reingreso por conciliar, siguiendo el HTML. La carga complementaria o la asignación manual son acciones explícitas y auditadas.
- La fecha de ingreso objetivo puede seleccionarse; vacía utiliza la más reciente del WIP disponible. Un cambio de fecha objetivo no retira una ejecución ya admitida.
- La falta de equivalencia produce una ejecución trazable fuera del trabajo activo y una fila en la cola React. Clasificarla reprocesa el lote persistido sin requerir nuevamente el archivo.
- NO PROGRAMAR, subcontrato y laboratorio fuera de la lista del HTML quedan identificados fuera del trabajo activo. Falta de Summary y fechas incompletas se conservan para revisión.
- La ausencia de una prueba en otra carga no borra ni archiva por sí sola registros anteriores. Los archivados manuales se respetan.
- USD requiere moneda explícita o GTQ y `PI ExRate` positivo. No se trasladan las tasas fijas ni la suposición de moneda/tasa de React.

## Tablas reutilizadas y ampliación necesaria

Se inspeccionaron las tablas de la copia `127.0.0.1:55441/lims_planning_dev`. No se presupone que el servidor desplegado tenga el mismo esquema.

| Tabla | Uso |
|---|---|
| `public.planificacion_test_map` | Catálogo de autoridad; la edición escribe esta tabla y conserva versión y auditoría nativas. No escribe su columna generada. |
| `public.planificacion_no_programar` | Decisiones explícitas existentes, cuando la tabla está disponible. |
| `public.planificacion_activa` | Proyección operativa compatible con React. Se conservan horarios, marcas y datos ajenos al importador. |
| `public.planificacion_archivada` | Protección de combinaciones archivadas manualmente. |
| `public.planificacion_test_map_pendiente` | Cola compatible con el contrato React; ausente en la copia inspeccionada. |
| `planning.episodes`, `executions`, `source_rows`, `batches`, `history` | Identidad por ingreso/ejecución, fuente íntegra, revisión y auditoría ya existentes. |
| `planning.react_execution_links` | Enlace aditivo entre UUID nativo y fila React, con admisión y versión de regla. |

Las tablas activa y archivada de React conservan su restricción `UNIQUE(job_prueba)`. Una fila React puede representar varias ejecuciones verificadas del mismo ingreso, prueba de planificación y laboratorio. El enlace permite esa relación sin fusionar los UUID. Las digitaciones y chequeos siguen separados por ejecución en .NET; una marca agregada de React no acredita automáticamente el estado de cada ejecución.

El enlace permite retirar o archivar una fila desde React sin bloquear esa operación. Su referencia queda vacía, se conserva el historial nativo y el trabajo requiere conciliación antes de volver a la vista operativa.

Ingresos o laboratorios incompatibles para la misma combinación se presentan como `ReactIdentity`, sin sobrescribir la fila existente. Dos filas fuente con el mismo nombre técnico siguen requiriendo una conciliación explícita de identidad; el agrupamiento de la proyección no resuelve esa ambigüedad.

El SQL revisable está en `scripts/planning/install-html-react-integration.sql`: añade el enlace, la cola React que falta, `manual_pinned` y los tipos de fuente `Side2`/`React Active` al historial. Ninguna importación ni el arranque de API ejecuta DDL. La instalación sobre la copia histórica y el servidor queda pendiente de revisión del destino.

## Flujos del cliente

1. **Summary + Report List:** misma selección existente; añade ingreso objetivo, admisión por fila y reglas de negocio en la revisión obligatoria.
2. **Carga complementaria:** equivalente a Side 2, con cinco encabezados y sus alias. Guarda el archivo original, laboratorio declarado y origen manual. Protege sus fechas y la fila con `manual_pinned`; conserva datos comerciales previamente conocidos.
3. **Registros de React:** obtiene una instantánea de `planificacion_activa`, conserva su ID y JSON completos, y prepara una revisión antes de enlazar identidades. La adopción de una fila existente no cambia sus valores. Una fila con prueba explícita pero sin nombre técnico conserva esa procedencia en el JSON original.

Confirmar utiliza una transacción para datos nativos, proyección React, cola y auditoría. Una huella de las tablas y del catálogo detecta cambios externos incluso si la revisión nativa no cambió. Un segundo confirmado del mismo lote es idempotente.

La revisión de tablas existentes conserva el WIP y los estados de autoridad cuando ya existe fuente Summary para ese episodio; una diferencia se presenta en la sección H y conserva el valor React original en procedencia. Las correcciones WIP autorizadas se reflejan en las filas enlazadas de React sin cambiar sus marcas de digitación. Los cierres y su reapertura respetan la admisión, por lo que los componentes conservados de Toxic Elements no se exigen nuevamente como trabajo individual.

Las vistas operativas respetan la admisión persistida. El histórico y los expedientes conservan los registros excluidos. Los lotes antiguos mantienen su contrato `Preserve`; el endpoint público de nuevas cargas exige `HtmlReact` y no permite usar aquel contrato para saltarse las reglas nuevas.

## Reglas React adicionales

- `DisneyRfSccp:v1`: RF y Buyer Disney producen SCCP si falta. FR y otros clientes no lo activan. Se conserva el UUID del padre y se reutiliza el UUID derivado al reimportar.
- `ReactToxicElements:v1`: requiere Lead, Cadmium, Mercury y Chromium VI. Conserva las ejecuciones fuente, retira los tres primeros componentes de la admisión individual, genera TOTAL TOXIC ELEMENTS y mantiene Chromium VI independiente.
- Una regla derivada que pierde su condición se identifica como `RuleRetired`; conserva la ejecución y su historial.

Estas reglas se aplican en la importación normal y conciliación de tablas. Side 2 respeta las pruebas explícitas del archivo y no agrega aquellas transformaciones.

## Verificación y límite de entrega

`powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-html-react-integration.ps1` ejecuta reglas, PostgreSQL desechable, permisos y compatibilidad del cliente. Requiere el clúster local existente. Las pruebas PostgreSQL crean bases `lims_planning_ci_<uuid>` y las eliminan; no importan datos ni instalan el esquema nuevo en la copia histórica.

La instalación local revisable se ejecuta mediante `scripts/install-html-react-development.ps1 -Install` únicamente después de autorizar las ampliaciones. Restringe host, puerto y base, crea un respaldo completo y compara conteos y huellas de los valores anteriores. Sin `-Install` solo informa que está preparado.

El 23 de noviembre de 2026 el usuario eligió dejar el código y SQL preparados. Las ampliaciones no se instalaron en `lims_planning_dev`; la activación y validación con sus datos históricos quedan pendientes.

Los resultados TRX quedan en `artifacts/html-react-integration/tests`. El conjunto incluye fixtures y algunos XLSX históricos ya disponibles, siempre contra bases CI aisladas. Compilar el cliente no acredita aceptación visual ni funcionamiento del servidor instalado. No se migran automáticamente chequeos históricos agregados a ejecuciones individuales.
