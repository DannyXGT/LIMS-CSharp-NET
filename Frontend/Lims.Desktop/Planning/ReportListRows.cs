using System.Collections.ObjectModel;
using System.Runtime.InteropServices.WindowsRuntime;
using Lims.Contracts.Planning;
using Microsoft.UI.Xaml.Data;
using Windows.Foundation;

namespace Lims.Desktop.Planning;

/// <summary>Bounded server requests, native row virtualization, and revision-checked continuation.</summary>
public sealed class ReportListRows : ObservableCollection<ReportListItem>, ISupportIncrementalLoading
{
    private readonly Func<int,long?,CancellationToken,Task<ReportListPage>> _fetch;
    private readonly CancellationToken _cancellation;
    private bool _busy,_failed;
    private int _page;
    public int Total { get; private set; }
    public long? Revision { get; private set; }
    public ReportListCapabilities? Capabilities { get; private set; }
    public bool HasMoreItems => !_failed && (_page==0 || Count<Total);
    public event Action<Exception?>? Updated;
    public ReportListRows(Func<int,long?,CancellationToken,Task<ReportListPage>> fetch,CancellationToken cancellation)
    { _fetch=fetch;_cancellation=cancellation; }
    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)=>LoadNext().AsAsyncOperation();
    public async Task<LoadMoreItemsResult> LoadNext()
    {
        if(_busy||!HasMoreItems||_cancellation.IsCancellationRequested)return new(){Count=0};
        _busy=true;
        try
        {
            var result=await _fetch(_page+1,Revision,_cancellation);
            _cancellation.ThrowIfCancellationRequested();
            if(_page>0&&result.Revision!=Revision)throw new InvalidOperationException("Los datos cambiaron. Actualice Report List.");
            if(result.Items.Count==0&&Count<result.Total)throw new InvalidOperationException("La consulta cambió. Actualice Report List.");
            Total=result.Total;Revision=result.Revision;Capabilities=result.Capabilities;_page=result.Page;
            foreach(var item in result.Items)Add(item);
            Updated?.Invoke(null);return new(){Count=(uint)result.Items.Count};
        }
        catch(OperationCanceledException){return new(){Count=0};}
        catch(Exception ex){_failed=true;Updated?.Invoke(ex);return new(){Count=0};}
        finally{_busy=false;}
    }
}
