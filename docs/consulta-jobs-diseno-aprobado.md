# Consulta de Jobs: implementación del diseño aprobado

La referencia es la imagen adjuntada por el usuario el 17/11/2026: encabezado horizontal con matraz, título y subtítulo, métricas dentro de la misma superficie y acciones a la derecha; debajo, buscador y herramientas en dos filas. Se implementó con WinUI 3 en el cliente existente. No se generaron imágenes de producto, HTML, WebView ni otra autenticación.

## Composición y comportamiento

- Encabezado azul marino con degradado discreto, acento diagonal, icono del módulo y métricas semánticas. Los cinco valores vienen de `PlanningPage.Counts`, sobre el filtro completo del servidor. Sin información mantiene su significado y no se convierte en Pendiente.
- Solo tabla conserva la cabecera compacta y libera el área de búsqueda y métricas. Estado del JOB es primario azul. El encabezado reduce tamaño e iconos secundarios cuando falta ancho; las etiquetas y cantidades siguen visibles.
- Buscador único con JOB, Prueba, Equivalencia, Laboratorio y Cliente / Buyer. Este último usa explícitamente el campo Buyer ya existente; no incorpora un catálogo de clientes inventado. Contiene, Exacto y Comienza con conservan los modos admitidos para JOB. Los demás campos mantienen el filtro Contiene del contrato actual.
- Búsqueda múltiple independiente: líneas/comas/punto y coma/tabulaciones, normalización, eliminación de duplicados de entrada, conteo, Buscar, Limpiar y Cancelar. Muestra encontrados y no encontrados. La comprobación de existencia recorre las páginas necesarias de la API autenticada; nunca determina ausencia usando solo la primera página. La tabla conserva las ejecuciones duplicadas legítimas.
- Filtros por columna y combinaciones existentes permanecen en servidor. Aplicar filtros queda fuera del área desplazable. Columnas permite mostrar, ocultar y restablecer la distribución original, con JOB obligatorio.
- Tabla virtualizada existente con estados como etiquetas compactas, selección rectangular, celda activa diferenciada, encabezados con orden/filtro y fila JOB fija. La barra inferior integra registros, página, Anterior/Siguiente, acceso al expediente y ayuda de teclado.
- El expediente conserva resumen, pruebas, WIP, incidencias, historial, procedencia y copia para Teams. Su encabezado comparte la paleta aprobada y el tamaño inicial se ajusta al área de Windows. Actualizar conserva la pestaña. Cerrar conserva rango, filtros y posición de Consulta.

Responsable y Observación siguen utilizando las rutas, permisos, versiones, validaciones, transacción y auditoría existentes. Los campos importados y digitación permanecen protegidos. Las transiciones consultan la preferencia de movimiento de Windows; no se agregan animaciones por fila ni por celda.

## Archivos de implementación

| Archivo | Cambio |
| --- | --- |
| `Frontend/Lims.Desktop/Planning/ConsultationHeader.cs` | Encabezado nativo de la imagen aprobada y adaptación de tamaño |
| `Frontend/Lims.Desktop/Planning/ConsultationVisuals.cs` | Recursos locales, botones, iconos y superficies de Consulta/expediente |
| `Frontend/Lims.Desktop/Planning/PlanningJobsView.cs` | Buscador, herramientas, columnas y barra inferior integrada |
| `Frontend/Lims.Desktop/Planning/PlanningMultiSearchWindow.cs` | Entrada de Excel, resultados y cancelación de consultas |
| `Frontend/Lims.Desktop/Planning/PlanningMultipleLookup.cs` | Normalización y comprobación completa de encontrados/no encontrados |
| `Frontend/Lims.Desktop/Planning/PlanningSheetGrid.cs` | Etiquetas de estados y coherencia entre foco, celda activa y desplazamiento |
| `Frontend/Lims.Desktop/Planning/PlanningDossierWindow.cs` | Superficie del expediente y tamaño limitado al área disponible |
| `Frontend/Lims.Desktop/Planning/PlanningJobStatus.cs` | Refinamiento previo conservado: resumen, WIP, historial y procedencia |
| `Tests/Frontend/Lims.Desktop.Tests/PlanningMultipleLookupTests.cs` | Duplicados de entrada, ejecuciones legítimas en varias páginas y fallo de consulta |
| `scripts/validate-consulta-approved.ps1` | Prueba real del diseño, tamaños de ventana, columnas y búsqueda múltiple |
| `scripts/validate-consulta-jobs.ps1` | Aceptación integral sobre la interfaz real y persistencia aislada |
| `scripts/set-consulta-validation-scale.ps1` | Cambio y comprobación del DPI real de Windows durante las pruebas |

Se reutiliza `PlanningJobStatus.cs`, refinado en el trabajo anterior. No se cambiaron AppShell, Estándares, Preparaciones, contratos, backend, permisos ni esquema PostgreSQL en esta fase.

## Entorno y evidencia

Cliente WinUI real con identidad aislada `Lims.Planning.Validation`, autenticación existente, API `https://localhost:7441/` y copia PostgreSQL `127.0.0.1:55441/lims_planning_dev`. Los archivos TIPS históricos ya importados conservan sus datos. Las escrituras de aceptación se identifican como `VALIDACION LOCAL ... no operativo` y se releen desde la API. El JOB fuente es `GUAT25038091-REV3`.

La consulta completa de esa copia tiene 8.449 registros y 8.445 ejecuciones; la tabla carga páginas de 100 filas. Tres consultas autenticadas locales, con caché caliente, tardaron 14, 16 y 12 ms en obtener una página. Es medición de API local; no es latencia de interfaz, LAN ni prueba de hardware mínimo.

Las capturas proceden de `PrintWindow` sobre las ventanas del ejecutable, sin generación, composición ni reescalado posterior. El guion fija y comprueba el tamaño físico de la ventana a 1920×1080 o 1366×768; no se presenta como prueba de un segundo monitor físico. El DPI se verifica con `GetDpiForWindow`.

```powershell
dotnet build Frontend/Lims.Desktop/Lims.Desktop.csproj --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/start-planning-development.ps1 -Desktop
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-planning-desktop.ps1 -Action LoginAndNavigate
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-consulta-approved.ps1 -Width 1920 -Height 1080 -Scale 125
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-consulta-jobs.ps1 -Step All -EvidenceName consulta-approved-functional-1920x1080-125
```

## Resultado de aceptación

Compilación final: **0 errores y 0 advertencias**. Las pruebas de modelo de tabla, compatibilidad y búsqueda múltiple finalizaron con **16 correctas, 0 fallidas y 0 omitidas**.

| Ventana física | Escala Windows | DPI comprobado | Comprobaciones de diseño y controles | Aceptación funcional integral |
| --- | --- | --- | --- | --- |
| 1920×1080 | 100 % | 96 | 13 correctas | — |
| 1366×768 | 100 % | 96 | 13 correctas | — |
| 1920×1080 | 125 % | 120 | 13 correctas | 20 correctas |
| 1366×768 | 125 % | 120 | 13 correctas | 20 correctas |

La aceptación integral utiliza ratón, teclado y portapapeles reales: celda individual, rango 5×4, arrastre, Shift+clic, Shift+flechas, desplazamiento automático, copia TSV, menú contextual, pegado de 10 valores y relectura de persistencia. También verifica doble clic, Enter, Escape, Tab, campos protegidos, expediente, Teams, Solo tabla, paginación y filtros del servidor. Las escrituras se restringen a la copia de desarrollo indicada arriba. Windows quedó en su escala original de **125 %**.

Se corrigieron dos defectos detectados durante esas pruebas: la primera fila podía quedar desplazada al actualizar una tabla corta y, cuando todas las columnas cabían al 100 %, la lista podía centrarse respecto al encabezado. La lista conserva virtualización y ahora mantiene alineación, celda activa y posición al editar o cerrar el expediente.

Las evidencias finales se reúnen en `artifacts/validation/consulta-jobs-diseno-aprobado-2026-11-17/` y en `artifacts/validation/Consulta-Jobs-Diseno-Aprobado-2026-11-17.zip`. Contienen capturas originales, informes JSON, copia TSV, persistencia de prueba, TRX, registro de compilación, tiempos de API y un manifiesto con hashes. No incluyen cuentas, credenciales ni configuración de conexión.

## Límites de la validación

La configuración de columnas se conserva en la vista de la sesión. No existe una infraestructura de preferencias de columnas por usuario que se pueda reutilizar, por lo que no se agregó almacenamiento local nuevo ni sincronización entre equipos.

No se aplicaron migraciones ni SQL productivo y no se modificaron InterDB o Supabase. Quedan fuera de esta evidencia el despliegue productivo, estados operativos actuales, cierre oficial TIPS, LAN con múltiples usuarios, equipos físicos de 8 GB/HDD y una prueba física de cambio de monitor. El movimiento reducido reutiliza la política de Windows; no se cambió esa preferencia durante la aceptación.
