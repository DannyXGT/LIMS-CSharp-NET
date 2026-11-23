using Lims.Application.Planning;
using Lims.Contracts.Planning;

namespace Lims.Application.Tests;

public sealed class HtmlReactImportRulesTests
{
    [Theory]
    [InlineData("J1",null,null,"Physical",false,"Active")]
    [InlineData("J1-REV1",null,null,"Physical",false,"Revision")]
    [InlineData("J1-S",null,null,"Physical",false,"Revision")]
    [InlineData("J1","Report Closed",null,"Physical",false,"Closed")]
    [InlineData("J1","Invoice Generated",null,"Physical",false,"Closed")]
    [InlineData("J1",null,"2026-11-17","Physical",false,"Closed")]
    [InlineData("J1",null,null,"SUB CONTRATO",false,"Subcontract")]
    [InlineData("J1",null,null,"Unknown",false,"InvalidLaboratory")]
    [InlineData("J1",null,null,null,true,"NoProgram")]
    public void HtmlAdmissionPreservesSourceExecutionButSeparatesActiveWork(string job,string? status,string? close,string? lab,bool no,string expected)
    {
        var source=Summary(job) with{Official=new(status,CloseDate:close is null?null:DateOnly.Parse(close,System.Globalization.CultureInfo.InvariantCulture))};
        var input=Input(source,Report(job,"Original"));var plan=PlanningReconciler.Plan(input,[],[],[new("Original",no?null:"Plan",lab,no,0)]);
        Assert.Equal(expected,Assert.Single(plan.Admissions!).State);Assert.Single(plan.Executions);Assert.Equal(2,input.Rows.Count);
    }
    [Fact]
    public void UnknownMappingStaysPendingRatherThanActive()
    {var plan=PlanningReconciler.Plan(Input(Summary("J1"),Report("J1","Unmapped")),[],[],[]);Assert.Equal("MissingEquivalence",Assert.Single(plan.Admissions!).State);}
    [Fact]
    public void VerifiedManualReentryKeepsItsClassificationAndAdmission()
    {
        var summary=Summary("J1-REV1");var episode=new StoredEpisode(Guid.NewGuid(),summary.Job,summary.Wip!,0,[],summary.Wip!.InDate);
        var execution=new StoredExecution(Guid.NewGuid(),episode.Id,"Original","Verified manual","Physical","Manual");
        var plan=PlanningReconciler.Plan(Input(summary,Report(summary.Job,"Original")),[episode],[execution],[new("Original",null,null,true,0)]);
        Assert.Equal("Active",Assert.Single(plan.Admissions!).State);Assert.Equal("Verified manual",Assert.Single(plan.Executions).Equivalence);Assert.False(plan.Executions[0].NoProgram);
    }
    [Fact]
    public void MissingDueDateIsConservedWithoutAdmissionOrInventingADate()
    {
        var summary=Summary("J1");summary=summary with{Wip=summary.Wip! with{DueDate=null}};
        var plan=PlanningReconciler.Plan(Input(summary,Report("J1","Original")),[],[],[new("Original","Plan","Physical",false,0)]);
        Assert.Equal("IncompleteData",Assert.Single(plan.Admissions!).State);Assert.Null(Assert.Single(plan.Episodes).Source.DueDate);
    }
    [Fact]
    public void ReportOnlyNeverBecomesActiveFromAnInventedWip()
    {var plan=PlanningReconciler.Plan(Input(Report("J1","Original")),[],[],[new("Original","Plan","Physical",false,0)]);Assert.Equal("ReportWithoutSummary",Assert.Single(plan.Admissions!).State);}
    [Fact]
    public void ExplicitTargetSelectsOnlyTheRequestedRegistration()
    {
        var input=Input(Summary("J1"),Summary("J2") with{Wip=Summary("J2").Wip! with{InDate=new(2026,11,17)}},Report("J1","Original"),Report("J2","Original"));
        input=input with{Cut=input.Cut with{TargetInDate=new(2026,11,16)}};
        var plan=PlanningReconciler.Plan(input,[],[],[new("Original","Plan","Physical",false,0)]);
        Assert.Equal("Active",plan.Admissions!.Single(a=>a.Job=="J1").State);Assert.Equal("OutsideDate",plan.Admissions!.Single(a=>a.Job=="J2").State);
    }
    [Theory]
    [InlineData("Disney","RF",true)]
    [InlineData("Disney","FR",false)]
    [InlineData("Other","RF",false)]
    public void DisneyRfProducesATracedSccpOnlyForTheExactTrigger(string buyer,string test,bool expected)
    {
        var input=Input(Summary("J1") with{Wip=Summary("J1").Wip! with{Buyer=buyer}},Report("J1",test));
        var plan=PlanningReconciler.Plan(input,[],[],[new(test,test,"Analytical",false,0)]);
        Assert.Equal(expected,plan.Executions.Any(x=>x.Rule=="DisneyRfSccp:v1"));
        if(expected){var derived=Assert.Single(plan.Executions,x=>x.Rule is not null);Assert.Empty(derived.SourceRows);Assert.NotNull(derived.ParentExecutionId);}
    }
    [Fact]
    public void ToxicConsolidationRetainsOriginalUuidsAndChromium()
    {
        var names=new[]{"CADMIUM / LEAD","TOTAL MERCURY","CHROMIUM VI"};
        var input=Input(new[]{Summary("J1")}.Concat(names.Select((name,i)=>Report("J1",name) with{Row=i+2})).ToArray());
        var plan=PlanningReconciler.Plan(input,[],[],names.Select(n=>new PlanningEquivalence(n,n,"Analytical",false,0)).ToArray());
        Assert.Equal(4,plan.Executions.Count);Assert.Equal(2,plan.Admissions!.Count(a=>a.State=="Consolidated"));
        Assert.Equal("Active",plan.Admissions!.Single(a=>a.OriginalTest=="CHROMIUM VI").State);
        Assert.Contains(plan.Executions,x=>x.Equivalence=="TOTAL TOXIC ELEMENTS"&&x.Rule=="ReactToxicElements:v1");
    }
    private static PlanningSourceRow Summary(string job)=>new("Summary","S",2,job,null,new("Buyer","Vendor","Regular",new(2026,11,16),new(2026,11,20),100,"USD",100),"{}");
    [Fact]
    public void ComplementaryReaderRecognizesReactAliasesAndPreservesOriginalHeaders()
    {
        var bytes=PlanningTests.Workbook(["FECHA INGRESO","DUE DATE","JOBS","PRUEBA","LABORATORIO"],[["2026-11-16","2026-11-20","J2-REV1","Manual Test","Physical"]]);
        var input=PlanningImportReader.ReadSupplement("original.xlsx",bytes,new(2026,11,16));
        var row=Assert.Single(input.Rows);Assert.Equal("Side2",row.File);Assert.Contains("LABORATORIO",row.Fields);Assert.Equal("Physical",row.SourceLaboratory);
        Assert.Null(row.Wip!.Buyer);Assert.Null(row.Wip.Usd);Assert.Empty(input.Summary);Assert.Equal(bytes,input.Report);
        Assert.Equal("HtmlReactSide2",input.Cut.Policy);
    }
    private static PlanningSourceRow Report(string job,string test)=>new("Report List","R",2,job,test,null,"{}");
    private static PlanningImportInput Input(params PlanningSourceRow[] rows)=>PlanningTests.Input(rows) with{Cut=new("Noon",new(2026,11,16),"HtmlReact")};
}
