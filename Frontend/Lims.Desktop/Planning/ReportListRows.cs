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
    private readonly int? _singlePage;
    public int Page => _singlePage??Math.Max(1,_page);
    public int Total { get; private set; }
    public long? Revision { get; private set; }
    public long? DataRevision {get;private set;}
    public long? CatalogueRevision {get;private set;}
    public ReportListCapabilities? Capabilities { get; private set; }
    public bool HasMoreItems => !_failed && (_page==0 || _singlePage is null&&Count<Total);
    public event Action<Exception?>? Updated;
    public ReportListRows(Func<int,long?,CancellationToken,Task<ReportListPage>> fetch,CancellationToken cancellation,int? singlePage=null)
    { _fetch=fetch;_cancellation=cancellation;_singlePage=singlePage; }
    public bool ApplySaved(ReportListItem item,long revision,string view,long? dataRevision=null,long? catalogueRevision=null,bool stableOrder=true)
    {
        var index=Enumerable.Range(0,Count).FirstOrDefault(i=>this[i].Execution.ExecutionId==item.Execution.ExecutionId,-1);
        var consistent=(DataRevision is null||dataRevision is not null&&dataRevision>=DataRevision&&dataRevision<=DataRevision+1&&CatalogueRevision==catalogueRevision)&&(_singlePage is not null||Count>=Total||stableOrder);
        Revision=revision;DataRevision=dataRevision;CatalogueRevision=catalogueRevision;_failed=!consistent;
        if(index<0)return consistent;
        if(view=="Pending"&&item.Execution.Complete||view=="Checked"&&!item.Execution.Complete)
        {RemoveAt(index);Total=Math.Max(Count,Total-1);}
        else this[index]=item;
        Updated?.Invoke(null);return consistent;
    }
    public IAsyncOperation<LoadMoreItemsResult> LoadMoreItemsAsync(uint count)=>LoadNext().AsAsyncOperation();
    public async Task<LoadMoreItemsResult> LoadNext()
    {
        if(_busy||!HasMoreItems||_cancellation.IsCancellationRequested)return new(){Count=0};
        _busy=true;
        try
        {
            var result=await _fetch(_singlePage??_page+1,Revision,_cancellation);
            _cancellation.ThrowIfCancellationRequested();
            if(_page>0&&result.Revision!=Revision)throw new InvalidOperationException("Los datos cambiaron. Actualice Report List.");
            if(result.Items.Count==0&&Count<result.Total)throw new InvalidOperationException("La consulta cambió. Actualice Report List.");
            Total=result.Total;Revision=result.Revision;DataRevision=result.DataRevision;CatalogueRevision=result.CatalogueRevision;Capabilities=result.Capabilities;_page=result.Page;
            for(var i=0;i<result.Items.Count;i++)
            {
                _cancellation.ThrowIfCancellationRequested();Add(result.Items[i]);
                // Let the UI process input and recycling while a large page arrives.
                if(i%32==31)await Task.Yield();
            }
            Updated?.Invoke(null);return new(){Count=(uint)result.Items.Count};
        }
        catch(OperationCanceledException){return new(){Count=0};}
        catch(Exception ex){_failed=true;Updated?.Invoke(ex);return new(){Count=0};}
        finally{_busy=false;}
    }
}
