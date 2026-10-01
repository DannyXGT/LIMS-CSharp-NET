# Dominio de Estándares

`ReferenceMaterial` representa registros nuevos del LIMS. Exige:

- método, unidad y ubicación activos;
- temperatura de almacenamiento como texto no vacío;
- pureza mayor que 0 y menor o igual que 100;
- presentación y número de envases positivos;
- expiración no anterior a recepción;
- creador y último modificador reales de `usuarios`;
- archivado/reemplazo lógico y versión de concurrencia.

No existe importador legacy ni `legacy_id`. La disponibilidad inicial es presentación por envase multiplicada por el número de envases. El porcentaje restante es derivado.

El reemplazo automático por CAS/catálogo permanece deshabilitado hasta confirmar la política del laboratorio; el reemplazo explícito conserva el vínculo y la auditoría.
