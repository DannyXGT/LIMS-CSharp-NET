# Solvente de aforo y estándar interno en preparaciones

Intermedia permite elegir el solvente de aforo del catálogo existente. Se guarda con la preparación y aparece en su ficha. No se deduce el solvente de las observaciones ni se modifica el cálculo de concentración de la Intermedia.

Cada solución de Curva y AQS permite seleccionar un solvente y, opcionalmente, una Intermedia como estándar interno, con volumen tomado y unidad propios. Se incluyen las Intermedias preparadas y los materiales comerciales que tengan un registro con rol Intermedia. Ambos orígenes deben corresponder al método y tener saldo disponible. Las comparaciones de fechas están temporalmente pausadas según `pausa-validacion-fechas-preparaciones.md`.

El estándar interno comparte el aforo de la solución. La suma de las alícuotas del origen principal y del estándar interno no puede superar ese aforo. Cada concentración conserva su origen, alícuota y resultados; no se suman concentraciones de distintos orígenes. El tubo representa el volumen incorporado: azul para el origen principal y verde para el estándar interno, con una leyenda de código y volumen. La ficha guardada conserva esa representación.

El servidor recalcula y acumula los consumos por botella física, incluso cuando una misma Intermedia se usa en varios puntos o cumple ambos roles. El guardado utiliza la transacción, versiones, movimientos e idempotencia existentes. Los registros históricos sin estos campos siguen siendo legibles y se muestran como solvente no registrado.

## Archivos principales

- `Shared/Lims.Contracts/ReferencePreparations/IntermediateContracts.cs` y `SolutionGroupContracts.cs`: catálogo, selección y resultados trazables.
- `Shared/Lims.Contracts/ReferencePreparations/SolutionGroupCalculator.cs`: aforo compartido y consumos acumulados.
- `Backend/Lims.Infrastructure/Persistence/IntermediateRepository.cs` y `SolutionGroupRepository.cs`: validación de catálogo y persistencia.
- `Frontend/Lims.Desktop/Views/IntermediateEditorDialog.xaml` y `SolutionGroupEditorDialog.xaml`: campos de selección.
- `Frontend/Lims.Desktop/Controls/SolutionGroupVisuals.cs`: tubo y ficha con estándar interno verde.

## Publicación

Se deben actualizar la API y el cliente. La API anuncia `supportsDilutionAdditives` en las opciones; un servidor anterior no ofrece las nuevas selecciones y el cliente muestra el motivo. La selección de solvente es explícita y no se asigna automáticamente.

No se agregó una migración: Intermedia reutiliza `stock_solvents` y el catálogo `reference_solvents`; Curva/AQS conservan el solvente y el estándar interno en el cálculo histórico JSON existente. Se requieren las migraciones previas de solventes y Curva/AQS del proyecto. Esta corrección no aplica SQL ni publica servicios en producción.

## Verificación

Las pruebas cubren alícuotas en mL/L, concentraciones independientes, exceso del aforo combinado, saldo acumulado, origen/método/versión inválidos, clasificación comercial, catálogo inválido, guardado y relectura, e idempotencia. Las pruebas de PostgreSQL utilizan únicamente el clúster aislado de este proyecto; admiten el puerto alterno 55440 cuando 55439 pertenece a otro clúster y verifican su directorio antes de crear esquemas.

Las capturas de WinUI se generan con datos fixture y se guardan en `artifacts/validation/dilution-additives/`. No validan una sesión autenticada ni persistencia en InterDB.
