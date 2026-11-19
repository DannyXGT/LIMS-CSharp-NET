# Consulta de Jobs: pie compacto

Corrección limitada al pie de la tabla en `Frontend/Lims.Desktop/Planning/PlanningJobsView.cs`. Se conservan el encabezado aprobado, buscador, filtros, columnas, tabla virtualizada, selección, edición, expediente y contratos existentes.

Las tres franjas anteriores se sustituyen por una única barra de **42 DIP**, con borde superior sutil y colores existentes. Al 125 % de Windows mide aproximadamente 52 píxeles físicos. La altura liberada queda disponible para las filas.

- Izquierda: ejecuciones y JOBs de `PlanningPage.Counts`, respetando los filtros del servidor, con separador de miles y singular/plural. No se infieren cantidades a partir de la página visible.
- Derecha: rango de registros y dos botones de flecha de 32 DIP, únicamente si existe más de una página. Anterior/Siguiente se deshabilitan en los extremos y mientras se consulta.
- Selección discreta dentro de la misma barra: `1 celda seleccionada` o `5 filas × 3 columnas`.
- Un botón de teclado abre los atajos implementados. No hay lista de atajos permanente ni botón duplicado de expediente.
- Copia, guardado y errores conservan feedback accesible dentro del mismo espacio; los avisos son temporales y el texto completo está disponible al pasar el puntero. No agregan franjas.
- El mensaje sobre filtrado del servidor pasa a `Debug.WriteLine`. Se conserva el ScrollViewer horizontal existente sin agregar otro contenedor de desplazamiento.

El total de registros de paginación y el de ejecuciones representan conceptos distintos en el contrato. En la copia local hay **8.449 registros, 8.445 ejecuciones y 770 JOBs**. Por ello se muestra `8,445 ejecuciones · 770 JOBs` y `1–100 de 8,449`. El JOB `GUAT25038091-REV3` devuelve `24 ejecuciones · 1 JOB`, sin paginación.

## Verificación reproducible

```powershell
dotnet build Frontend/Lims.Desktop/Lims.Desktop.csproj --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-consulta-footer.ps1 -Width 1920 -Height 1080
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-consulta-jobs.ps1 -Step All -EvidenceName consulta-footer-functional
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-consulta-footer.ps1 -Width 1366 -Height 768
```

El guion específico comprueba altura, límites y ausencia de superposiciones; JOB único; selección 5×3 y copia TSV; ayuda contextual sin pérdida de selección; miles de resultados, navegación y botones deshabilitados; contadores filtrados y estado vacío. El guion integral existente conserva las comprobaciones de Ctrl+V, persistencia autorizada, Enter y acceso al expediente. Se actualizaron sus expectativas y las de `validate-consulta-approved.ps1` para el nuevo texto del pie.

La evidencia corresponde al cliente WinUI real con autenticación existente, API local y PostgreSQL aislado `lims_planning_dev` en `127.0.0.1:55441`. Las capturas son originales de `PrintWindow`, sin imágenes generadas ni reescalado posterior. Los tamaños indicados son de la ventana nativa, al 125 % de Windows. Las escrituras del guion integral se restringen a observaciones de validación local y se releen desde la API; no se escriben datos productivos.

## Resultado

- Compilación final: **0 errores y 0 advertencias**.
- **6 comprobaciones específicas correctas por tamaño**, en ventanas de 1920×1080 y 1366×768 a 120 DPI (125 %). La barra mide 53 píxeles físicos por redondeo de los 42 DIP. No se detectaron elementos fuera del pie ni superpuestos.
- **20 comprobaciones integrales correctas**, incluidos rango rectangular, TSV, portapapeles real, pegado autorizado de 10 valores, protección de campos importados, persistencia, Enter, Escape, Tab y apertura/cierre del expediente.
- La lista dispone de 572 píxeles físicos de altura en la ventana grande y 296 en la reducida. El espacio antes reservado a las franjas adicionales queda dentro de la tabla.

Capturas completas, recortes de detalle sin reescalar, informes JSON, registro de compilación y hash de la DLL ejecutada: `artifacts/validation/consulta-jobs-pie-2026-11-17/`. La validación no acredita producción, LAN ni otros equipos físicos.
