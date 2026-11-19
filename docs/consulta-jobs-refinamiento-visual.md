# Consulta de Jobs: refinamiento visual nativo

Entrega del 17 de noviembre de 2026. Se modificaron exclusivamente Consulta y sus ventanas de búsqueda múltiple y Estado del JOB. Las pantallas Estándares, Preparaciones, AppShell, autenticación, contratos y backend no forman parte del cambio.

## Referencias y diferencias

Se revisaron `ShellPage.xaml`, `ReferenceMaterialsPage.xaml`, `ReferenceMaterialDetailDialog.xaml`, `SolutionGroupPage.xaml`, `SolutionGroupDetailDialog.xaml`, el catálogo `LimsIcon` y los estilos `LimsControls.xaml` / `LimsTheme.xaml`. No se encontró `Consulta_Jobs_V5_Barra_Compacta.html`; sí está disponible `docs/ui-reference/Consulta_Jobs_Prototipo_Interactivo.html`, que sirve de referencia operativa junto con la implementación existente.

| Elemento | Versión anterior | Resultado |
| --- | --- | --- |
| Encabezado | Descripción, cuatro indicadores con borde y fondo | Título compacto y cinco métricas tipográficas; incluye Sin información y distingue ejecuciones de JOBs |
| Búsqueda | JOB y prueba en campos simultáneos; barra con desplazamiento horizontal | Un buscador con selector JOB / Prueba / Equivalencia / Laboratorio; conserva contiene, exacto y prefijo para JOB |
| Acciones | Buscar, Limpiar, Pegar JOBs y botones de copiar/pegar | Buscar primario; Búsqueda múltiple, Filtros y Columnas secundarios; CSV y Restablecer filtros complementarios |
| Búsqueda múltiple | Diálogo modal | Ventana nativa independiente, pegado desde Excel con Ctrl+V y límite existente de 500 JOBs |
| Tabla | Paleta propia y caracteres junto a encabezados | Recursos del tema LIMS, iconos vectoriales de filtro, flecha de orden, encabezados legibles y rango azul con borde de celda activa |
| Filtros | Campos de anchos diferentes | Campos alineados, fechas con ejemplo ISO y botón Aplicar fijo fuera del contenido desplazable |
| Expediente | Título genérico, resumen de texto y bloques repetidos | JOB y datos generales como identidad, métricas operativas, pestañas compactas, WIP con pares etiqueta/valor y conservación de pestaña al actualizar |
| Feedback | Mensaje pequeño | Carga vinculada a las solicitudes, estado vacío con reintento, anuncio accesible de copia/guardado y paginación deshabilitada en sus límites |

La tabla conserva su modelo, virtualización, selección rectangular, columna JOB fija, desplazamiento, teclado, TSV, vista previa del pegado, edición autorizada, guardado auditado y control de versión. Las consultas, conteos, filtros, orden y CSV continúan en el servidor. No se agregaron checks de digitación ni estados oficiales.

Las acciones reutilizan los botones, foco y estados de interacción del sistema. Las entradas de filtros, feedback y expediente comprueban `Motion.Enabled`, que consulta la preferencia de animaciones de Windows. No se agregaron animaciones por celda.

## Evidencia reproducible

El ejecutable es el cliente WinUI real, registrado con la identidad aislada `Lims.Planning.Validation`. Consume `https://localhost:7441/` con autenticación contra `127.0.0.1:55441 / lims_planning_dev`. Los datos de origen son los XLSX TIPS históricos ya importados en esa copia; no se sustituyeron por fixtures. El JOB de aceptación es `GUAT25038091-REV3`.

Los guiones interactúan mediante UI Automation, teclado y ratón nativos, comprueban el portapapeles de Windows y releen las notas desde la API autenticada. Las observaciones escritas se identifican como `VALIDACION LOCAL ... no operativo`. La prueba confirma que las columnas importadas no cambian.

```powershell
dotnet build Frontend/Lims.Desktop/Lims.Desktop.csproj --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/start-planning-development.ps1 -Desktop
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-planning-desktop.ps1 -Action LoginAndNavigate
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-consulta-jobs.ps1 -Step All -EvidenceName consulta-refinement-125
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-consulta-refinement.ps1 -Scale 125
```

Para validar otra escala, `scripts/set-consulta-validation-scale.ps1` utiliza el selector real de Windows y verifica `GetDpiForWindow`. No reescala imágenes. La escala original de este equipo es 125 % y se restaura en un bloque `finally` después de probar el 100 %.

Los registros completos están en `artifacts/planificacion-dev/consulta-refinement-100/` y `consulta-refinement-125/`. `acceptance-All.json` documenta selección, arrastre, Shift, copia TSV, pegado persistido, celdas protegidas, Escape/Enter/Tab, expediente, Teams, preservación de selección, Solo tabla, listas, filtros, páginas y ausencia de checks. `refinement.json` registra el DPI efectivo, las búsquedas por campo contrastadas con API, alineación de filas, ventana de lista, orden, panel de filtros, estado vacío, pestaña WIP y controles sin duplicación ni superposición.

Resultado final: compilación x64 con cero errores y advertencias; 13 pruebas automatizadas aprobadas; 20 comprobaciones integrales y 12 comprobaciones de controles/diseño aprobadas en cada escala. DPI confirmado: 96 al 100 % y 120 al 125 %. Se inspeccionaron las capturas originales de Consulta, rangos, filtros, búsqueda múltiple y expediente. Se corrigieron la alineación de filas al caber todas las columnas y la posición del botón Aplicar filtros. Windows quedó nuevamente al 125 %.

Entrega de capturas originales, hashes y registros: `artifacts/validation/consulta-jobs-refinamiento-2026-11-17/`. El paquete ZIP del mismo nombre no contiene credenciales ni archivos de configuración local.

- [Consulta al 100 %](../artifacts/validation/consulta-jobs-refinamiento-2026-11-17/Consulta-100.png)
- [Consulta al 125 %](../artifacts/validation/consulta-jobs-refinamiento-2026-11-17/Consulta-125.png)
- [Expediente al 125 %](../artifacts/validation/consulta-jobs-refinamiento-2026-11-17/Expediente-125.png)
- [Panel de filtros al 125 %](../artifacts/validation/consulta-jobs-refinamiento-2026-11-17/Filtros-125.png)

## Límites

Esta validación cubre el cliente nativo y una API PostgreSQL local autenticada. No acredita datos operativos actuales, despliegue productivo, cierre oficial TIPS, concurrencia LAN, equipos físicos de 8 GB/HDD o impresión. No se aplicaron migraciones ni SQL productivo. El comportamiento con movimiento reducido reutiliza la política existente; no se cambió esa preferencia global durante las pruebas.
