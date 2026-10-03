using Lims.Contracts.Errors;

namespace Lims.Desktop.Presentation;

internal static class ReferenceMaterialErrors
{
    public static string Message(ApiError? error) => error?.Code switch
    {
        ErrorCodes.Forbidden => "No tiene autorización para realizar esta operación.",
        ErrorCodes.NotFound => "El estándar solicitado ya no existe.",
        ErrorCodes.Conflict => "El estándar cambió. Actualice la lista e intente nuevamente.",
        ErrorCodes.InvalidState => "El estado actual del estándar no permite esta acción. Actualice los datos.",
        ErrorCodes.DatabaseSchemaOutOfDate => "El servicio necesita una actualización. Contacte al administrador.",
        ErrorCodes.ServerUnavailable => "El servidor no está disponible.",
        ErrorCodes.ValidationError => ValidationMessage(error),
        _ => "No se pudo completar la operación. Intente nuevamente o contacte al administrador.",
    };

    private static string ValidationMessage(ApiError error)
    {
        var messages = error.ValidationErrors?.Keys.Select(key => key switch
        {
            "methodId" => "El método seleccionado ya no está disponible. Actualice los catálogos e intente nuevamente.",
            "unitId" => "La unidad seleccionada ya no está disponible. Actualice los catálogos e intente nuevamente.",
            "locationId" => "La ubicación seleccionada ya no está disponible. Actualice los catálogos e intente nuevamente.",
            "name" => "Revise el nombre del estándar.",
            "casNumber" => "Revise el formato y dígito de control del CAS.",
            "catalogNumber" => "Revise el número de catálogo.",
            "purityPercent" => "Revise la pureza del estándar.",
            "lot" => "Revise el lote del estándar.",
            "brand" => "Revise la marca del estándar.",
            "receivedDate" or "expirationDate" => "Revise las fechas de ingreso y expiración.",
            "presentationQuantity" => "Revise la cantidad por unidad.",
            "packageCount" => "Revise el número de unidades.",
            "storageTemperature" => "Revise la temperatura de almacenamiento.",
            "reason" => "Indique un motivo válido para esta acción.",
            _ => "Revise los datos indicados e intente nuevamente.",
        }).Distinct();
        return messages is null || !messages.Any()
            ? "Revise los datos indicados e intente nuevamente."
            : string.Join(" ", messages);
    }
}
