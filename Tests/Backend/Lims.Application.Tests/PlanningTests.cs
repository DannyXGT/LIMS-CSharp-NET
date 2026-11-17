using System.Globalization;
using System.IO.Compression;
using System.Text;
using Lims.Application.Planning;
using Lims.Contracts.Planning;

namespace Lims.Application.Tests;

public sealed class PlanningTests
{
    [Fact]
    public void UnknownCurrencyNeverBecomesZeroUsdAndBuyerIsNotVendor()
    {
        var input=PlanningImportReader.Read("summary.xlsx",Workbook(["Report #","Reg Date","Delivery Date","Buyer","Vendor","PI Total Amt"],[["J1","2026-11-16","2026-11-20","BUYER","VENDOR","120.50"]]),
            "report.xlsx",Workbook(["Report_no","Test_Name"],[["J1","AZO"]]),new("Noon",new(2026,11,16)));
        Assert.DoesNotContain(input.Issues,i=>i.Blocking);var w=Assert.Single(input.Rows,r=>r.Wip is not null).Wip!;
        Assert.Equal("BUYER",w.Buyer);Assert.Equal("VENDOR",w.Vendor);Assert.Null(w.Usd);Assert.Null(w.Currency);Assert.Equal(120.50m,w.Amount);
    }
    [Fact]
    public void ValidManualResolutionDoesNotReopenMissingTestsOrHideMissingVendor()
    {
        var id=Guid.NewGuid();var w=Wip();var previous=new StoredEpisode(id,"J1",w,2,[],w.InDate);
        var test=new StoredExecution(Guid.NewGuid(),id,"AZO","AZO","Químico","Manual");
        var plan=PlanningReconciler.Plan(Input([new("Summary","Sheet",2,"J1",null,w,"{}")]),[previous],[test],[]);
        Assert.DoesNotContain(plan.Reviews,r=>r.Category=="SummaryWithoutReport");Assert.Contains(plan.Reviews,r=>r.Category=="ManualManaged");Assert.Contains(plan.Reviews,r=>r.Category=="MissingVendor");
    }
    [Fact]
    public void SingleManualMatchProposesLinkAndKeepsExistingUuidAndClassification()
    {
        var id=Guid.NewGuid();var test=new StoredExecution(Guid.NewGuid(),id,"AZO","Manual AZO","Lab manual","Manual");var w=Wip();
        var plan=PlanningReconciler.Plan(Input([new("Summary","S",2,"J1",null,w,"{}"),new("Report List","R",2,"J1","azo",null,"{}")]),[new(id,"J1",w,0,[],w.InDate)],[test],[]);
        var execution=Assert.Single(plan.Executions);Assert.Equal(test.Id,execution.Id);Assert.Equal("LinkManual",execution.Action);Assert.Equal("Manual AZO",execution.Equivalence);
    }
    [Fact]
    public void RepeatedSourceRowsRequireExplicitExecutionIdentity()
    {
        var w=Wip();var plan=PlanningReconciler.Plan(Input([new("Summary","S",2,"J1",null,w,"{}"),new("Report List","R",2,"J1","AZO",null,"{}"),new("Report List","R",3,"J1","AZO",null,"{}")]),[],[],[]);
        Assert.Empty(plan.Executions);Assert.Equal(2,plan.Reviews.Count(r=>r.Category=="ExecutionIdentity"));Assert.Single(plan.Episodes);
    }
    [Fact]
    public void SeveralExistingExecutionsAreNotMergedByJobAndName()
    {
        var id=Guid.NewGuid();var w=Wip();var plan=PlanningReconciler.Plan(Input([new("Summary","S",2,"J1",null,w,"{}"),new("Report List","R",2,"J1","AZO",null,"{}")]),
            [new(id,"J1",w,0,[],w.InDate)],[new(Guid.NewGuid(),id,"AZO","AZO","Q","Manual"),new(Guid.NewGuid(),id,"AZO","AZO","Q","ReportList")],[]);
        Assert.Empty(plan.Executions);Assert.Contains(plan.Reviews,r=>r.Category=="ExecutionIdentity");
    }
    [Fact]
    public void DifferentRegistrationCreatesSeparateEpisodeWithoutMovingManualTests()
    {
        var previous=Guid.NewGuid();var w=Wip();var plan=PlanningReconciler.Plan(Input([new("Summary","S",2,"J1",null,w,"{}")]),[new(previous,"J1",w with{InDate=new(2025,1,1)},0,[],new(2025,1,1))],[],[]);
        Assert.NotEqual(previous,Assert.Single(plan.Episodes).Id);Assert.Contains(plan.Reviews,r=>r.Category=="Episode");
    }
    [Fact]
    public void ExternalCloseArchivesSourceWithoutInventingLocalClosure()
    {
        var w=Wip();var plan=PlanningReconciler.Plan(Input([new("Summary","S",2,"J1",null,w,"{\"Close Date\":\"2026-11-16\"}")]),[],[],[]);
        Assert.Empty(plan.Episodes);Assert.Empty(plan.Executions);
    }
    [Fact]
    public void NoProgramDecisionIsClassificationAndDoesNotDeleteTest()
    {
        var w=Wip();var plan=PlanningReconciler.Plan(Input([new("Summary","S",2,"J1",null,w,"{}"),new("Report List","R",2,"J1","AZO",null,"{}")]),[],[],[new("AZO",null,null,true,0)]);
        Assert.True(Assert.Single(plan.Executions).NoProgram);Assert.DoesNotContain(plan.Reviews,r=>r.Category=="MissingEquivalence");
    }
    public static WipValues Wip()=>new("Buyer",null,"Regular",new(2026,11,16),new(2026,11,20),null,null,null);
    public static PlanningImportInput Input(IReadOnlyList<PlanningSourceRow> rows,string? hash=null)=>new(new("Noon",new(2026,11,16)),[1],[2],"summary.xlsx","report.xlsx",hash??Guid.NewGuid().ToString("N"),[],rows,[]);
    public static byte[] Workbook(string[] headers,string[][] rows)
    {
        using var stream=new MemoryStream();
        using(var zip=new ZipArchive(stream,ZipArchiveMode.Create,true))
        {
            void Part(string path,string text){using var writer=new StreamWriter(zip.CreateEntry(path).Open(),new UTF8Encoding(false));writer.Write(text);}
            Part("xl/workbook.xml","<workbook xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main' xmlns:r='http://schemas.openxmlformats.org/officeDocument/2006/relationships'><sheets><sheet name='Data' sheetId='1' r:id='r1'/></sheets></workbook>");
            Part("xl/_rels/workbook.xml.rels","<Relationships xmlns='http://schemas.openxmlformats.org/package/2006/relationships'><Relationship Id='r1' Target='worksheets/sheet1.xml'/></Relationships>");
            var xml=new StringBuilder("<worksheet xmlns='http://schemas.openxmlformats.org/spreadsheetml/2006/main'><sheetData>");var all=new[]{headers}.Concat(rows).ToArray();
            for(var i=0;i<all.Length;i++){xml.Append(CultureInfo.InvariantCulture,$"<row r='{i+1}'>");for(var j=0;j<all[i].Length;j++)xml.Append(CultureInfo.InvariantCulture,$"<c r='{(char)('A'+j)}{i+1}' t='inlineStr'><is><t>{System.Security.SecurityElement.Escape(all[i][j])}</t></is></c>");xml.Append("</row>");}
            xml.Append("</sheetData></worksheet>");Part("xl/worksheets/sheet1.xml",xml.ToString());
        }
        return stream.ToArray();
    }
}
