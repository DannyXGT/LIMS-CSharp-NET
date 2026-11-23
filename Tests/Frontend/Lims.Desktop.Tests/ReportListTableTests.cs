using Lims.Contracts.Planning;
using Lims.Desktop.Planning;

namespace Lims.Desktop.Tests;

public sealed class ReportListTableTests
{
    private static readonly string[] DefaultKeys=["InDate","DueDate","Job","Test","Laboratory","Buyer","Time","Date","VerifiedBy","Selection"];
    private static ReportListItem Row(string state="Sin información",string? equivalence="PH")=>new(new(Guid.NewGuid(),Guid.NewGuid(),"JOB repetido",new(2026,11,1),new(2026,11,20),"Original PH",equivalence,"Chemical",null,null,null,state,null,null,"ReportList","Cliente\tcon tab",null,false),0,false,null,"manual");
    [Fact]
    public void RectangularCopyPreservesDatesOriginalIdentityAndInformedState()
    {
        var first=Row();var second=Row("Pendiente");var model=new ReportListTableModel{Rows=new[]{first,second}};
        model.Select(0,0);model.Select(1,3,true);
        Assert.Equal("2026-11-01\t2026-11-20\tJOB repetido\tPH\r\n2026-11-01\t2026-11-20\tJOB repetido\tPH",model.Copy());
        Assert.NotEqual(first.Execution.ExecutionId,second.Execution.ExecutionId);
        Assert.Equal("Sin información",ReportListTableModel.Value(first,"Checked"));Assert.Equal("Pendiente",ReportListTableModel.Value(second,"Checked"));
        model.Select(0,5);Assert.Equal("\"Cliente\tcon tab\"",model.Copy());
    }
    [Fact]
    public void MissingEquivalenceKeepsOriginalAccessibleAndDoesNotPretendItIsVerified()
    {
        var row=Row(equivalence:null);Assert.Equal("Original PH · Equivalencia pendiente",ReportListTableModel.Value(row,"Test"));
        Assert.Null(row.Execution.Equivalence);Assert.Equal("Original PH",ReportListTableModel.Value(row,"OriginalTest"));
    }
    [Fact]
    public void UserLayoutsRestoreOrderAndWidthsAndRecoverRequiredDatesFromStaleOrCorruptPreferences()
    {
        var a=new ReportListTableModel();var c=a.Columns[2];a.Columns.RemoveAt(2);a.Columns.Insert(0,c with{Width=333});
        var b=new ReportListTableModel();b.LoadLayout(a.SaveLayout());Assert.Equal("Job",b.Columns[0].Key);Assert.Equal(333,b.Columns[0].Width);
        b.LoadLayout("[{\"Key\":\"Job\",\"Width\":99999},{\"Key\":\"Job\",\"Width\":1},{\"Key\":\"Removed\",\"Width\":100}]");
        Assert.Single(b.Columns,c=>c.Key=="Job");Assert.Contains(b.Columns,c=>c.Key=="InDate");Assert.Contains(b.Columns,c=>c.Key=="DueDate");Assert.Equal(700,b.Columns.Single(c=>c.Key=="Job").Width);
        b.LoadLayout("broken");Assert.Equal(DefaultKeys,b.Columns.Select(c=>c.Key));
        Assert.DoesNotContain(b.Columns,c=>c.Key=="ExecutionId");
        Assert.DoesNotContain(b.Columns,c=>c.Key=="Checked");
        Assert.All(ReportListTableModel.Required,key=>Assert.Contains(b.Columns,c=>c.Key==key));
    }
    [Fact]
    public void LegacyPreferencesHideFormerDefaultStateButExplicitNewPreferencesKeepIt()
    {
        var model=new ReportListTableModel();model.LoadLayout("[{\"Key\":\"Job\",\"Width\":220},{\"Key\":\"Checked\",\"Width\":155}]");
        Assert.DoesNotContain(model.Columns,c=>c.Key=="Checked");Assert.Equal(220,model.Columns.Single(c=>c.Key=="Job").Width);
        model.Columns.Add(ReportListTableModel.Available.Single(c=>c.Key=="Checked"));var next=new ReportListTableModel();next.LoadLayout(model.SaveLayout());Assert.Contains(next.Columns,c=>c.Key=="Checked");
    }
    [Fact]
    public void TemporaryMarkCannotMasqueradeAsPersistedDigitizationAndCompletedRowsCannotBeMarkedAgain()
    {
        var pending=Row();Assert.True(ReportListTableModel.CanMark(pending));Assert.Equal("Sin información",ReportListTableModel.CheckDescription(pending,false));Assert.Equal("Marcada sin guardar",ReportListTableModel.CheckDescription(pending,true));
        var saved=pending with{Execution=pending.Execution with{DigitizedAt=DateTimeOffset.UtcNow,TestedOn=new(2026,11,20),DigitizationState="Digitado"}};
        Assert.False(ReportListTableModel.CanMark(saved));Assert.Equal("Digitación guardada",ReportListTableModel.CheckDescription(saved,false));
        Assert.False(ReportListTableModel.CanMark(pending with{Execution=pending.Execution with{OnHold=true}}));
        Assert.False(ReportListTableModel.CanMark(pending with{Execution=pending.Execution with{Equivalence=null}}));
        Assert.NotEqual(pending.Execution.ExecutionId,Row().Execution.ExecutionId);
    }
}
