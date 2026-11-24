namespace Lims.Desktop.Planning;

public sealed partial class PlanningReportListView
{
    private bool _batchDialogActive;
    private ReportFixedTimeBatchDialog? _batchDialog;
    private async Task OpenFixedTimeBatch()
    {
        if(_mutating||!Has(Lims.Contracts.Planning.PlanningPermissions.ReportMark))return;
        _batchDialogActive=true;_mutating=true;_grid.SetBusy(true);UpdateSelection();var savedBatch=false;
        try
        {
            _batchDialog=new(_api,XamlRoot,_fixedClock.ForScan(DateTimeOffset.UtcNow),_administrator(),_grid.Selected);
            await _batchDialog.ShowAsync();
            if(_batchDialog.Saved is{} saved)
            {
                savedBatch=true;
                _grid.ClearSelection();Feedback($"{saved.ExecutionIds.Count:N0} pruebas digitadas con la misma hora. Horario pendiente de chequeo."+(saved.ReadyJobs.Count>0?$" {saved.ReadyJobs.Count:N0} JOBs disponibles en Cierres.":""));
                if(Changed is{} changed)await changed();
            }
        }
        finally{_batchDialog=null;_batchDialogActive=false;_mutating=false;_grid.SetBusy(false);UpdateSelection();UpdateScanner();if(savedBatch&&!_disposed&&!_clearing)await LoadPage(_listPage,true);await DrainScans();}
    }
}
