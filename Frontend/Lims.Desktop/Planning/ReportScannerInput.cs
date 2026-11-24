using System.Text;
using Lims.Contracts.Planning;

namespace Lims.Desktop.Planning;

/// <summary>CR/LF framing and idle flush match the Electron reader; buffers are bounded.</summary>
public sealed class ReportScannerInput
{
    private readonly StringBuilder _buffer=new();
    public IReadOnlyList<string> Append(string text)
    {
        var result=new List<string>();
        foreach(var ch in text)
        {
            if(ch is '\r' or '\n' or '\t'){var value=Flush();if(value is not null)result.Add(value);}
            else if(!char.IsControl(ch)){if(_buffer.Length>=200)_buffer.Clear();_buffer.Append(ch);}
        }
        return result;
    }
    public string? Flush(){var value=_buffer.ToString().Trim();_buffer.Clear();return value.Length==0?null:value;}
    public void Clear()=>_buffer.Clear();
}
