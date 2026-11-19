# Centro de Importaciones operativo

## Inspección previa a los cambios — 18 de noviembre de 2026

Se revisaron `ReferenceMaterialsPage.xaml`, `PreparationsPage.xaml`, `PreparationEditorHeader.cs`, `LimsControls.xaml`, el expediente y las vistas, contratos, endpoints y persistencia de Planificación. Se reutilizan las superficies, tabla compacta, tipografía, botones, iconos y pestañas del DesignSystem; no se modifica otro módulo.

La confirmación existente guarda el lote y los XLSX originales, hashes, cobertura, corte, actor, plan, vista previa y revisión base. En una transacción copia episodios, ejecuciones y filas fuente; conserva campos manuales, concilia incidencias, audita y aumenta la revisión. Se conserva este motor.

Se volvieron a revisar los cuatro XLSX reales de los días 13 y 17 mediante la inspección de fuentes ya archivada. Report List no acredita cobertura temporal ni digitación. Summary es autoridad de WIP y estados oficiales. No se generan archivos ni JOBs ficticios.

## Qué significan 14,723 casos

Consulta real de PostgreSQL: `artifacts/planificacion-dev/centro-operativo/inspection-before.json`. Hay **14,723 incidencias SummaryWithoutReport pendientes, de 14,723 episodios distintos**. Son el acumulado persistido del histórico local, con procedencia de **seis lotes confirmados**; no son 14,723 errores del último lote. Otros 952 episodios de esta categoría figuran resueltos porque tienen pruebas vinculadas a Report List. Ninguno de los 14,723 pendientes tiene un vínculo Report List.

La tabla interna también contiene 14,723 MissingTests pendientes: describe la ausencia de cualquier ejecución en estos mismos episodios. No se vuelve a contar como otra bandeja o error. Sin Vendor es otra condición: 190 pendientes; Solo Report List tiene 10 episodios pendientes. Existen 745 ejecuciones sin equivalencia. Las 507 filas fuente Report List sin vínculo corresponden al histórico de cargas y no equivalen automáticamente a 507 decisiones actuales: la revisión debe identificar la misma fila fuente por hash, hoja y número, y mostrar su vínculo más reciente.

Las bandejas son condiciones que pueden solaparse. Sus cantidades no deben sumarse como un total de JOBs únicos. Un Close Date externo, una fecha vencida o una cobertura distinta no son errores estructurales ni cierran un JOB local.

## Correspondencia de la transformación

| Función | Situación encontrada | Cambio requerido |
|---|---|---|
| Formulario | Permanente, alto; cuatro pestañas | Encabezado compacto, cuatro etapas, procedencia contraíble, tres secciones |
| Revisión | Texto repetido, sin datos WIP, solo pendientes | Seis bandejas con pendientes, retenidos y resueltos; filas enriquecidas desde PostgreSQL |
| Métricas del lote | UpdatedJobs ya combina cambios WIP/estado | Etiquetar JOBs actualizados; calcular WIP por episodios distintos del plan, sin inventar una métrica |
| Tabla | Selección única, búsqueda parcial | Selección múltiple, copia TSV, filtros de columna, ordenamiento y exportación de todo el resultado filtrado |
| Decisiones | API persistida y versionada | Acciones accesibles en bandeja: agregar, retener, resolver y reabrir con motivo y auditoría |
| Manual | Campos libres en expediente | Ventana contextual precargada con Summary y selección del catálogo validado |
| Historial | Nombres/fechas sin actor ni resultados | Mostrar actor y resumen persistido; consultar procedencia sin reimportar |
| Conservación | Identidad independiente y transacción existentes | Mantener, probar reimportación y resolución manual persistida |

## Validación y entrega

Las escrituras se hicieron exclusivamente en `127.0.0.1:55441/lims_planning_dev`, con respaldo `artifacts/planificacion-dev/centro-operativo/pre-operativo-20261118.backup`. No se modificaron producción ni sistemas externos. Se utilizaron los XLSX originales del 17, sin fabricar ni alterar filas fuente.

| Escenario | Recorrido y resultado real |
|---|---|
| A | Selectores nativos Summary + Report List → validación → conciliación → confirmación. Lote persistido, formulario contraído y Revisión disponible. Los mismos archivos en otro corte no crean JOBs ni pruebas duplicadas. |
| B | GUAT25002016-REV4-REV5-RE, inicialmente Solo Summary: búsqueda, selección, expediente con Buyer J Crew, Vendor Apparel Links S.A., ingreso 2026-08-19, Due Date 2026-08-24 y estado oficial Report Closed. |
| C | Ventana nativa con Summary precargado. Se seleccionó la entrada real del catálogo «Appearance After Laundering / Apariencia Después de Lavado Doméstico», equivalencia DIMENSIONAL, laboratorio Physical. PostgreSQL guardó la ejecución manual `1d9c4057-4862-4a28-89b6-f811aecbb43f`. |
| D | Resolución explícita con motivo, usuario y fecha. El caso desapareció de pendientes y quedó consultable en Resueltos y en el historial del expediente. Report Closed se conservó como estado oficial independiente. |
| E | Mismos archivos y corte: lote ya aplicado; una sola ejecución manual, mismo UUID y caso todavía resuelto. |
| F | Consulta de Jobs muestra la ejecución manual y los mismos datos del Summary. Digitación sigue «Sin información»; no se agregó ningún marcado. |

La asignación C es una **decisión de validación local autorizada**, documentada con ese motivo; no representa una orden oficial recuperada de TIPS. No se creó ningún JOB ficticio. Se conserva esta decisión en la base aislada para poder revisar la persistencia y auditoría.

Se preservaron **15,685 de 15,685 episodios y 10,986 de 10,986 ejecuciones anteriores**, con sus UUID originales. La base quedó con 10,987 ejecuciones: la diferencia es la única prueba manual de C. Solo Summary pasó de **14,723 pendientes / 952 resueltos** a **14,722 pendientes / 953 resueltos**.

### Pruebas y comprobaciones

- **65 pruebas automatizadas distintas aprobadas**: 11 reglas de aplicación, 18 PostgreSQL, 18 selección/compatibilidad del cliente y 18 autorización de API. Sin fallos ni omisiones en los resultados finales. La prueba de revisión se amplió con filas reales para verificar filtros, exportación, catálogo obsoleto, retención, resolución, reapertura, concurrencia y reimportación.
- **A–F ejecutados en WinUI**, API autenticada y PostgreSQL aislado; archivos `scenario-A.json` a `scenario-F.json`.
- Tabla nativa: limpiar, anterior/siguiente, filtro Estado, orden por Fecha ingreso, selección de dos filas y copia TSV contrastada con el orden exacto del servidor. Se recorrieron las seis bandejas y el historial con usuario y resultados. `scenario-Interactions.json`.
- Respuesta de bandeja observada de aproximadamente **0.3–1.3 segundos con el servidor caliente**; la primera consulta en frío tardó unos 4 segundos. Son mediciones locales, no una garantía para otra máquina. El cliente carga 100 casos por página y virtualiza las filas.
- Compilación x64 y API con cero errores y advertencias; la verificación final compara el SHA256 de la salida x64 normal, la copia de validación y el ensamblado AppX realmente ejecutado.

La matriz y los datos iniciales se conservaron antes de los cambios. Durante la validación se corrigieron el formato del calendario, una carrera entre la carga inicial y Nueva importación y la reutilización de controles visuales. La confirmación y las interacciones de tabla se repitieron después de esas correcciones. Los lotes realmente confirmados permanecen en el historial.

Las pruebas unitarias de regresión existentes conservan sus fixtures aisladas. A–F y la nueva prueba de revisión usan datos originales de los archivos reales; las capturas y la base de aceptación no contienen JOBs ficticios creados para este trabajo.

### Clasificación y alcance de las cantidades

- Solo Summary cuenta episodios persistidos con esa condición, una vez por episodio. Sus resueltos incluyen vínculos posteriores a Report List y resoluciones manuales explícitas.
- Solo Report List conserva las pruebas aunque falte Summary; no se inventan fechas, Buyer, Vendor ni estados.
- Sin Vendor es independiente; sus resueltos incluyen episodios cuyo Vendor está informado y condiciones reconciliadas por la fuente. No implica una resolución manual en todos los casos.
- Equivalencias pendientes cuenta ejecuciones sin clasificación válida; los resueltos tienen clasificación o NO PROGRAMAR validado. La prueba manual de C aumenta sus clasificados en uno.
- Conciliación ambigua identifica filas fuente mediante **hash del archivo + hoja + número**, conservando el vínculo más reciente. Las 507 filas históricas sin vínculo de la inspección inicial representan **169 filas fuente distintas**, ya vinculadas en cargas posteriores: **0 pendientes y 169 resueltas**.
- Errores de importación muestra problemas bloqueantes reales guardados con los lotes. Los avisos de cobertura o importes no verificables permanecen en la validación del archivo, fuera de esta categoría. Los ejemplos actuales no tienen errores estructurales.

Los contadores de las seis bandejas corresponden al histórico local; el total del pie corresponde a la bandeja, situación, búsqueda y filtros seleccionados. Las categorías pueden coincidir en un mismo JOB. La banda superior describe la **instantánea del lote**: su número de revisiones es el calculado en la previsualización, no el total vivo de pendientes ni una cantidad de errores. Cada indicador abre sus diferencias o condiciones correspondientes.

### Funcionalidades implementadas

Encabezado del LIMS, tres secciones y cuatro etapas; componentes equilibrados de archivos con cobertura y validación; procedencia expandible; resultados compactos seleccionables; seis bandejas independientes; tabla virtualizada y selección múltiple; copia para Excel; filtros por columna, calendarios y ordenamiento del servidor; búsqueda y exportación completa de resultados filtrados; expediente independiente; asignación desde catálogo versionado; retener, resolver y reabrir con permisos, motivo, actor, fecha y concurrencia; historial y conservación del motor incremental existente.

Al reducir la altura se presenta un selector compacto de bandejas con sus cantidades y se contrae la banda de resultados junto con la procedencia. Así se conserva espacio para las filas. La información del lote permanece disponible al expandir la procedencia y desde Historial.

### Límites y pendientes reales

- No se hizo una prueba sobre hardware físico de 8 GB, HDD, LAN ni carga de múltiples usuarios. Se comprobó paginación/virtualización y la ventana reducida en este equipo.
- Los XLSX no proporcionan ID oficial de ejecución, fecha/hora de prueba ni digitador. Report List tampoco acredita cobertura temporal. Se conservan como desconocidos y separados de la clasificación del catálogo.
- El recorrido nativo C utiliza una asignación manual real del catálogo para comprobar funcionalidad local. La pertinencia analítica de esa asignación a una orden oficial exige la decisión del coordinador en su operación real.

Evidencia: `artifacts/validation/centro-importaciones-operativo-2026-11-18`, con capturas originales del cliente, resultados A–F, pruebas, inspección inicial y manifiesto SHA256. Se excluyen credenciales, respaldo, archivos fuente completos y listados privados de UUID.
