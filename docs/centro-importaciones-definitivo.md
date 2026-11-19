# Centro de Importaciones: inspección y correspondencia

## Alcance confirmado antes de editar

Se trabaja en LIMS-CSharp-NET, cliente WinUI integrado en AppShell, con sesión y permisos compartidos. LIMS_2027 es la referencia anterior; no se crea otro ejecutable funcional. Base autorizada: 127.0.0.1:55441 / lims_planning_dev. Se conservan los cambios previos del espacio de trabajo.

Se inspeccionaron PlanningWorkspacePage, PlanningImportsView/Flow/Reviews/Tables, PlanningSheetGrid/Model, PlanningDossierWindow, contratos, endpoints, PostgresPlanningStore/Decisions/Queries/ReviewWorkspace, conciliador, lector XLSX y escritura transaccional. Se reutilizan superficies, botones, tipografía y ventanas del DesignSystem que ya usan Estándares y Preparaciones.

Referencia original: Desktop/programas/App_web/Job_Consulta_V_final.html, flujo Summary (líneas 4090–4235), revisión previa y detección de Express, Next Day y Same Day. No se ejecuta su conexión remota. No se reproducen sus descartes de sufijos REV ni sus tasas monetarias supuestas: se conserva identidad completa y procedencia.

| Función original / solicitada | Implementación existente | Diferencia | Corrección |
|---|---|---|---|
| Seleccionar y conciliar Summary + Report List | Lector real, cobertura, revisión incremental y confirmación transaccional | Flujo embebido sin ventana previa completa | Revisión obligatoria con secciones A–E y confirmación fija |
| Revisa antes de agregar | Tabla de cambios y cinco métricas | Faltan agrupación de equivalencias y emergencias del lote | Datos detallados calculados por API desde input/plan persistidos |
| Resumen de importación | Métricas en página | No hay ventana independiente ni navegación por lote | Ventana nativa con resultados, laboratorio y enlaces al lote |
| Bandejas permanentes | Seis categorías, histórico global, filtro/orden/exportación servidor | No hay alcance por lote; Buyer, incompletos y situaciones requieren completar | Siete bandejas operativas, lote actual por defecto y filtros explícitos |
| Equivalencias / NO PROGRAMAR | Catálogo persistido, versiones, auditoría y permiso | Clasificación individual sin agrupación previa | Agrupar nombre fuente, JOBs relacionados y reutilizar catálogo |
| Datos Buyer/Vendor | WIP de Summary; edición auditada en expediente | Falta distinguir los cuatro casos y acceso directo | Procedencia visible y completar datos con backend existente |
| Selección Excel | Consulta tiene selección rectangular; revisión solo filas | Falta rango de celdas en bandejas | Reutilizar control de selección existente en modo de lectura |
| Exportar | CSV con filtros servidor y límite explícito de 50,000 | Falta selección/categoría; la infraestructura XLSX es de lectura | CSV para Excel por filtro, selección o categoría; no se incorpora un escritor XLSX nuevo |
| Resolver / retener / reabrir | Incidentes versionados y resolución manual Solo Summary | Faltan decisiones para casos derivados y exclusión justificada | Persistir decisiones estables y auditar sin borrar datos |
| Reimportación | Fingerprint, revisión global, UUID independiente, procedencia y conservación manual | Mantener el contrato | Regresión con XLSX originales e identidades persistidas |

Las ausencias de Report List no prueban pérdida: ese archivo no contiene fechas de cobertura verificables. El conteo de Solo Summary representa diferencias comprobadas entre archivos disponibles; su cobertura temporal no puede afirmarse.

## Validación

Validación local con los archivos originales, API autenticada en `https://localhost:7441` y PostgreSQL `127.0.0.1:55441/lims_planning_dev`. No se modificaron producción, InterDB, Supabase ni `planificacion_migracion_test`. La inspección y matriz anteriores se escribieron antes de modificar esta entrega.

### Datos reales y resultados

- Summary original del 17 de noviembre: 15,675 filas; Report List original: 9,400 filas. No se alteraron los XLSX.
- Lote confirmado desde el cliente nativo: `0163be42-a10e-4705-b68f-a2c76b036bd1`, primera carga del 18 de noviembre de 2026. La revisión final de esta entrega es del 19 de noviembre; se conserva la fecha real del lote.
- Los archivos ya estaban incorporados: la confirmación produjo 0 JOBs nuevos, 0 ejecuciones nuevas, 0 actualizados y 15,675 registros sin cambios. Las 9,400 filas de Report List se reconocieron como existentes. Cero altas es el resultado real, no una simulación.
- Quedaron 119 nombres originales pendientes de equivalencia en ese archivo, aceptados explícitamente antes de confirmar. No se inventaron equivalencias, laboratorios, Vendor ni fechas de cobertura.
- La repetición desde el cliente abrió el resumen del mismo lote aplicado, sin volver a escribir sus datos.
- Se conserva la prueba manual de `GUAT25002016-REV4-REV5-RE`, su UUID, nombre original, equivalencia DIMENSIONAL, laboratorio Physical, origen Manual, auditoría y resolución explícita de Solo Summary. Su incorporación nativa se acreditó en la validación anterior; en esta entrega se comprobaron su persistencia, expediente y reimportación.
- El lote actual no contiene emergencias nuevas porque sus ejecuciones ya existían. La prueba positiva separada utiliza las dos filas originales Next Day de `GUAT26011508-REV1` en una base de prueba aislada: 1 JOB de emergencia, 2 ejecuciones distintas, sin duplicación al confirmar nuevamente.

Conteos de pendientes del lote consultado por la API, sin filtros adicionales:

| Bandeja | Pendientes | Resueltos |
|---|---:|---:|
| Solo Summary | 14,722 | 953 |
| Solo Report List | 10 | 0 |
| Sin Buyer/Vendor | 190 | 15,495 |
| Sin equivalencia | 745 | 10,242 |
| Pruebas por agregar | 14,722 | 1,132 |
| Datos incompletos | 10 | 0 |
| Retenidos y resueltos | 0 | 2 |

Las bandejas se superponen y no representan totales sumables. Las categorías de completitud incluyen registros completos como resueltos por su regla; la bandeja final muestra únicamente intervenciones persistidas. El alcance, la situación y los filtros de columna se aplican en servidor a página, contadores y exportación. La interfaz comienza en pendientes del último lote aplicado y permite seleccionar expresamente el histórico.

### Funciones y límites comprobados

- Ventanas nativas independientes de revisión previa A–E y resumen; confirmación obligatoria con aceptación explícita de equivalencias pendientes. Los errores críticos deshabilitan la confirmación.
- Navegación desde el resumen a Consulta de Jobs original por membresía del lote y a sus bandejas; apertura posterior de bandejas y expediente real.
- API autenticada: nueve comprobaciones satisfactorias, incluido un lote vacío que devuelve cero, WIP con procedencia, conservación manual, catálogo persistido y NO PROGRAMAR.
- Exportación CSV completa de 14,722 filas y exportación de una selección de una fila, ambas desde servidor. Incluye lote, origen, situación, fecha y motivo. No queda limitada a la página de 100 filas. El límite máximo explícito es de 50,000; si se supera, se solicita acotar el filtro, sin truncar silenciosamente.
- Guardado nativo comprobado: `Downloads/Consulta de Jobs.csv`, 14,722 filas y SHA256 `D3FD9D7849AF063A69F69187ED9F5551A54E794C425D1D76481EBA05623CD0B1`, idéntico al resultado completo de la API. El selector Guardar se completó por separado; la comprobación posterior del archivo y del mensaje de éxito del cliente fue automatizada.
- Las decisiones de retener, excluir con motivo, reabrir, resolver Solo Summary y conciliar identidad utilizan permisos, versiones y auditoría persistidos. La resolución no sustituye la incorporación de pruebas reales ni permite editar estados oficiales de TIPS.
- Las pruebas sin nombre fuente se conservan como datos incompletos, separadas de las pruebas con nombre sin equivalencia. La clasificación NO PROGRAMAR reconoce tanto el indicador persistido como el marcador original explícito de laboratorio; no reescribe sus valores originales.
- Compilaciones Release de API y cliente: correctas, sin advertencias ni errores. Pruebas automatizadas: 72 casos distintos aprobados (12 aplicación, 22 PostgreSQL, 20 autorización, 18 selección/búsqueda/compatibilidad del cliente), sin sumar ejecuciones repetidas como casos nuevos.

- Interacción nativa final: paginación ida/vuelta, filtro Estado, orden Fecha ingreso, selección y copia de dos filas, arrastre y Ctrl+C de un rango de 3 × 3, recorrido de las siete bandejas y ventana de 1280 × 800. El control conserva su selección; se corrigió la automatización para enviar eventos reales de movimiento, porque mover únicamente el cursor no equivale a arrastrar en WinUI.
- Integridad final: 15,685 episodios y 10,987 ejecuciones, incluyendo la ejecución manual previa. Se compararon todos los UUID del respaldo anterior: ninguno desapareció y no aumentaron los conteos por reimportar. La resolución manual continúa persistida. El SHA256 del ensamblado del cliente activo coincide con la compilación Debug final utilizada para la aceptación; Release también compila correctamente.

Se cerraron los once escenarios nativos documentados en `checks/native-*.json`. Las capturas de diagnóstico, conexiones privadas, respaldos y exportaciones con todos los registros no forman parte del ZIP de evidencias. El CSV guardado permanece disponible localmente en Descargas.

### Capturas reales

En el paquete de evidencias, `ui/` contiene:

| Archivo | Escenario |
|---|---|
| 01-seleccionar-archivos.png | Centro listo, sin ejecutar otra carga |
| 02 a 06-revision-*.png | Ventana independiente, cinco secciones de revisión previa |
| 07-resumen-importacion.png | Resumen del lote realmente aplicado |
| 08-bandejas-lote-actual.png | Pendientes del lote, acciones y paginación |
| 09-expediente-desde-bandeja.png | Expediente real con prueba manual y resolución conservadas |
| 10-seleccion-celdas.png | Rango rectangular de 3 × 3 copiado con Ctrl+C |
| 11-historial.png | Lotes y resultados persistidos |
| 12-reimportacion-sin-duplicados.png | Resumen de la repetición del mismo lote |
| 13-consulta-del-lote.png | Consulta de Jobs original con filtro de lote |
| 14-filtro-orden-copia-filas.png | Filtro, orden y copia de dos filas |
| 15-ventana-reducida.png | Distribución real a 1280 × 800 |
| 16-exportacion-completa.png | Mensaje de éxito después del guardado completo |

Las capturas de revisión previa, confirmación y reimportación se tomaron el 18 de noviembre durante esas operaciones. Las de interacción, bandejas, expediente, historial y exportación se verificaron nuevamente el 19. La evidencia anterior de adición y resolución manual se conserva identificada como `prior-native-manual`, sin presentarla como una nueva inserción.

No se acreditan despliegue productivo, red LAN, 50 usuarios simultáneos ni rendimiento en equipo de 8 GB con HDD. Los campos desconocidos y la cobertura no verificable de Report List permanecen expresamente no informados. CSV es el formato implementado; no hay escritor XLSX reutilizable en esta solución.
