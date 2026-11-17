# Planificación dentro de LIMS

Implementación aditiva en `codex/planificacion-aditiva-20261116`. Reutiliza WinUI 3, el login, la sesión JWT con revisión de permisos en servidor, los recursos visuales y los módulos actuales del host. No introduce otra autenticación. Laboratorio Analítico reutiliza las pantallas disponibles; no se implementaron Digitación, Bodega, Reprocesos, programación completa ni finanzas.

## Cambios y protección del trabajo existente

Los archivos nuevos están en `Planning/` de Contracts, Application, Infrastructure, API y Desktop; la navegación del shell se extiende mediante `ShellPage.Workspaces.cs`. Solo se modificaron tres archivos existentes:

- `Backend/Lims.Api/Program.cs`: registro del módulo y sus rutas.
- `Frontend/Lims.Desktop/App.xaml.cs`: cliente de API y navegación con selector.
- `Frontend/Lims.Desktop/MainWindow.xaml.cs`: conexión de la ventana y visibilidad de controles globales del laboratorio.

La comparación SHA-256 de los archivos presentes al inicio confirmó exclusivamente esos tres cambios. Los cambios anteriores de Estándares, Preparaciones y solventes se conservaron. Evidencia local: `artifacts/planificacion-dev/baseline-changes.json`. El login y las vistas actuales de laboratorio permanecen en sus archivos originales.

Si la API existente todavía no publica `/api/workspaces`, el cliente consulta sus permisos originales y permite entrar a los espacios actuales autorizados. Planificación requiere la API nueva. Esta compatibilidad solo se aplica a HTTP 404; los errores de autenticación, autorización o servidor no habilitan accesos.

## Comportamiento implementado

- Selector de espacios tras iniciar o restaurar sesión, regreso sin cerrar sesión y opciones según permisos actuales.
- Consulta paginada y virtualizada, búsqueda múltiple, filtros, vencimientos, ordenamiento, columnas, selección y CSV con los mismos filtros. No permite marcar ni digitar pruebas.
- Expediente independiente: Buyer, Vendor, Service, fechas, importe, moneda, USD, pruebas, incidencias, historial y procedencia. Correcciones WIP requieren motivo y control de versión; USD manual requiere evidencia verificable.
- Importación XLSX: seleccionar dos archivos, validar, revisar cobertura y cambios, conciliar excepciones, confirmar explícitamente y consultar el resultado e historial.
- Bandejas independientes para Summary sin Report List, pruebas faltantes, Vendor, equivalencias, gestión manual e identidades ambiguas. Retención, resolución y NO PROGRAMAR son decisiones distintas.
- Las pruebas manuales conservan UUID, episodio, origen, clasificación, historia y digitación. Su resolución válida no se reabre al importar otra vez; Vendor sigue pendiente si falta. Una coincidencia posterior se presenta como vínculo manual antes de confirmar. Las repeticiones ambiguas requieren decisión explícita.
- Equivalencias y NO PROGRAMAR persistidos. El catálogo legado de la misma base se importa mediante previsualización y confirmación; los conflictos se excluyen. Administración de permisos de Planificación conserva los permisos de Laboratorio.

## Persistencia y seguridad

La migración incremental `20261116190000_AddPlanningWorkspace` crea el esquema separado `planning`: `revision`, `batches`, `episodes`, `executions`, `source_rows`, `equivalences`, `incidents` e `history`. Conserva archivos, hashes y filas originales, usuario, motivo y cambios. No copia indiscriminadamente tablas operativas ni modifica las tablas actuales del laboratorio. La reversión destructiva se rechaza para proteger el historial.

Confirmación transaccional con bloqueo de revisión, caducidad de previsualización, control de versión e idempotencia. Un error revierte el lote entero. Cada ejecución tiene UUID; JOB + nombre de prueba no es una identidad universal. La ausencia de una fila no elimina ni cancela registros.

Políticas independientes: `planning.view`, `planning.import`, `planning.wip.edit`, `planning.tests.manual`, `planning.equivalences.manage`. El servidor exige sesión vigente y permisos actuales en cada ruta; el administrador utiliza el rol existente. El cliente vuelve a verificar acceso cada 15 segundos y cierra expedientes al cambiar permisos. No hay rutas de marcado o digitación.

## Entorno local utilizado

PostgreSQL 18 en **127.0.0.1:55441 / lims_planning_dev**, restaurado desde el archivo local `backup DB/InterDB.backup` de enero de 2026, sin propietarios ni privilegios del origen y en una transacción. No es una copia actual de producción. La tabla antigua de JOBs estaba vacía; los JOBs consultables de Planificación proceden de los XLSX reales del 13 de noviembre. Se creó únicamente un administrador de aceptación identificado como validación local; no se cambiaron contraseñas existentes.

El respaldo antiguo carecía de `usuarios_permisos`, dependencia externa ya asumida por el host. `scripts/planning/development-legacy-prerequisites.sql` prepara esa tabla vacía exclusivamente en esta restauración y rechaza otra base o puerto. Después se aplicaron las migraciones del host y la nueva migración.

API local: `https://localhost:7441`. Se confió el certificado de desarrollo localhost existente mediante el mecanismo de Windows; no se desactiva la validación TLS. El ejecutable de verificación usa el mismo ensamblado compilado, una copia de salida y la identidad de paquete **Lims.Planning.Validation**, para aislar credenciales y sesión de la aplicación habitual. No es otra implementación del producto. Hash y ruta quedan en `desktop-validation.json`.

No se consultó ni modificó InterDB productiva, Supabase ni `planificacion_migracion_test`. Las credenciales locales se guardan en archivos ignorados de `artifacts/planificacion-dev`; no deben compartirse con los informes.

## Ejecutar y verificar

Desde la raíz, con PostgreSQL 18, SDK .NET 10 y WinAppCLI disponibles:

```powershell
dotnet restore Lims.sln -p:Platform=x64
dotnet build Lims.sln --no-restore -p:Platform=x64 -m:1 -p:BuildInParallel=false
dotnet tool restore
.\scripts\prepare-planning-development.ps1 -BackupPath 'RUTA\InterDB.backup'
.\scripts\migrate-planning-development.ps1
.\scripts\start-planning-development.ps1 -Desktop
.\scripts\test-planning.ps1
.\scripts\test-planning-acceptance.ps1 -SummaryPath 'RUTA\Summary.xlsx' -ReportPath 'RUTA\ReportList.xlsx'
```

Los scripts rechazan destinos distintos del clúster local. Preparación no sobrescribe un clúster existente. Para reconstruir el backend, detener primero solo el PID de API local registrado en `processes.json`; el proceso mantiene cargados sus ensamblados. No detener servicios ajenos. La API HTTPS necesita un certificado localhost confiable. El guion de aceptación guarda una cuenta local de prueba en `acceptance-account.json`; su uso requiere el login original del host.

Las pruebas PostgreSQL crean bases temporales `lims_planning_ci_<guid>` dentro del clúster local y eliminan exclusivamente esas bases verificadas. Sus escenarios son datos sintéticos identificados como pruebas, separados de los JOBs TIPS de desarrollo.

## Evidencia de aceptación — 16 de noviembre de 2026

| Verificación | Resultado y alcance |
| --- | --- |
| Compilación de solución x64, serial | 0 errores y 0 advertencias. |
| Suite general final | 489 aprobadas, 0 fallidas, 88 omitidas. 87 requieren el entorno PostgreSQL propio de módulos anteriores; la aceptación TIPS se ejecutó aparte. |
| Reglas y persistencia de Planificación | 8 pruebas de reglas y 7 pruebas sobre PostgreSQL real, incluidas en la suite. WIP, retención con motivo, prueba manual, reimportación, vínculo posterior, Vendor independiente, conservación de digitación/historia, idempotencia, concurrencia y reversión total verificados con datos sintéticos aislados. |
| Autorización y compatibilidad | 14 casos API y 5 casos de compatibilidad del cliente, incluidos en la suite; escrituras directas rechazadas sin permiso, incluso con un antiguo token administrador. |
| Aceptación autenticada con TIPS explícitos | 1 prueba adicional aprobada; login real del host, JWT/sesión reales, PostgreSQL local restaurado, importación, consulta, expediente, CSV, repetición y cierre de sesión. |
| Datos TIPS persistidos, consulta SQL exitosa | 770 episodios, 8.445 ejecuciones y 2.655 equivalencias; 1 conflicto del catálogo legado excluido. Tres lotes confirmados, 72.282 filas fuente archivadas. Las nuevas cargas no duplicaron episodios ni ejecuciones. |
| Cliente WinUI ejecutándose | Login original, selector, Estándares, Preparaciones, Consulta, expediente independiente, selectores nativos XLSX, revisión y confirmación. La última repetición desde UI confirmó 0 JOBs y 0 pruebas nuevas. |

Archivos TIPS utilizados:

- `a0_-_pro-invoice_summary_13Nov2026104743316.xlsx`: 15.480 filas; SHA-256 `B06B8E1DCED372A01F8B89510EDCA3CA7D7575547CD14CD1EAE739A609B21095`.
- `a0_-_delay_report_list_based_on_due_date_13Nov2026103900894.xlsx`: 8.614 filas; SHA-256 `4E93A2DDCA7D9375E258ADC5C643829E947D68FF158E3DC4BA86246EE9C8AB4F`.

Evidencia local ignorada: `artifacts/planificacion-dev/test-results/*.trx`, `acceptance-api.json`, `acceptance-desktop.json`, `acceptance-desktop-import.json`, `tips-preview.json` y `screenshots/00-*` a `08-*`. Las capturas muestran el ejecutable WinUI real contra la copia aislada. JOB consultado: `GUAT25038091-REV3`, con WIP fuente y 24 pruebas. Estándares y Preparaciones abren; los catálogos vacíos del respaldo histórico no acreditan los saldos actuales de producción.

## Límites conocidos y referencias faltantes

El perfil `lims-tips-observed-v2` corresponde a esos dos XLSX observados. No representa todos los posibles formatos de TIPS. El Report List de muestra no contiene fechas verificadas para acreditar cobertura temporal; la UI lo indica. Una diferencia de cobertura es aviso, no error estructural. Summary conserva los valores originales; sin moneda USD explícita o conversión verificada, USD permanece nulo. Un Close Date externo se archiva y no cierra ni elimina silenciosamente el estado local.

Máximo 5 MiB por archivo; historial de expediente muestra los últimos 1.000 eventos, procedencia las últimas 2.000 filas, bandejas hasta 2.000 incidencias, historial hasta 200 lotes y CSV hasta 50.000 filas por exportación. Las decisiones siguen persistidas fuera de esos límites de visualización. No se certificó desempeño de LAN, hardware objetivo ni 50 usuarios concurrentes.

Se localizaron `Job_Consulta_V_final.html`, los documentos de análisis, `LIMS_2027`, recursos/capturas del host y el importador lateral legado legible. Falta el fuente principal `LIMS(React)/Backend/app/modules/planificacion/router.py` (solo está el `.pyc`) y no se localizaron las dos capturas legadas exactas de revisión antes de agregar y resumen final. El lector se verificó con los XLSX encontrados; no se afirmó recuperar ese código ni esas capturas.

La implementación y aceptación descritas son locales. Quedan fuera de esta entrega el despliegue productivo, migraciones sobre producción y la validación por operadores con sus usuarios y datos actuales.
