# Auditoría y corrección de presentación de Estándares

Fecha: 03/10/2026. Alcance: listado, detalle, nuevo, editar, reemplazar y archivar.

## 1. Datos técnicos encontrados

| Superficie | Hallazgo | Corrección |
| --- | --- | --- |
| Detalle | `CreatedByUserId` y `UpdatedByUserId` se mostraban como números | Nombres reales de los usuarios existentes, resueltos por backend |
| Auditoría | Bindings directos a `DateTimeOffset`, con hora UTC, segundos y offset | `dd/MM/yyyy · HH:mm` en `America/Guatemala` |
| Listado y detalle | Fechas de negocio sin formato explícito | `dd/MM/yyyy`, sin conversión de zona horaria |
| Listado y detalle | Pureza enlazada directamente al decimal | `98.09 %`, `100 %` |
| Detalle | Cantidad por unidad enlazada directamente al decimal | `100 mg × 1 unidad`, sin ceros finales |
| Cantidades | Formato disperso y dependiente de la cultura de la estación | Un formatter común con cultura `es-GT` y precisión decimal preservada |
| Detalle | Motivo de baja siempre visible, aunque no existiera | Filas condicionadas al estado y al valor |
| Estados | Estado en azul sin badge; el fallback devolvía el código desconocido | Badge con punto, fondo sutil y etiqueta humana; fallback legible |
| Errores | Mensajes originales del servidor y validaciones del dominio podían exponer inglés, parámetros y nombres técnicos | Mensajes humanos por código y campo, sin interpolar el mensaje técnico |
| Editor | Referencia de soporte mostrada directamente como un ID | Se conserva internamente y se ofrece “Copiar referencia” sin mostrar su valor |
| Accesibilidad | El `ToString()` generado de los records anunciaba UUID, IDs, enums, versiones y escala decimal en el lector de pantalla | Representación textual de filas y opciones limitada a nombres humanos |
| Accesibilidad | Acciones con contenido compuesto y campos con etiquetas externas sin nombre accesible explícito | Nombres accesibles para acciones, paginación y campos |

También se protegieron valores ausentes, blancos y texto literal `NULL`: identificadores opcionales se ocultan en detalle; en listado se usa “No informado” o texto vacío para CAS; en el editor se presentan como campos vacíos. Un usuario histórico sin nombre resuelto muestra “Usuario no disponible”, nunca el ID. Los UUID de material, versión y reemplazo y las FK de catálogos continúan internos. No se encontró un binding visible de booleanos `true/false`.

## 2. Contracts, Application, Repository y API

`ReferenceMaterialDetail` añade cuatro campos opcionales y compatibles con clientes anteriores:

- `createdByName`
- `updatedByName`
- `archivedByName`
- `replacedByMaterialName`

GET de detalle y respuestas de crear, editar, archivar y reemplazar pasan por la misma proyección. Los nombres de los tres actores se consultan juntos, con IDs distintos, proyectando solamente ID y nombre de `usuarios`. Se incluyen usuarios históricos inactivos. Si hay reemplazo, una consulta adicional obtiene su nombre. WinUI no realiza llamadas por usuario.

Los IDs, versiones, decimales, fechas de negocio y timestamps originales permanecen en el contrato. Las propiedades nuevas de formato y visibilidad usan `JsonIgnore`. No cambiaron rutas, permisos, solicitudes de escritura, entidades, esquema, catálogos, migraciones ni reglas del CRUD.

## 3. WinUI

El detalle sigue siendo un panel. Su encabezado reúne nombre, badge, CAS, catálogo y acciones diferenciadas: Editar con estilo de acción principal, Reemplazar como acción secundaria y Archivar con recursos de peligro del Design System. Conservan los estados nativos de hover, pressed y focus.

Los bloques Datos principales, Vigencia, Almacenamiento, Disponibilidad y Auditoría usan tipografía, espaciado y separadores, sin cards anidadas. Los datos principales se distribuyen en dos columnas. El contenido completo se puede desplazar verticalmente.

La tabla comparte formatos con el detalle, usa el badge y tiene tooltips con el valor completo. Sus anchos mínimos y el desplazamiento horizontal conservan las columnas cuando el espacio disponible es insuficiente. La composición sigue adaptándose entre panel lateral y detalle debajo de la tabla.

Nuevo, Editar y Reemplazar comparten el formatter decimal. Los valores iniciales de fecha usan el día de Guatemala; las fechas recibidas del API mantienen su día de negocio. Los catálogos muestran nombres y símbolos. Archivar y confirmar Reemplazar mantienen el motivo y muestran el error dentro del diálogo cuando una operación falla; un motivo vacío impide cerrar la confirmación.

Tras archivar, el refresco conserva la identidad seleccionada para recuperar el detalle si el registro sigue dentro del filtro actual. Si el filtro lo excluye, el panel se vacía correctamente.

## 4. Helpers reutilizables

`ReferenceMaterialPresentation` centraliza:

- `Number`, `Purity`, `Quantity` y `Packages`.
- `AvailabilityPercent`, derivado de cantidad disponible, cantidad por unidad y número de unidades; no se persiste.
- `Date`, `LocalTimestamp` y `ApplicationToday`.
- `HasValue`, `Optional` y `User`.
- `StatusLabel` y `StatusTone`.

`StatusBadge`, en `Lims.DesignSystem`, acepta una etiqueta y un tono semántico. `ReferenceMaterialErrors`, en presentación de Desktop, traduce errores sin exponer su texto técnico. `ReferenceMaterialInput.FormatDecimal` reutiliza el formatter común.

## 5. Comportamiento condicional

- CAS y catálogo del encabezado: solamente si tienen un valor.
- Motivo de baja: solamente con estado Archivado y motivo no vacío.
- Actor y fecha de baja: solamente con estado Archivado y el valor correspondiente.
- Reemplazado por y motivo del reemplazo: solamente con estado Reemplazado y los valores correspondientes; se muestra el nombre, nunca el UUID.
- Porcentaje disponible: solamente con disponibilidad informada y denominador positivo. Una disponibilidad nula no se interpreta como cero.
- Referencia de soporte: solamente si existe; su valor se copia y no se imprime en la interfaz.
- “Por vencer”: representación visual prevista para un código futuro; no se agregó una política de vencimiento ni un cálculo de ese estado.

## 6. Validación

Build completo de `Lims.sln`, Debug x64: **0 advertencias, 0 errores**.

| Suite | Resultado |
| --- | --- |
| Application | 63 aprobadas |
| Infrastructure | 19 aprobadas |
| API | 13 aprobadas |
| Desktop | 35 aprobadas |
| Total | **130 aprobadas, 0 fallos, 0 omitidas** |

Se probaron nombres de creador/modificador/archivador, consulta conjunta de actores y usuarios inactivos, nombre del reemplazo, contrato JSON y compatibilidad con payload anterior, preservación de IDs y valores originales, conversión de timestamps con cambio de día y offsets equivalentes, fechas de negocio, independencia de la cultura de la estación, decimales significativos, pureza, cantidades, porcentaje disponible, valores opcionales, condiciones de archivo y reemplazo, estados desconocidos, representación accesible y errores técnicos sanitizados.

La comprobación de WinUI usa una copia temporal de la aplicación con datos de ejemplo aislados, sin cliente HTTP real ni conexión a base de datos. Se verificaron carga del XAML, badge, panel y auditoría, formulario de edición, nombres accesibles, motivo obligatorio de archivo, tema claro/oscuro y tamaños 1920×1080, 1600×900, 1366×768 y 1100×800. El build y esas comprobaciones no equivalen a validación del backend desplegado ni a CRUD contra PostgreSQL real. Para recibir los nombres reales, el backend desplegado debe incluir la ampliación de contrato y proyección.

Capturas locales: [detalle](../artifacts/presentation-preview/active-1920.png), [auditoría](../artifacts/presentation-preview/audit-1920.png), [editor](../artifacts/presentation-preview/editor-1600.png), [validación del archivo](../artifacts/presentation-preview/archive-empty-validation.png), [vista estrecha](../artifacts/presentation-preview/narrow-1100.png) y [tema claro](../artifacts/presentation-preview/light-1366.png). Son artifacts locales ignorados por Git.

## 7. Alcance conservado

No se ejecutó SQL ni se migraron datos. No se implementaron Stock, Intermedia, Curvas o AQS. La autenticación y las reglas de negocio permanecen intactas.
