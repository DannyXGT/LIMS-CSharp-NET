using System.Collections.ObjectModel;
using System.Globalization;
using System.Text;
using Lims.Contracts.Planning;

namespace Lims.Desktop.Planning;

public sealed record SheetColumn(string Key,string Title,int Width,bool Editable=false,bool InitiallyVisible=true);
public readonly record struct SheetCell(int Row,int Column);
public sealed record SheetPasteCell(int Row,int Column,string Before,string After,string Status,PlanningNoteCell? Change);
public sealed record SheetPastePreview(IReadOnlyList<SheetPasteCell> Cells)
{
    public int Valid=>Cells.Count(c=>c.Change is not null);
    public int Invalid=>Cells.Count(c=>c.Status.StartsWith("Inválido",StringComparison.Ordinal));
    public int Skipped=>Cells.Count-Valid-Invalid;
}

/// <summary>Selection and clipboard rules independent of native rendering. All indices refer to the loaded server page.</summary>
public sealed class PlanningSheetModel
{
    public static readonly IReadOnlyList<SheetColumn> AvailableColumns=[
        new("InDate","Fecha Ingreso",108),new("DueDate","Due Date",112),new("JobState","Estado",175),new("Job","JOB",190),new("OriginalTest","Prueba",230),
        new("Laboratory","Laboratorio",125),new("DigitizedAt","Hora",100),new("TestedOn","Fecha Prueba",110),new("DigitizationState","Estado de digitación",150),new("DigitizedBy","Digitado por",120),
        new("Equivalence","Equivalencia",190,false,false),new("Service","Servicio",112,false,false),new("Buyer","Buyer",150,false,false),new("Vendor","Vendor",170,false,false),new("Usd","USD",100,false,false),
        new("Responsible","Responsable",150,true,false),new("Observation","Observación",193,true,false),new("OnHold","On Hold",100,false,false),new("JobLabel","Etiqueta",120,false,false),new("NoProgram","Programación",125,false,false),
        new("Origin","Origen",110,false,false),new("ExecutionType","Tipo ejecución",135,false,false)];
    public ObservableCollection<PlanningRow> Rows{get;}=[];
    public Func<int,string,string>? DisplayValue {get;set;}
    public List<SheetColumn> Columns{get;}=AvailableColumns.Where(c=>c.InitiallyVisible).ToList();
    public SheetCell Active{get;private set;}
    public SheetCell Anchor{get;private set;}
    public SheetCell End{get;private set;}
    public (int Top,int Left,int Bottom,int Right) Range=>(Math.Min(Anchor.Row,End.Row),Math.Min(Anchor.Column,End.Column),Math.Max(Anchor.Row,End.Row),Math.Max(Anchor.Column,End.Column));
    public void Select(int row,int column,bool extend=false)
    {
        if(Rows.Count==0||Columns.Count==0)return;
        Active=new(Math.Clamp(row,0,Rows.Count-1),Math.Clamp(column,0,Columns.Count-1));End=Active;if(!extend)Anchor=Active;
    }
    public void SelectColumn(int column){if(Rows.Count==0)return;Select(0,column);Select(Rows.Count-1,column,true);}
    public void SelectAll(){if(Rows.Count==0||Columns.Count==0)return;Select(0,0);Select(Rows.Count-1,Columns.Count-1,true);}
    public bool Contains(int row,int column){var r=Range;return row>=r.Top&&row<=r.Bottom&&column>=r.Left&&column<=r.Right;}
    public string Value(int row,int column)=>DisplayValue?.Invoke(row,Columns[column].Key)??Value(Rows[row],Columns[column].Key);
    public static string Value(PlanningRow row,string key)=>key switch
    {
        "JobState"=>row.JobState,"Job"=>row.Job,"InDate"=>row.InDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"","DueDate"=>row.DueDate?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"",
        "OriginalTest"=>row.OriginalTest??"","Equivalence"=>row.Equivalence??"","Laboratory"=>row.Laboratory??"","Service"=>row.Service??"",
        "DigitizationState"=>row.DigitizationState=="Digitado"?"Digitada":row.DigitizationState=="NoProgramar"?"NO PROGRAMAR":row.DigitizationState,
        "DigitizedAt"=>row.DigitizedAt?.ToOffset(TimeSpan.FromHours(-6)).ToString("HH:mm:ss",CultureInfo.InvariantCulture)??"",
        "TestedOn"=>row.TestedOn?.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture)??"","DigitizedBy"=>row.DigitizedBy??"","Responsible"=>row.Responsible??"",
        "Observation"=>row.Observation??"","ExecutionType"=>row.ExecutionType??"","Origin"=>row.OriginLabel,
        "Buyer"=>row.Buyer??"","Vendor"=>row.Vendor??"","Usd"=>row.Usd?.ToString("0.####",CultureInfo.InvariantCulture)??"",
        "OnHold"=>row.OnHold?"On Hold":"Activo","JobLabel"=>row.JobLabel??"","NoProgram"=>row.NoProgram?"NO PROGRAMAR":"",_=>""
    };
    private static string Quote(string value)=>value.IndexOfAny(['\t','\n','\r','"'])<0?value:"\""+value.Replace("\"","\"\"",StringComparison.Ordinal)+"\"";
    public string Copy()
    {
        if(Rows.Count==0)return "";var r=Range;
        return string.Join("\r\n",Enumerable.Range(r.Top,r.Bottom-r.Top+1).Select(row=>string.Join('\t',Enumerable.Range(r.Left,r.Right-r.Left+1).Select(col=>Quote(Value(row,col))))));
    }
    public static IReadOnlyList<IReadOnlyList<string>> ParseTsv(string text)
    {
        if(text.Length>4*1024*1024)throw new FormatException("El portapapeles supera 4 MiB.");
        text=text.Replace("\r\n","\n",StringComparison.Ordinal).Replace('\r','\n');
        var rows=new List<IReadOnlyList<string>>();var line=new List<string>();var field=new StringBuilder();bool quoted=false,closed=false;int cells=0;
        void Cell(){if(++cells>2000)throw new FormatException("Máximo 2000 celdas por pegado.");line.Add(field.ToString());field.Clear();closed=false;}
        for(var i=0;i<text.Length;i++)
        {
            var ch=text[i];
            if(ch=='"'&&quoted){if(i+1<text.Length&&text[i+1]=='"'){field.Append('"');i++;}else{quoted=false;closed=true;}continue;}
            if(ch=='"'&&field.Length==0&&!closed){quoted=true;continue;}
            if(!quoted&&(ch=='\t'||ch=='\r'||ch=='\n'))
            {Cell();if(ch!='\t'){if(ch=='\r'&&i+1<text.Length&&text[i+1]=='\n')i++;rows.Add(line);line=[];}continue;}
            if(closed)throw new FormatException("Matriz tabulada inválida después de las comillas.");
            field.Append(ch);
        }
        if(quoted)throw new FormatException("El texto tabulado tiene comillas sin cerrar.");
        if(field.Length>0||closed||line.Count>0||rows.Count==0){Cell();rows.Add(line);}
        return rows;
    }
    public SheetPastePreview Preview(string text,bool allowed)
    {
        if(Rows.Count==0)throw new FormatException("Primero consulte y seleccione una celda.");
        var matrix=ParseTsv(text);var cells=new List<SheetPasteCell>();
        for(var r=0;r<matrix.Count;r++)for(var c=0;c<matrix[r].Count;c++)
        {
            int row=Active.Row+r,col=Active.Column+c;string after=matrix[r][c],before="",status;PlanningNoteCell? change=null;
            if(row>=Rows.Count||col>=Columns.Count)status="Omitido · fuera de la página cargada";
            else
            {
                before=Value(row,col);var column=Columns[col];var item=Rows[row];
                if(!allowed)status="Omitido · sin permiso de edición WIP";
                else if(item.OnHold)status="Omitido · JOB On Hold";
                else if(!column.Editable||item.ExecutionId is null)status="Omitido · columna protegida";
                else if(after==before)status="Omitido · sin cambios";
                else if(after.Length>(column.Key=="Responsible"?150:2000)||after.Any(ch=>char.IsControl(ch)&&ch!='\n')||(column.Key=="Responsible"&&after.Contains('\n')))status="Inválido · longitud o caracteres no permitidos";
                else{status="Válido";change=new(item.ExecutionId.Value,item.NoteVersion,column.Key,after);}
            }
            cells.Add(new(row,col,before,after,status,change));
        }
        return new(cells);
    }
    public void Apply(IReadOnlyList<PlanningNoteSaved> saved)
    {
        var byId=saved.ToDictionary(s=>s.ExecutionId);
        for(var i=0;i<Rows.Count;i++)if(Rows[i].ExecutionId is {} id&&byId.TryGetValue(id,out var s))Rows[i]=Rows[i] with{Responsible=s.Responsible,Observation=s.Observation,NoteVersion=s.Version};
    }
}
