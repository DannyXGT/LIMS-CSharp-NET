using System.Net;
using System.Text;
using Lims.Contracts.ReferencePreparations;
using Lims.Desktop.Http;
using Lims.Desktop.ViewModels;

namespace Lims.Desktop.Tests;

public sealed class SolventCompositionTests
{
    private static SolventCompositionViewModel Draft()
    {
        var draft = new SolventCompositionViewModel();
        draft.SetCatalog([new(4, "Metanol", "MeOH", true, 0), new(8, "Agua", "H2O", true, 1), new(3, "Acetonitrilo", "ACN", true, 2)]);
        return draft;
    }
    [Fact]
    public void SingleBinaryAndTernaryMixturesUseTheSameBasisAndRequireExactlyOneHundredPercent()
    {
        foreach (var proportions in new string[][] { ["100"], ["50", "50"], ["30", "20", "50"] })
        {
            var draft = Draft();
            foreach (var value in proportions) { var row = draft.Add(); row.SelectedSolvent = draft.Catalog[draft.Rows.Count - 1]; row.Proportion = value; }
            Assert.True(draft.IsValid); Assert.Equal(100, draft.Total);
            draft.Rows[0].Proportion = "29"; Assert.False(draft.IsValid);
            draft.Rows[0].Proportion = "101"; Assert.False(draft.IsValid);
        }
    }
    [Fact]
    public void DuplicateSolventsAskToUnifyTheirProportionsAndCannotBeSaved()
    {
        var draft = Draft();
        foreach (var percent in new[] { "30", "70" }) { var row = draft.Add(); row.SelectedSolvent = draft.Catalog[0]; row.Proportion = percent; }
        Assert.False(draft.IsValid); Assert.Contains("Unifique", draft.Error, StringComparison.Ordinal);
        draft.Remove(draft.Rows[1]); draft.Rows[0].Proportion = "100"; Assert.True(draft.IsValid);
    }
    [Fact]
    public void MissingOrEmptyPersistentCatalogIsAnExplicitLoadingFailure()
    {
        var draft = Draft(); draft.SetCatalog(null);
        Assert.False(draft.IsReady); Assert.Contains("servidor", draft.CatalogMessage, StringComparison.Ordinal);
        draft.SetCatalog([]); Assert.False(draft.IsReady); Assert.Contains("persistente", draft.CatalogMessage, StringComparison.Ordinal);
    }
    [Fact]
    public async Task OlderCommercialResponseLoadsTheExistingStockCatalogWithoutInventingPersistenceSupport()
    {
        using var handler = new CatalogHandler(false);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        var result = await new CommercialPreparationApiClient(new Factory(client)).OptionsAsync("Stock", null, default);
        Assert.True(result.IsSuccess); Assert.Equal("THF", Assert.Single(result.Value!.Solvents!).Abbreviation);
        Assert.False(result.Value.SupportsSolventComposition);
        Assert.EndsWith("/stock/solvents?search=", handler.Paths[1], StringComparison.Ordinal);
    }
    [Fact]
    public async Task FailedCatalogRequestCannotBeSilentlyConvertedToAnEmptySuccessfulSelector()
    {
        using var handler = new CatalogHandler(true);
        using var client = new HttpClient(handler) { BaseAddress = new Uri("http://fixture.invalid/") };
        var result = await new CommercialPreparationApiClient(new Factory(client)).OptionsAsync("Intermedia", null, default);
        Assert.False(result.IsSuccess); Assert.NotNull(result.Error); Assert.Null(result.Value);
    }
    private sealed class Factory(HttpClient client) : IHttpClientFactory { public HttpClient CreateClient(string name) => client; }
    private sealed class CatalogHandler(bool fail) : HttpMessageHandler
    {
        public List<string> Paths { get; } = [];
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Paths.Add(request.RequestUri!.PathAndQuery);
            var commercial = Paths.Count == 1;
            var json = commercial ? "{\"methods\":[],\"units\":[],\"locations\":[],\"materials\":[]}" : "[{\"id\":1,\"name\":\"Tetrahidrofurano\",\"abbreviation\":\"THF\",\"isActive\":true,\"displayOrder\":0}]";
            return Task.FromResult(new HttpResponseMessage(!commercial && fail ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK)
            { Content = new StringContent(!commercial && fail ? "{}" : json, Encoding.UTF8, "application/json") });
        }
    }
}
