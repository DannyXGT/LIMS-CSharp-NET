using System.Globalization;
using System.Text;
using Lims.Contracts.Planning;

namespace Lims.Application.Planning;

/// <summary>HTML admission rules, followed by the two explicit React business rules.
/// Original executions and source rows remain intact even when they are not operational.</summary>
public static class PlanningImportRules
{
    public const string Version="html-react-v1";
    private static readonly string[] RequiredToxicComponents=["lead","cadmium","mercury","chromium"];
    public static string Token(string? value)
    {
        var text=(value??"").Normalize(NormalizationForm.FormD);
        return PlanningReconciler.TestKey(new string(text.Where(c=>CharUnicodeInfo.GetUnicodeCategory(c)!=UnicodeCategory.NonSpacingMark).ToArray()));
    }
    public static bool IsClosed(PlanningOfficialState? state)=>state?.CloseDate is not null ||
        Token(state?.SampleStatus).Contains("REPORT CLOSED",StringComparison.Ordinal)||Token(state?.SampleStatus).Contains("INVOICE GENERATED",StringComparison.Ordinal);
    public static string Laboratory(string? value)=>Token(value).Replace("/ ","/",StringComparison.Ordinal) switch
    {
        "ORGANIC" or "ORGANICO" or "ANALYTICAL/ORGANICO"=>"ANALYTICAL/ORGANIC",
        "INORGANIC" or "INORGANICO" or "ANALYTICAL/INORGANICO"=>"ANALYTICAL/INORGANIC",
        "MICRO" or "MICROBIOLOGIA"=>"MICROBIOLOGY",var name=>name
    };
    public static bool IsProgramLaboratory(string? value)=>Laboratory(value) is "PHYSICAL" or "ANALYTICAL" or "CHEMICAL" or "ANALYTICAL/ORGANIC" or "ANALYTICAL/INORGANIC" or "MICROBIOLOGY";
    public static string DispositionLabel(string state)=>state switch
    {
        "Active"=>"Trabajo activo", "Closed"=>"Histórico cerrado", "Revision"=>"Revisión o reingreso pendiente",
        "OutsideDate"=>"Fuera del ingreso seleccionado", "ReportWithoutSummary"=>"Report List sin Summary",
        "MissingEquivalence"=>"Equivalencia pendiente", "NoProgram"=>"NO PROGRAMAR", "Subcontract"=>"Subcontrato",
        "IncompleteData"=>"Fechas WIP incompletas",
        "InvalidLaboratory"=>"Laboratorio no programable", "Consolidated"=>"Componente conservado en consolidación",
        "RuleRetired"=>"Regla derivada sin condición vigente",
        "ManualArchived"=>"Archivado manualmente", "ReactIdentity"=>"Identidad React por conciliar",_=>state
    };
    public static PlanningPlan Apply(PlanningImportInput input,PlanningPlan plan,IReadOnlyList<StoredEpisode> stored,IReadOnlyList<StoredExecution> existing)
    {
        var episodes=plan.Episodes.ToDictionary(e=>e.Id);
        var side2=input.Cut.Policy=="HtmlReactSide2";
        var table=input.Cut.Policy=="ReactTable";
        foreach(var e in stored)episodes.TryAdd(e.Id,new(e.Id,e.Job,e.Wip,false,0,e.Official));
        var target=input.Cut.TargetInDate??episodes.Values.Where(e=>e.HasSummary&&!e.Job.Contains('-')&&!IsClosed(e.Official)).Select(e=>e.Source.InDate).Max();
        var executions=plan.Executions.ToList();var reviews=plan.Reviews.ToList();var changes=plan.Changes.ToList();
        var admissions=new Dictionary<Guid,PlanningAdmission>();
        foreach(var x in executions)
        {
            var e=episodes[x.EpisodeId];
            var manual=side2||x.Origin=="Manual"||x.Rule=="ReactSide2:v1";
            var state=IsClosed(e.Official)?"Closed":!manual&&e.Job.Contains('-')?"Revision":!manual&&!e.HasSummary?"ReportWithoutSummary":
                e.Source.InDate is null||e.Source.DueDate is null?"IncompleteData":!manual&&!table&&e.Source.InDate!=target?"OutsideDate":
                x.NoProgram||Laboratory(x.Laboratory)=="NO PROGRAMAR"?"NoProgram":
                string.IsNullOrWhiteSpace(x.Equivalence)?"MissingEquivalence":
                Laboratory(x.Laboratory).Replace(" ","",StringComparison.Ordinal) is "SUBCONTRATO" or "SUBONCONTRATO"?"Subcontract":
                !IsProgramLaboratory(x.Laboratory)?"InvalidLaboratory":"Active";
            var reason=DispositionLabel(state)+(state=="OutsideDate"?$" · ingreso objetivo {target:yyyy-MM-dd}":"");
            admissions[x.Id]=new(x.Id,x.Job,x.OriginalTest,state,reason,x.Rule);
        }
        void Derived(ProposedExecution parent,string name,string rule)
        {
            var previous=existing.SingleOrDefault(e=>e.EpisodeId==parent.EpisodeId&&e.ExecutionType==rule);
            var x=new ProposedExecution(previous?.Id??Guid.NewGuid(),parent.EpisodeId,parent.Job,name,name,parent.Laboratory,false,
                previous is null?"Create":"Observe",[],rule,parent.Id);
            executions.Add(x);admissions[x.Id]=new(x.Id,x.Job,x.OriginalTest,"Active",DispositionLabel("Active")+" · "+rule,rule);
            changes.Add(new("BusinessRule",previous is null?"CreateDerived":"ObserveDerived",parent.Job,parent.EpisodeId,name,parent.Id.ToString(),rule));
        }
        foreach(var x in plan.Executions.Where(x=>!side2&&x.Origin!="Manual"&&x.Rule!="ReactSide2:v1"&&admissions[x.Id].State=="Active"&&Token(x.Equivalence)=="RF"))
        {
            if(!Token(episodes[x.EpisodeId].Source.Buyer).Contains("DISNEY",StringComparison.Ordinal))continue;
            if(executions.Any(e=>e.EpisodeId==x.EpisodeId&&Token(e.Equivalence)=="SCCP"))continue;
            Derived(x,"SCCP","DisneyRfSccp:v1");
        }
        foreach(var group in plan.Executions.Where(x=>!side2&&x.Origin!="Manual"&&x.Rule!="ReactSide2:v1"&&admissions[x.Id].State=="Active").GroupBy(x=>x.EpisodeId))
        {
            var components=group.Where(x=>Components(x.Equivalence).Count>0).ToArray();
            var covered=components.SelectMany(x=>Components(x.Equivalence)).ToHashSet();
            if(!RequiredToxicComponents.All(covered.Contains))continue;
            foreach(var component in components.Where(x=>!Components(x.Equivalence).Contains("chromium")))
                admissions[component.Id]=admissions[component.Id] with{State="Consolidated",Reason="Conservado como fuente de TOTAL TOXIC ELEMENTS · ReactToxicElements:v1",Rule="ReactToxicElements:v1"};
            if(!executions.Any(x=>x.EpisodeId==group.Key&&Token(x.Equivalence)=="TOTAL TOXIC ELEMENTS"))
                Derived(components.First(x=>Components(x.Equivalence).Contains("lead")),"TOTAL TOXIC ELEMENTS","ReactToxicElements:v1");
        }
        if(table)
        foreach(var child in executions.Where(x=>x.Rule is "DisneyRfSccp:v1" or "ReactToxicElements:v1"&&admissions[x.Id].State=="Active"))
        {
            if(existing.Any(x=>x.Id==child.Id&&x.HasReportSource))continue;
            var current=executions.Where(x=>x.EpisodeId==child.EpisodeId&&x.Rule is not ("DisneyRfSccp:v1" or "ReactToxicElements:v1")&&admissions[x.Id].State is "Active" or "Consolidated").Select(x=>x.Equivalence);
            var retained=existing.Where(x=>x.EpisodeId==child.EpisodeId&&!x.NoProgram&&x.ExecutionType is not ("DisneyRfSccp:v1" or "ReactToxicElements:v1")&&executions.All(e=>e.Id!=x.Id)&&IsProgramLaboratory(x.Laboratory)).Select(x=>x.Equivalence);
            var names=current.Concat(retained).ToArray();
            var valid=child.Rule=="DisneyRfSccp:v1"?Token(episodes[child.EpisodeId].Source.Buyer).Contains("DISNEY",StringComparison.Ordinal)&&names.Any(n=>Token(n)=="RF"):
                RequiredToxicComponents.All(names.SelectMany(Components).ToHashSet().Contains);
            if(!valid)admissions[child.Id]=admissions[child.Id] with{State="RuleRetired",Reason=DispositionLabel("RuleRetired")};
        }
        foreach(var previous in existing.Where(x=>x.ExecutionType is "DisneyRfSccp:v1" or "ReactToxicElements:v1"&&episodes.ContainsKey(x.EpisodeId)&&executions.All(e=>e.Id!=x.Id)))
        {
            var episode=episodes[previous.EpisodeId];
            executions.Add(new(previous.Id,previous.EpisodeId,episode.Job,previous.OriginalTest,previous.Equivalence,previous.Laboratory,false,"Observe",[],previous.ExecutionType));
            admissions[previous.Id]=new(previous.Id,episode.Job,previous.OriginalTest,"RuleRetired",DispositionLabel("RuleRetired"),previous.ExecutionType);
        }
        foreach(var item in admissions.Values.Where(a=>a.State!="Active"))
        {
            var x=executions.Single(x=>x.Id==item.ExecutionId);
            reviews.Add(new("Admission",x.Job,x.EpisodeId,x.OriginalTest,item.Reason,x.SourceRows.Count>0?x.SourceRows[0]:0));
        }
        return plan with{Executions=executions,Reviews=reviews,Changes=changes,Admissions=admissions.Values.ToArray()};
    }
    private static HashSet<string> Components(string? value)
    {
        var token=Token(value);var words=token.Split(' ').ToHashSet();var set=new HashSet<string>();
        if(words.Contains("LEAD")||words.Contains("PB"))set.Add("lead");
        if(words.Contains("CADMIUM")||words.Contains("CD"))set.Add("cadmium");
        if(words.Contains("MERCURY")||words.Contains("HG"))set.Add("mercury");
        if(token.Contains("CHROMIUM VI",StringComparison.Ordinal)||token.Contains("HEXAVALENT CHROMIUM",StringComparison.Ordinal)||token is "CR VI" or "CRVI" or "CHROMIUM 6")set.Add("chromium");
        return set;
    }
}
