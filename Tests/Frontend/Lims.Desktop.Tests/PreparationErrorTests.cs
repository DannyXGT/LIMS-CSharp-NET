using Lims.Contracts.Errors;
using Lims.Desktop.Presentation;

namespace Lims.Desktop.Tests;

public sealed class PreparationErrorTests
{
    [Fact]
    public void SchemaFailureIsDistinguishedFromUnexpectedFailure()
    {
        var error = new ApiError(ErrorCodes.DatabaseSchemaOutOfDate, "Database details", "schema-check-123");
        var message = ReferenceMaterialErrors.PreparationMessage(error);
        Assert.Contains("actualizar el módulo de Preparaciones", message, StringComparison.Ordinal);
        Assert.Contains("Referencia de soporte: schema-check-123", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Database details", message, StringComparison.Ordinal);
    }

    [Fact]
    public void DomainValidationIsPreservedAndMalformedSupportIdIsNotShown()
    {
        var message = ReferenceMaterialErrors.PreparationMessage(new ApiError(ErrorCodes.ValidationError,
            "No hay volumen suficiente.", "invalid\nreference"));
        Assert.Equal("No hay volumen suficiente.", message);
    }
}
