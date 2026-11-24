using System.Globalization;
using System.Text.Json;
using Lims.Contracts.Planning;

namespace Lims.Desktop.Planning;

/// <summary>Column layout and clipboard selection, independent of digitization and review state.</summary>
public sealed class ReportListTableModel
{
    public sealed record Column(string Key,string Label,double Width,bool Default=true);
    private sealed record Layout(int Version,IReadOnlyList<Column> Columns);
    public static readonly IReadOnlySet<string> Required=new HashSet<string>(StringComparer.Ordinal){"Job","Test","Laboratory","Buyer","Keep","Time","Date","VerifiedBy","Check"};
    public static readonly IReadOnlyList<Column> Available=[
        new("Job","JOB",140),new("Test","Prueba",250),new("Laboratory","Laboratorio",110),new("Buyer","Cliente",185),
        new("Keep","Mantener en Muestreo",98),new("VerifiedBy","Digitado por",140),new("Date","Fecha",100),new("Time","Hora",70),new("Check","CHK",52),
        new("InDate","Fecha de ingreso",112,false),new("DueDate","Due Date",105,false),new("Selection","Seleccionar",85,false),new("Checked","Estado de digitación",155,false),
        new("Block","Bloque histórico",110,false),
        new("ExecutionId","Ejecución",290,false),new("ExecutionType","Tipo ejecución",140,false),new("Service","Servicio",120,false)];
    public List<Column> Columns {get;}=Available.Where(c=>c.Default).ToList();
    public IReadOnlyList<ReportListItem> Rows {get;set;}=[];
    public SheetCell Active {get;private set;}
    public SheetCell Anchor {get;private set;}
    public bool HasCells {get;private set;}
    public (int Top,int Left,int Bottom,int Right) Range=>(Math.Min(Anchor.Row,Active.Row),Math.Min(Anchor.Column,Active.Column),Math.Max(Anchor.Row,Active.Row),Math.Max(Anchor.Column,Active.Column));
    public void Select(int row,int column,bool extend=false)
    {
        if(Rows.Count==0||Columns.Count==0)return;
        Active=new(Math.Clamp(row,0,Rows.Count-1),Math.Clamp(column,0,Columns.Count-1));
        if(!extend||!HasCells)Anchor=Active;HasCells=true;
    }
    public void ClearCells()=>HasCells=false;
    public bool Contains(int row,int col){var r=Range;return HasCells&&row>=r.Top&&row<=r.Bottom&&col>=r.Left&&col<=r.Right;}
    public string Copy()
    {
        if(!HasCells||Rows.Count==0)return "";var r=Range;
        return string.Join("\r\n",Enumerable.Range(r.Top,Math.Min(r.Bottom,Rows.Count-1)-r.Top+1).Select(row=>string.Join('\t',Enumerable.Range(r.Left,r.Right-r.Left+1).Select(col=>Quote(Value(Rows[row],Columns[col].Key))))));
    }
    public static string Quote(string s)=>s.IndexOfAny(['\t','\r','\n','"'])<0?s:"\""+s.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";
    public static string Value(ReportListItem item,string key)=>key switch
    {
        "Test"=>item.Execution.PlanningTest,
        "Checked"=>PlanningSheetModel.Value(item.Execution,"DigitizationState"),
        "Time"=>item.Execution.DigitizedAt?.ToOffset(TimeSpan.FromHours(-6)).ToString("HH:mm",CultureInfo.InvariantCulture)??"","Date"=>PlanningSheetModel.Value(item.Execution,"TestedOn"),
        "VerifiedBy"=>item.Execution.DigitizedBy??"","Block"=>item.BlockLabel,"Keep"=>item.KeepLabel,"Selection"=>"","Check"=>item.Execution.Complete?"Sí":"No",
        _=>PlanningSheetModel.Value(item.Execution,key)
    };
    public void Restore()=>LoadLayout(null);
    public static bool CanMark(ReportListItem item)=>item.Execution.ExecutionId is not null&&!item.Execution.Complete&&!item.Execution.NoProgram&&!item.Execution.OnHold&&!string.IsNullOrWhiteSpace(item.Execution.Equivalence);
    public static bool CanCheck(ReportListItem item,bool mark,bool review,bool admin)=>item.Execution.ExecutionId is not null&&
        (item.Execution.Complete?review:mark&&CanMark(item))&&(item.CheckMode=="manual"||admin);
    public void Fit(double width)
    {
        if(!double.IsFinite(width)||width<700||Columns.Count==0)return;
        static double Min(Column c)=>c.Key switch{"Check"=>44,"Keep"=>82,"Time"=>62,"Date"=>86,"Job"=>110,"Laboratory"=>100,"Test"=>170,"Buyer"=>120,"VerifiedBy"=>100,_=>65};
        var minimum=Columns.Sum(Min);if(width<minimum)return;
        var weight=Columns.Sum(c=>Math.Max(1,c.Width-Min(c)));
        for(var i=0;i<Columns.Count;i++){var c=Columns[i];Columns[i]=c with{Width=Min(c)+(width-minimum)*Math.Max(1,c.Width-Min(c))/weight};}
    }
    public static string CheckDescription(ReportListItem item,bool selected)=>item.Execution.Complete?"Digitación guardada":selected?"Marcada sin guardar":item.Execution.DigitizationState;
    public string SaveLayout()=>JsonSerializer.Serialize(new Layout(3,Columns));
    public void LoadLayout(string? json)
    {
        IReadOnlyList<Column>? saved=null;
        try{if(json is {Length:>0 and <16000}){using var doc=JsonDocument.Parse(json);saved=doc.RootElement.ValueKind==JsonValueKind.Array?JsonSerializer.Deserialize<Column[]>(json)?.Where(c=>c is not null&&c.Key!="Checked"&&c.Key!="Selection"&&c.Key!="InDate"&&c.Key!="DueDate").ToArray():JsonSerializer.Deserialize<Layout>(json) is {Version:3} layout?layout.Columns:JsonSerializer.Deserialize<Layout>(json) is {Version:2} old?old.Columns.Where(c=>c.Key is not ("Selection" or "InDate" or "DueDate")).ToArray():null;}}catch(JsonException){ }
        Columns.Clear();
        if(saved is not null)
            foreach(var c in saved)
                if(c is not null&&Available.FirstOrDefault(a=>a.Key==c.Key) is {} known&&!Columns.Any(a=>a.Key==c.Key))
                    Columns.Add(known with{Width=double.IsFinite(c.Width)?Math.Clamp(c.Width,65,700):known.Width});
        if(Columns.Count==0)Columns.AddRange(Available.Where(c=>c.Default));
        foreach(var key in Available.Where(c=>Required.Contains(c.Key)).Select(c=>c.Key))
            if(!Columns.Any(c=>c.Key==key))Columns.Insert(Math.Min(Available.ToList().FindIndex(c=>c.Key==key),Columns.Count),Available.Single(c=>c.Key==key));
        ClearCells();
    }
}
