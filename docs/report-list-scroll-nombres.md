# Report List: desplazamiento y nombres de pruebas

La corrección conserva el host, la sesión, los permisos, la carga incremental y la identidad por ejecución. La lectura del nombre requiere actualizar API y cliente; no requiere SQL de instalación.

## Referencia y cambios

Se consultaron `Frontend/src/pages/planificacion/PlanificacionPage.tsx`, `planificacion.columns.ts` y `planificacion.grid.css` de LIMS React. Esa tabla utiliza AG Grid, filas de 32 px, encabezados independientes del desplazamiento vertical, texto recortado y selección de celdas. Los nombres de catálogo de planificación y los nombres técnicos se mantienen separados en su modelo.

- La única columna visible de nombre se titula `Prueba` y contiene el nombre de planificación. El nombre técnico se conserva en el tooltip y la procedencia, sin una columna adicional. Las preferencias anteriores descartan la columna técnica y conservan las otras columnas y anchos.
- La API sigue la relación de React: `planificacion_test_map.planificacion_nombre_id` → `planificacion_nombre.id`, priorizando `planificacion_nombre.nombre_planificacion`; después utiliza `planificacion_test_map.nombre_planificacion` y su alias `nombre_prueba_plan`. No sustituye un nombre de planificación ausente por el nombre técnico.
- Report List resuelve el catálogo actual también para ejecuciones antiguas que no tenían equivalencia guardada. Búsqueda, filtros, ordenación y paginación utilizan esa misma clasificación. Consultar no escribe datos ni fusiona UUID.
- Las clasificaciones manuales y las cargas complementarias quedan protegidas. Las ejecuciones ya digitadas conservan la clasificación guardada en esa operación. Cuando se confirma una nueva digitación, la API valida el catálogo actual y guarda la clasificación utilizada y su auditoría en la misma transacción. Los cambios externos del catálogo invalidan la continuación de una página anterior.
- `ReportTableViewport` deja ambos ejes en el ScrollViewer nativo del ListView. El encabezado permanece encima de las filas y sigue el desplazamiento horizontal.
- `ReportListGrid` reutiliza controles al reciclar filas. El desplazamiento horizontal no reconstruye sus celdas ni recalcula las filas seleccionables visibles. El índice y la versión de diseño se conservan fuera de los recursos XAML para evitar invalidaciones de recursos al reciclar.
- Cuando la plantilla aún no está disponible en la fase inicial, se registra la siguiente fase del contenedor. Si está disponible, se pinta inmediatamente. La misma corrección se aplica a las tablas nativas de horarios y detalle.
- Se conservan selección rectangular, copia TSV, teclado, filtros, ordenación, columnas configurables y distinción entre selección temporal y digitación persistida.

## Verificación

`ReportListTableTests` comprueba el encabezado `Prueba`, la ausencia de la columna técnica, copia y compatibilidad con preferencias anteriores. `PlanningCompatibilityTests` comprueba los contratos ya existentes. Las pruebas PostgreSQL comprueban la prioridad del maestro React sobre alias antiguos, lectura sin mutaciones, filtro por nombre de planificación, digitación con auditoría, NO PROGRAMAR y protección de decisiones manuales e históricas.

La acción `reportListScroll` del VisualHarness compila las clases reales de presentación y carga páginas ficticias de 500 ejecuciones sobre un total de 9,038. Comprueba desplazamiento horizontal y vertical, límites de realización de filas, el nombre de planificación después del reciclado, reutilización de controles, encabezados alineados y renderizado de horarios en ventanas de 1440×900 y 1280×800. No usa sesiones, API ni bases de producción.

Los resultados y capturas se guardan en `artifacts/validation/report-list-scroll`. Los tiempos incluyen esperas deliberadas y no constituyen una medición de LAN ni del hardware de los usuarios. La aceptación con la sesión y los datos del servidor queda separada de esta validación visual con fixtures.

## Abrir el cliente actualizado

Cerrar la ventana anterior y lanzar desde la raíz del repositorio:

```powershell
$env:DOTNET_ENVIRONMENT = "Production"
C:\Tools\WinAppCLI\winapp.exe run `
  .\Frontend\Lims.Desktop\Lims.Desktop.csproj `
  --configuration Debug `
  --arch x64 `
  --property Platform=x64 `
  --property UseSharedCompilation=false `
  --with-alias
```

El comando recompila y prepara el cliente. No aplica migraciones de base de datos ni actualiza el servicio del servidor.
