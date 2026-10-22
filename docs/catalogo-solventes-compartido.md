# Catálogo compartido y composición de solventes

Stock preparado, Stock comercial e Intermedia comercial reutilizan `SolventCompositionEditor`, `SolventCompositionViewModel`, `StockSolventRow` y la validación existente `StockSolventComposition`. La base sigue siendo `CompositionPercent`: porcentajes de composición, sin atribuir una base de masa o volumen que no esté documentada.

## Selector y catálogo

El código anterior convertía un catálogo comercial ausente en una lista vacía con `options.Solvents ?? []`. Esto ocultaba la diferencia entre una respuesta antigua/incompleta del servidor y un catálogo persistente vacío. El selector podía abrirse y mostrar «Sin resultados» incluso sin escribir una búsqueda.

La API comercial consulta ahora el mismo `StockRepository.SolventsAsync` que Stock preparado, sobre `reference_solvents`. Si una API antigua omite el catálogo, el cliente consulta el endpoint de Stock existente. Una respuesta fallida o un catálogo vacío se informa explícitamente y permite reintentar; no se sustituyen por solventes definidos en la interfaz. La API declara además si admite persistir la composición: el cliente bloquea los registros nuevos cuando el servidor no confirma esa capacidad, para evitar un guardado que descarte las proporciones.

El catálogo obligatorio conserva los doce nombres y abreviaturas solicitados. Se corrige la abreviatura anterior «Cyclohexano» a «Ciclohexano» sin cambiar su identidad.

## Persistencia e historial

Se amplía la tabla existente `stock_solvents` con `material_id` opcional y `preparation_id` nullable. Una restricción exige exactamente un propietario: preparación o material comercial. Se mantienen los porcentajes `numeric(7,4)`, sus posiciones y las referencias al mismo catálogo. Los índices impiden repetir un solvente o una posición dentro del mismo producto.

Los registros comerciales nuevos requieren composición válida, solventes activos y suma exacta de 100 %. Los duplicados muestran «Unifique su proporción en una sola fila». La ficha comercial vuelve a consultar las filas persistidas y sus nombres desde el catálogo.

No se completan ni invalidan retrospectivamente registros históricos sin composición. Su consulta y asociación conservan identidad, versión, inventario y certificado. Las propiedades nominales y los datos anteriores permanecen disponibles; las altas nuevas de Preparaciones utilizan la concentración certificada.

## Limpieza de formularios

- «Número de catálogo» se conserva y permite guardar sin completarlo.
- Se retiran del formulario comercial la concentración nominal alternativa y el selector de concentración utilizada.
- «Registrado por» es texto de solo lectura procedente de la sesión; el servicio registra el usuario autenticado.
- Se retira el selector escalar opcional de matriz. La composición requerida se captura con el editor compartido, con agregar/quitar filas, búsqueda, suma y barra.
- No se añaden apartados avanzados ni se modifican Dashboard, permisos, Sidebar, Curva o AQS.

## Carga y migración preparadas

[Carga idempotente del catálogo](../artifacts/sql/ensure_reference_solvents.sql): encuentra las identidades existentes por nombre/abreviatura, mantiene sus IDs y los registros adicionales, reactiva los doce solventes requeridos e inserta únicamente los ausentes. Se comprobó ejecutándola dos veces en PostgreSQL aislado.

[Migración idempotente de composición comercial](../artifacts/sql/commercial_solvent_composition.sql): corresponde a `20261020045452_AddCommercialSolventComposition`. La reversión se cancela si eliminaría composiciones comerciales.

Estos scripts están preparados, **no se ejecutaron en InterDB**. El despliegue de la API y esta migración son necesarios para habilitar el guardado de proporciones en el servidor real. La comprobación remota sin autenticación confirmó `/health/live` = 200 y ambas rutas de catálogo/opciones = 401; no permite certificar el contenido actual del catálogo de producción.

## Evidencia

La comprobación visual usa los controles reales de WinUI y PostgreSQL local aislado en `127.0.0.1:55439 / lims_stock_test`, con un esquema de prueba independiente. En este modo, el catálogo y los registros comerciales se consultan/guardan mediante los repositorios y servicios de producción. No son listas de solventes simuladas en el frontend. La evidencia no acredita un despliegue ni escrituras en producción.

- Compilación Desktop y harness: sin errores ni advertencias.
- 46 pruebas de PostgreSQL y 35 pruebas de cliente superadas.
- Catálogo de doce solventes en los tres editores; búsqueda nativa «THF» y «Tetrahidrofurano» sobre la misma identidad.
- Composiciones 100 %, 50/50 y 30/20/50; rechazo de total incorrecto, solvente repetido, solvente inexistente y base diferente.
- Guardado comercial y recarga con un contexto nuevo de PostgreSQL, conservando IDs, proporciones y orden; consulta de registros históricos sin exigir composición retrospectiva.

[Selector cargado](../artifacts/validation/shared-solvents/solvent-selector-cargado.png) · [Mezcla configurada](../artifacts/validation/shared-solvents/solvent-mezcla-30-20-50.png) · [Búsqueda THF](../artifacts/validation/shared-solvents/solvent-busqueda-thf.png) · [Búsqueda por nombre](../artifacts/validation/shared-solvents/solvent-busqueda-nombre.png)

![Selector con catálogo persistente](../artifacts/validation/shared-solvents/solvent-selector-cargado.png)

![Mezcla 30/20/50](../artifacts/validation/shared-solvents/solvent-mezcla-30-20-50.png)

[Ficha Stock recargada](../artifacts/validation/shared-solvents/stock-comercial-ficha-recargada-30-20-50.png) · [Intermedia configurada](../artifacts/validation/shared-solvents/intermedia-comercial-mezcla-30-20-50.png) · [Ficha Intermedia recargada](../artifacts/validation/shared-solvents/intermedia-comercial-ficha-recargada.png) · [Resultado visual](../artifacts/validation/shared-solvents/visual-validation.json)

[Pruebas PostgreSQL](../artifacts/validation/shared-solvents/postgres-tests.log) · [Pruebas cliente](../artifacts/validation/solvents-frontend-tests.log) · [Compilación Desktop](../artifacts/validation/solvents-desktop-build.log)

Repetir pruebas: `powershell -ExecutionPolicy Bypass -File scripts/test-commercial-solvents.ps1`. Repetir capturas y guardados: `powershell -ExecutionPolicy Bypass -File scripts/validate-shared-solvents.ps1 -SkipBuild`, después de compilar el harness. Requiere la sesión interactiva de Windows y conserva únicamente datos de prueba en PostgreSQL aislado.
