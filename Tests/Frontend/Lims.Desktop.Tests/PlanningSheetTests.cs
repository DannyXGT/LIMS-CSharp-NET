using Lims.Contracts.Planning;
using Lims.Desktop.Planning;

namespace Lims.Desktop.Tests;

public sealed class PlanningSheetTests
{
    private static PlanningSheetModel Sheet(int count=12)
    {
        var model=new PlanningSheetModel();
        for(int i=0;i<count;i++)model.Rows.Add(new(Guid.NewGuid(),Guid.NewGuid(),"JOB"+i,new(2026,11,16),new(2026,11,20),"Prueba "+i,null,"Físico","Regular",null,null,"Sin información",null,null,"ReportList","Buyer",null,false));
        return model;
    }
    [Fact]
    public void RectangularSelectionCopiesExactlyFiveRowsAndFourColumns()
    {var m=Sheet();m.Select(1,0);m.Select(5,3,true);var text=m.Copy();var lines=text.Split("\r\n");Assert.Equal(5,lines.Length);Assert.All(lines,line=>Assert.Equal(4,line.Split('\t').Length));Assert.Equal("JOB1\t2026-11-16\t2026-11-20\tPrueba 1",lines[0]);Assert.Equal(new SheetCell(5,3),m.Active);Assert.Equal(new SheetCell(1,0),m.Anchor);}
    [Fact]
    public void SingleCellAndWholeLoadedColumnPreserveBounds()
    {var m=Sheet();m.Select(2,3);Assert.Equal("Prueba 2",m.Copy());m.SelectColumn(3);Assert.Equal(12,m.Copy().Split("\r\n").Length);Assert.All(m.Copy().Split("\r\n"),line=>Assert.DoesNotContain('\t',line));m.Select(1000,1000,true);Assert.Equal(11,m.Active.Row);Assert.Equal(m.Columns.Count-1,m.Active.Column);}
    [Fact]
    public void TsvSupportsExcelQuotesEmptyCellsAndMultilineObservations()
    {var rows=PlanningSheetModel.ParseTsv("Ana\t\"Texto con\r\nsegunda línea y \"\"comillas\"\"\"\r\nLuis\t\r\n");Assert.Equal(2,rows.Count);Assert.Equal("Texto con\nsegunda línea y \"comillas\"",rows[0][1]);Assert.Equal("",rows[1][1]);}
    [Fact]
    public void PastePositionsDoNotShiftAroundProtectedColumnsAndAreExplicitlyCounted()
    {var m=Sheet();m.Select(0,9);var p=m.Preview("Protegido\tAna\tObservación",true);Assert.Equal(2,p.Valid);Assert.Equal(1,p.Skipped);Assert.Equal(0,p.Invalid);Assert.Equal("Responsible",p.Cells[1].Change!.Column);Assert.Equal("Observation",p.Cells[2].Change!.Column);Assert.All(m.Rows,r=>Assert.Null(r.Responsible));}
    [Fact]
    public void TenCellPasteValidatesBeforeApplyingAndKeepsSelection()
    {var m=Sheet();m.Select(0,10);var p=m.Preview(string.Join('\n',Enumerable.Range(0,10).Select(i=>"Persona "+i)),true);Assert.Equal(10,p.Valid);m.Apply(p.Cells.Select(c=>new PlanningNoteSaved(c.Change!.ExecutionId,c.After,null,1)).ToArray());Assert.Equal(new SheetCell(0,10),m.Active);Assert.Equal(10,m.Rows.Count(r=>r.Responsible is not null));Assert.All(m.Rows.Take(10),r=>Assert.Equal(1,r.NoteVersion));}
    [Fact]
    public void InvalidNoPermissionAndWipOnlyRowsCannotBecomeWrites()
    {var m=Sheet(1);m.Select(0,10);Assert.Equal(1,m.Preview(new string('x',151),true).Invalid);Assert.Equal(0,m.Preview("Nombre",false).Valid);m.Rows[0]=m.Rows[0]with{ExecutionId=null};Assert.Equal(0,m.Preview("Nombre",true).Valid);}
    [Fact]
    public void OversizedAndMalformedClipboardFailBeforeAllocationOfEdits()
    {Assert.Throws<FormatException>(()=>PlanningSheetModel.ParseTsv(string.Join('\t',Enumerable.Repeat("x",2001))));Assert.Throws<FormatException>(()=>PlanningSheetModel.ParseTsv("\"sin cierre"));}
    [Fact]
    public void StatusCopyDistinguishesUnknownFromPendingAndNeverAssertsOfficialClosure()
    {var m=Sheet(2);m.Rows[1]=m.Rows[1]with{DigitizedAt=new DateTimeOffset(2026,11,16,14,0,0,TimeSpan.Zero),DigitizationState="Digitado"};var d=new JobDossier(m.Rows[0].EpisodeId,"JOB0",new("Cliente real conocido",null,null,new(2026,11,16),new(2026,11,20),null,null,null),0,m.Rows,[],[],[]);var text=PlanningStatusText.Teams(d);Assert.Contains("Digitadas: 1",text);Assert.Contains("Pendientes: 0",text);Assert.Contains("Sin información: 1",text);Assert.Contains("Estado oficial: No verificado",text);Assert.Contains("Prueba 0",text);}
}
