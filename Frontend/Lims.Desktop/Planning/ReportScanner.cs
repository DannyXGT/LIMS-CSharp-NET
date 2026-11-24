using System.IO.Ports;
using Microsoft.Win32;

namespace Lims.Desktop.Planning;

public sealed record ReportScannerPort(string Path,string Name,string DeviceId)
{
    private static readonly string[] ScannerNames=["SCANNER","BARCODE","HONEYWELL","ZEBRA","DATALOGIC"];
    public int Score
    {
        get
        {
            var text=(DeviceId+" "+Name).ToUpperInvariant();
            if(text.Contains("BTHENUM")||text.Contains("BLUETOOTH"))return -1;
            var score=text.Contains("USB")||text.Contains("FTDIBUS")?120:0;
            if(ScannerNames.Any(text.Contains))score+=80;
            return score;
        }
    }
    public override string ToString()=>Path+" · "+Name;
}
public sealed record ReportScannerState(bool Connected,string? Port,string Message);

/// <summary>Owns one read-only serial connection while Report List is visible.</summary>
public interface IReportScanner : IDisposable
{
    event Action<ReportScannerState>? Status;
    event Action<string>? Received;
    void Connect(string? path=null,int baud=9600);
    void Disconnect();
}
public sealed class ReportScanner : IReportScanner
{
    private CancellationTokenSource? _stop;
    private Task? _running;
    private ReportScannerState? _last;
    public event Action<ReportScannerState>? Status;
    public event Action<string>? Received;
    public static IReadOnlyList<ReportScannerPort> Ports()
    {
        var names=SerialPort.GetPortNames().ToHashSet(StringComparer.OrdinalIgnoreCase);
        var found=new Dictionary<string,ReportScannerPort>(StringComparer.OrdinalIgnoreCase);
        foreach(var bus in new[]{"USB","FTDIBUS","BTHENUM"})
        {
            using var root=Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Enum\"+bus);
            if(root is null)continue;
            foreach(var device in root.GetSubKeyNames())
            {
                using var group=root.OpenSubKey(device);if(group is null)continue;
                foreach(var instance in group.GetSubKeyNames())
                {
                    using var entry=group.OpenSubKey(instance);using var parameters=entry?.OpenSubKey("Device Parameters");
                    if(parameters?.GetValue("PortName") is not string port||!names.Contains(port))continue;
                    found[port]=new(port,entry?.GetValue("FriendlyName") as string??entry?.GetValue("DeviceDesc") as string??bus,bus+"\\"+device+"\\"+instance);
                }
            }
        }
        return names.Select(name=>found.GetValueOrDefault(name)??new(name,name,"")).OrderByDescending(p=>p.Score).ThenBy(p=>p.Path,StringComparer.OrdinalIgnoreCase).ToArray();
    }
    public void Connect(string? path=null,int baud=9600)
    {
        Disconnect();var cancellation=new CancellationTokenSource();_stop=cancellation;var prior=_running;
        _running=Task.Run(async()=>
        {
            try{if(prior is not null)await prior.ConfigureAwait(false);await Run(path,baud,cancellation.Token).ConfigureAwait(false);}
            catch(OperationCanceledException)when(cancellation.IsCancellationRequested){}
            finally{cancellation.Dispose();}
        });
    }
    private void Publish(ReportScannerState state){if(state==_last)return;_last=state;Status?.Invoke(state);}
    private async Task Run(string? requested,int baud,CancellationToken ct)
    {
        while(!ct.IsCancellationRequested)
        {
            try
            {
                var ports=Ports();var candidates=string.IsNullOrEmpty(requested)
                    ?ports.Where(p=>p.Score>0).OrderByDescending(p=>p.Score+(p.Path==Environment.GetEnvironmentVariable("LIMS_SCANNER_COM")?40:0)).ToArray()
                    :ports.Where(p=>p.Path.Equals(requested,StringComparison.OrdinalIgnoreCase)).ToArray();
                var opened=false;
                foreach(var candidate in candidates)
                {
                    ct.ThrowIfCancellationRequested();
                    Publish(new(false,candidate.Path,"Conectando · "+candidate.Path));
                    try
                    {
                        // Electron serialport enables DTR (hupcl=true) and disables RTS on Windows.
                        // USB CDC readers can open successfully yet send nothing with DTR disabled.
                        using var port=new SerialPort(candidate.Path,baud,Parity.None,8,StopBits.One){ReadTimeout=80,WriteTimeout=250,Handshake=Handshake.None,DtrEnable=true,RtsEnable=false};
                        port.Open();opened=true;Publish(new(true,candidate.Path,"Conectado · "+candidate.Path));
                        var frame=new ReportScannerInput();var chars=new char[256];var probe=DateTime.UtcNow;
                        while(!ct.IsCancellationRequested)
                        {
                            try{var count=port.Read(chars,0,chars.Length);ct.ThrowIfCancellationRequested();foreach(var code in frame.Append(new string(chars,0,count)))Received?.Invoke(code);}
                            catch(TimeoutException){if(frame.Flush() is{} code)Received?.Invoke(code);}
                            if(DateTime.UtcNow-probe>TimeSpan.FromSeconds(1))
                            {probe=DateTime.UtcNow;if(!SerialPort.GetPortNames().Contains(candidate.Path,StringComparer.OrdinalIgnoreCase))throw new IOException("Scanner desconectado");}
                        }
                    }
                    catch(Exception e)when(e is IOException or UnauthorizedAccessException or InvalidOperationException or ArgumentException)
                    {Publish(new(false,candidate.Path,e is UnauthorizedAccessException?"Puerto ocupado · "+candidate.Path+" · cierre LIMS React u otra aplicación que use el lector":"Desconectado · "+candidate.Path));}
                    if(opened)break;
                }
                if(candidates.Length==0)Publish(new(false,requested,"Sin conexión COM · USB teclado en espera"));
            }
            catch(Exception e)when(e is IOException or UnauthorizedAccessException or System.Security.SecurityException)
            {Publish(new(false,null,"No se pudo acceder al scanner · revise el puerto"));}
            await Task.Delay(2600,ct).ConfigureAwait(false);
        }
    }
    public void Disconnect(){try{_stop?.Cancel();}catch(ObjectDisposedException){} _stop=null;_last=null;}
    public void Dispose()=>Disconnect();
}
