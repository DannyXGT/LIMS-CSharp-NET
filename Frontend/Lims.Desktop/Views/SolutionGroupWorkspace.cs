using Lims.Desktop.Http;
using Lims.Desktop.Services;
using Lims.Desktop.ViewModels;

namespace Lims.Desktop.Views;

public sealed class SolutionGroupWorkspace(ISolutionGroupApiClient api, ISessionService session)
{
    public SolutionGroupPage Curve { get; } = new(new SolutionGroupViewModel("Curva", api, session));
    public SolutionGroupPage Aqs { get; } = new(new SolutionGroupViewModel("AQS", api, session));
}
