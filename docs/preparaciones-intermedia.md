# Preparaciones → Intermedia

Una ventana permite preparar una mezcla desde varios Stock o diluir una Intermedia
previa completa. Los selectores buscan localmente por código, nombre, CAS y lote;
el método procede de `reference_methods`. Cambiar el método conserva las selecciones
y exige corregir las incompatibilidades. La expiración se elige manualmente.

## Reglas confirmadas en el código legacy

Consulta limitada a `Trazalab/view/form_ingreso_intermedio.py`:

- `_on_combo_intermedio` y `_rebuild_tube`: Solución a Diluir selecciona una Intermedia
  previa y aplica la dilución a todos sus analitos; no duplica solo los datos del formulario.
- `_compute_group_usage` y `guardar_ingreso`: Volumen Tomado es una alícuota común
  aplicada a cada Stock seleccionado. En modo nuevo, total = alícuota × número de Stock.
  La nueva interfaz permite registrar ese volumen por componente y deriva su suma.
- `_update_row_status`: la barra es saldo simulado después del consumo / volumen
  inicial del origen. En dilución previa corresponde al grupo Intermedia, no a cada analito.
- `_load_stock_cache`: los orígenes directos son Stock; no estándares líquidos ni otros tipos.
- `_rebuild_tube`: C₂ de cada analito = C₁ × volumen tomado / volumen de aforo.
  No existe una concentración global que sume sustancias distintas.

Una Intermedia previa se consume una sola vez como solución física y conserva todos
sus analitos. En mezclas, los resultados se mantienen separados por identidad química;
si un mismo CAS aporta desde varios Stock, sus contribuciones se acumulan solo para
ese analito. Sin CAS se conserva la identidad del material, sin equiparar nombres.

## Persistencia y cálculo

`reference_preparations` contiene Stock e Intermedia, método, volumen final y saldo,
fechas de negocio, preparador, auditoría, versión, resultados históricos por analito
y clave de idempotencia. `reference_preparation_components` contiene origen FK,
tipo, volumen/unidad, posición, versión y snapshot completo del origen y sus analitos.

El ledger existente `reference_material_movements` registra un consumo por componente
con origen, destino, actor, fecha, cantidad, unidad y saldos antes/después. La creación,
componentes, movimientos y saldos se confirman juntos. Las versiones optimistas
impiden sobreconsumo concurrente. Los reintentos recargan los datos; la misma solicitud
no vuelve a debitar. Un trigger de integridad conserva la vinculación del consumo Stock
con su pesada y valida la correspondencia entre componente y consumo de volumen.

Se reutiliza `StockUnits` para L/mL y unidades de concentración; los cálculos usan Decimal
sin redondeo previo. Se rechaza un débito que el saldo de seis decimales no pueda representar.
El backend valida y recalcula dentro de la transacción; Desktop previsualiza localmente.
El código INT-AAAAMMDD-NNN usa secuencia global e índice único, igual que Stock.

La probeta es vectorial, tiene cinco graduaciones, representa suma/aforo y anima su nivel
en 240 ms. Conserva el porcentaje real si supera 100 %, limita solo el dibujo y usa Danger.
Respeta la preferencia de movimiento reducido. El resultado pasa debajo en ventanas estrechas.

## Migración y validación

`20261007230730_AddIntermediatePreparations` y `artifacts/sql/reference_intermediate.sql`
amplían el modelo y recuperan volumen/método de los Stock existentes. El método se
resuelve desde su nombre histórico; un nombre sin correspondencia queda sin asignar
y ese Stock no se ofrece hasta reconciliarlo. No se ejecuta la migración en InterDB.

```powershell
dotnet build Lims.sln --no-restore -c Debug -p:Platform=x64 -m:1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/test-stock.ps1 -SkipBuild
dotnet build Tests/Frontend/Lims.Desktop.VisualHarness/Lims.Desktop.VisualHarness.csproj --no-restore -c Debug -p:Platform=x64 -m:1
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-intermediate.ps1 -Action Start
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-intermediate.ps1 -Action Scenarios
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-intermediate.ps1 -Action Finish
powershell -NoProfile -ExecutionPolicy Bypass -File scripts/validate-intermediate.ps1 -Action Responsive
git diff --check
```

Las pruebas PostgreSQL usan exclusivamente 127.0.0.1:55439/lims_stock_test.
Las capturas usan las vistas WinUI reales con fuentes y persistencia ficticias;
no constituyen una prueba de guardado en producción ni una validación de InterDB.
