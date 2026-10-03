using Lims.Contracts.Errors;
using Lims.Desktop.Presentation;

namespace Lims.Desktop.Tests;

public sealed class ReferenceMaterialErrorsTests
{
    [Theory]
    [InlineData(ErrorCodes.InvalidState)]
    [InlineData(ErrorCodes.UnexpectedError)]
    [InlineData("UnexpectedInternalCode")]
    public void TechnicalServerMessagesNeverReachPresentation(string code)
    {
        var error = new ApiError(code, "NULL reference_materials uuid=aaaaaaaa-aaaa-aaaa-aaaa-aaaaaaaaaaaa", "trace-123");
        var message = ReferenceMaterialErrors.Message(error);
        Assert.DoesNotContain("NULL", message, StringComparison.Ordinal);
        Assert.DoesNotContain("reference_materials", message, StringComparison.Ordinal);
        Assert.DoesNotContain("uuid", message, StringComparison.Ordinal);
        Assert.DoesNotContain("trace-123", message, StringComparison.Ordinal);
    }

    [Fact]
    public void ValidationFieldsAreTranslatedWithoutDisplayingColumnNamesOrRawMessages()
    {
        var error = new ApiError(ErrorCodes.ValidationError, "Technical validation error", "trace", new Dictionary<string, string[]>
        {
            ["reason"] = ["Value must contain between 1 and 500 characters (Parameter 'reason')"],
            ["unknown_column"] = ["NULL"],
        });
        var message = ReferenceMaterialErrors.Message(error);
        Assert.Contains("motivo", message, StringComparison.Ordinal);
        Assert.DoesNotContain("unknown_column", message, StringComparison.Ordinal);
        Assert.DoesNotContain("Parameter", message, StringComparison.Ordinal);
        Assert.DoesNotContain("NULL", message, StringComparison.Ordinal);
    }
}
