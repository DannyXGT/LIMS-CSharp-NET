# Report List: auditoría previa y decisiones

20 de noviembre de 2026. Inspección de código, antes de modificar comportamiento o base.

- El host existente es **WinUI 3**, no WPF. Se conserva AppShell, JWT, permisos y PostgreSQL.
- Referencias visuales: `ReferenceMaterialsPage.xaml`, `PreparationsPage.xaml`, `CommercialPreparationDetailDialog.cs`, `LimsTabStrip`, `Motion` y recursos `Lims*` existentes.
- Flujo vigente: `PlanningReportListView`, `ReportListGrid`, `PlanningApiClient`, `PlanningEndpoints`, `PostgresReportList` y contratos `ReportListContracts`.
- `planning.executions.id` identifica la ejecución. JOB y equivalencia no son únicos. `original_test`, `equivalence`, `execution_type`, episodio y procedencia se conservan.
- `digitized_at`, `tested_on`, `digitized_by` representan la digitación. `report_checks.checked_at` y `actor_id` **también representan el marcado anterior**, no una revisión posterior. No se renombrarán ni reinterpretarán.
- `report_blocks.number` es una secuencia que se reutiliza por actor durante 15 minutos de tiempo del servidor. No representa `horario` ni `fecha_prueba`. Se conserva sin convertirlo.
- `report_close_alerts` señala última pendiente completada entre registros conocidos; no acredita universo TIPS ni cierre oficial.
- `Digitacion_23_OCT.html` inspeccionado en `Desktop/programas/App_web`: `toggleRow` selecciona temporalmente; `abrirConfirmacion` revisa selección; confirmación asigna `h`, `f`, `dp` y reinicia `chk`; `checkGroups` agrupa fecha+hora; `computeCierres` evalúa todas las filas cargadas. Se traslada la operación, sin localStorage, eliminación de duplicados ni exportación/importación para sincronizar Consulta.
- `Desktop/programas/Analisis_App_web.md` confirma que las tablas de Consulta y Digitación históricas estaban separadas y que sus cierres podían depender del subconjunto local.
- El Report List React anterior usa `checked_at`, `prueba_completa`, bloques y modo manual/escáner. No contiene una revisión posterior equivalente al `chequeado` del HTML.

## Diseño de persistencia aditiva

Sesión UUID por confirmación, fecha/hora operativa explícita, actor del servidor y fecha de guardado distinta. Miembros inmutables con versión de digitación; revisión posterior por otro evento con actor, fecha y revisión. Reintento de la misma solicitud no duplica sesión. Correcciones o cambios de versión invalidan el chequeo del grupo; no se trasladan a otra ejecución ni a otra hora. Registros antiguos sin sesión permanecen históricos, sin asignarles una sesión u hora inventada.

Consulta comparte las mismas ejecuciones y expone el chequeo derivado de la sesión válida. Cierres calcula sobre todos los episodios del JOB, excluye NO PROGRAMAR explícito, retenciones, equivalencias faltantes, incidentes y filas de Report List sin conciliar; informa que la cobertura es la persistida y no certifica cobertura externa completa.

## Límites de validación

Solo se autoriza la copia restaurada local `127.0.0.1:55441/lims_planning_dev` y bases efímeras de pruebas. Ninguna migración o cambio en producción. Capturas, persistencia, concurrencia y rendimiento deben documentarse después de ejecutar; este archivo no acredita esos resultados.

## Corrección prioritaria de columnas y operación, 20 de noviembre

Inspección repetida del HTML original, `ReportListGrid`, contratos, consultas SQL, esquema de sesiones, `ReferenceMaterialsPage.xaml`, `PreparationsPage.xaml` y recursos compartidos. El adjunto de esta solicitud contiene solamente `Texto pegado.txt`: no incluye las capturas anterior/actual mencionadas. No se atribuye una comparación visual a imágenes ausentes. Las capturas de las referencias se obtuvieron después desde el cliente nativo autenticado.

| Campo/flujo original | Procedencia y conservación en el cliente nativo |
|---|---|
| Due Date (`d`) | `episodes.due_date`; predeterminada y protegida frente a ocultación accidental. |
| Fecha de ingreso / Cliente | `episodes.in_date` / `episodes.buyer`, provenientes de los datos persistidos de Summary/WIP. El HTML de Digitación no incluye esos campos en `rowToCloud`; no se deducen del JOB. |
| JOB / prueba / laboratorio (`j`, `p`, `l`) | Episodio y ejecución. Equivalencia verificada en `executions.equivalence`; si falta, se muestra el original con «Equivalencia pendiente». El original permanece en tooltip y columna opcional. |
| Fecha/hora y usuario (`f`, `h`, `dp`) | `tested_on`, `digitized_at`, `digitized_by`; selección temporal y confirmación antes de guardar. Actor resuelto por el servidor. |
| Marcado (`m`) | Selección temporal independiente de digitación y chequeo. Checkbox por ejecución y selección múltiple nativa. |
| Chequeado (`chk`) | Revisión posterior centralizada, con usuario, fecha, versiones e historial. Solo en Chequeo de horarios y expediente/Consulta configurables. |
| Agrupación `checkGroups()` | Fecha y minuto operativo, incluyendo todas las confirmaciones del horario. Sesiones y miembros permanecen inmutables; no se fusionan ni se eliminan pruebas repetidas. |
| Exportación de chequeados | CSV UTF-8 con identidades y revisores; recupera Fecha de ingreso, Due Date y Cliente. Los horarios incompletamente revisados o con excepciones no se exportan como grupos chequeados. |
| Bloques anteriores / permanencia / captura AR | Conservados como datos y controles existentes; bloque opcional, sin sustituir el horario. No se modifica tracking ni autenticación. |
| Copiar / selección / disposición | Ctrl+C de rango rectangular, Shift+flechas, Tab/Enter, Escape y selección múltiple. Orden y ancho en menú contextual de cabecera; persistencia local por ID de usuario. Sin botón redundante de copiar. |

La tabla anterior convertía cualquier ejecución incompleta a «Pendiente» al renderizar. La corrección muestra literalmente el estado del backend: «Sin información», «Información incompleta», «Pendiente» si es el estado recibido y «Digitada» para `Digitado`. No modifica datos para cambiar esa etiqueta.

No se necesitó otra migración ni se modificaron Importaciones, WIP, Administración o Programación en esta corrección. Los cambios de sesión, persistencia y chequeo ya existentes en el árbol de trabajo se conservaron. La integración de navegación únicamente suministra el ID de la sesión autenticada a las preferencias de columnas.
