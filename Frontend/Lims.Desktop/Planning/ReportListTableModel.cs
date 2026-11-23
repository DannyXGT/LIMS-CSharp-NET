using System.Globalization;
using System.Text.Json;
using Lims.Contracts.Planning;

namespace Lims.Desktop.Planning;

/// <summary>Column layout and clipboard selection, independent of digitization and review state.</summary>
public sealed class ReportListTableModel
{
    public sealed record Column(string Key,string Label,double Width,bool Default=true);
    private sealed record Layout(int Version,IReadOnlyList<Column> Columns);
    public static readonly IReadOnlySet<string> Required=new HashSet<string>(StringComparer.Ordinal){"InDate","DueDate","Job","Test","Laboratory","Time","Date","VerifiedBy","Selection"};
    public static readonly IReadOnlyList<Column> Available=[
        new("InDate","Fecha de ingreso",112),new("DueDate","Due Date",105),new("Job","JOB",145),
        new("Test","Prueba de Planificación",210),new("Laboratory","Laboratorio",110),new("Buyer","Cliente",145),
        new("Time","Hora",85),new("Date","Fecha de prueba",112),
        new("VerifiedBy","Digitado por",135),new("Selection","CHK",85),new("Checked","Estado de digitación",155,false),
        new("Block","Bloque histórico",110,false),new("Keep","Mantener",85,false),new("OriginalTest","Prueba original",260,false),
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
        "Test"=>!string.IsNullOrWhiteSpace(item.Execution.Equivalence)?item.Execution.PlanningTest:item.Execution.NoProgram?"NO PROGRAMAR":(item.Execution.OriginalTest??"Sin nombre original")+" · Equivalencia pendiente",
        "Checked"=>PlanningSheetModel.Value(item.Execution,"DigitizationState"),
        "Time"=>item.CheckedTimeText,"Date"=>PlanningSheetModel.Value(item.Execution,"TestedOn"),
        "VerifiedBy"=>item.Execution.DigitizedBy??"","Block"=>item.BlockLabel,"Keep"=>item.KeepLabel,"Selection"=>"",
        _=>PlanningSheetModel.Value(item.Execution,key)
    };
    public void Restore()=>LoadLayout(null);
    public static bool CanMark(ReportListItem item)=>item.Execution.ExecutionId is not null&&!item.Execution.Complete&&!item.Execution.NoProgram&&!item.Execution.OnHold&&!string.IsNullOrWhiteSpace(item.Execution.Equivalence);
    public static string CheckDescription(ReportListItem item,bool selected)=>item.Execution.Complete?"Digitación guardada":selected?"Marcada sin guardar":item.Execution.DigitizationState;
    public string SaveLayout()=>JsonSerializer.Serialize(new Layout(2,Columns));
    public void LoadLayout(string? json)
    {
        IReadOnlyList<Column>? saved=null;
        try{if(json is {Length:>0 and <16000}){using var doc=JsonDocument.Parse(json);saved=doc.RootElement.ValueKind==JsonValueKind.Array?JsonSerializer.Deserialize<Column[]>(json)?.Where(c=>c is not null&&c.Key!="Checked").ToArray():JsonSerializer.Deserialize<Layout>(json) is {Version:2} layout?layout.Columns:null;}}catch(JsonException){ }
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
