# Dominio inicial

`ReferenceMaterial` es el agregado raíz de Estándares.

Campos persistidos:

- nombre, CAS opcional y catálogo opcional;
- método, pureza, lote y marca;
- fechas de ingreso y expiración como `date`/`DateOnly`;
- presentación por unidad, unidad y número de unidades;
- condiciones y ubicación física de almacenamiento;
- cantidad disponible real, inicializada desde `presentación × unidades` y persistida en la unidad del material;
- estado tipado;
- actores y timestamps de creación, actualización y archivado;
- motivo de archivado;
- token `Version` para concurrencia optimista.

Reglas implementadas:

- nombre, método, lote, marca, condiciones y ubicación son obligatorios;
- pureza `> 0` y `<= 100`;
- presentación y número de unidades mayores que cero;
- expiración no anterior al ingreso;
- CAS opcional, pero si existe debe tener formato y dígito de control válidos;
- no se puede editar ni archivar de nuevo un registro archivado, reemplazado o retirado;
- archivado conserva el registro, actor, fecha y motivo;
- no hay `DELETE` operativo;
- total de presentación se deriva de `presentación × unidades`.
- disponibilidad nunca puede ser negativa ni superar el total inicial;
- reemplazo explícito crea el sucesor y enlaza `ReplacedByMaterialId` en la misma unidad atómica;
- vencido y agotado se presentan como estados efectivos para impedir selección futura en Stock.

Estados iniciales: `Active`, `Depleted`, `Expired`, `Blocked`, `Replaced`, `Archived`, `Retired`.

Las cantidades usan `decimal`; EF persiste pureza como `numeric(7,4)` y presentación/disponibilidad como `numeric(18,6)`. Antes de existir consumos de Stock, el saldo real de un alta es su total inicial. La vertical de Stock deberá descontarlo en la misma transacción que crea la preparación.
