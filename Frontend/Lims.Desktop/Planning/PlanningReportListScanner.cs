using Lims.Contracts.Planning;
using Lims.DesignSystem.Controls;
using System.Globalization;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Automation;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Windows.Foundation;
using Windows.System;
using Windows.UI.Core;

namespace Lims.Desktop.Planning;

public sealed partial class PlanningReportListView
{
    private readonly IReportScanner _serial;
    private readonly ReportScannerInput _keyboard=new();
    private readonly DispatcherTimer _keyboardIdle=new(){Interval=TimeSpan.FromMilliseconds(500)};
    private readonly TextBlock _scanReceipt=PlanningUi.Text("Escanea el reporte para marcar su JOB y prueba",12,true);
    private readonly TextBlock _scannerStatus=PlanningUi.Text("Sin conexión COM · USB teclado en espera",11,true);
    private readonly TextBlock _scannerDot=PlanningUi.Text("●",10,true);
    private readonly Queue<string> _scanQueue=[];
    private readonly DispatcherTimer _scanResume=new(){Interval=TimeSpan.FromMilliseconds(100)};
    private UIElement? _inputRoot;
    private bool _clearing,_scannerActive,_serialEnabled,_draining,_readerConnected;
    private string _readerLabel="USB teclado en espera";
    private readonly TextBlock _scannerSource=PlanningUi.Text("Consultando fuente AR…",11,true);
    private readonly TextBlock _scannerActivity=PlanningUi.Text("Todavía no se han recibido datos del lector.",12,true);
    private int _receivedScans;
    private string? _reportedPortError;
    private bool _scannerMessageOpen;
    private DateTimeOffset _lastCharacter;
    private string? _port;
    private int _baud=int.TryParse(Environment.GetEnvironmentVariable("LIMS_SCANNER_BAUD"),out var baud)&&baud>0?baud:9600;
    private TypedEventHandler<UIElement,CharacterReceivedRoutedEventArgs> _characterHandler=null!;
    private KeyEventHandler _terminatorHandler=null!;

    private Grid BuildScannerSection(bool enabled)
    {
        _serialEnabled=enabled;
        _characterHandler=OnScannerCharacter;_terminatorHandler=OnScannerTerminator;
        var configure=Compact("",()=>Task.CompletedTask);
        configure.MinHeight=30;configure.Height=30;configure.Padding=new(9,3,9,3);configure.VerticalAlignment=VerticalAlignment.Center;
        AutomationProperties.SetName(configure,"Estado del lector");
        _scannerStatus.FontSize=12;_scannerStatus.MaxWidth=230;_scannerStatus.TextWrapping=TextWrapping.NoWrap;_scannerStatus.TextTrimming=TextTrimming.CharacterEllipsis;
        var bars=new StackPanel{Orientation=Orientation.Horizontal,Spacing=1,Height=14,VerticalAlignment=VerticalAlignment.Center};
        foreach(var width in new[]{1d,2d,1d,1d,2d,1d})bars.Children.Add(new Border{Width=width,Height=14,Background=PlanningUi.Brush("LimsTextSecondaryBrush")});
        var indicator=PlanningUi.Row(bars,_scannerDot,_scannerStatus,new LimsIcon{Icon=LimsIconKind.ChevronDown,Size=10});indicator.Spacing=7;indicator.VerticalAlignment=VerticalAlignment.Center;
        configure.Content=indicator;
        _scanReceipt.TextWrapping=TextWrapping.NoWrap;_scanReceipt.TextTrimming=TextTrimming.CharacterEllipsis;_scanReceipt.VerticalAlignment=VerticalAlignment.Center;
        var connection=new ComboBox{MinWidth=270,Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"]};
        AutomationProperties.SetName(connection,"Puerto del scanner");
        var speed=new ComboBox{ItemsSource=new[]{9600,19200,38400,57600,115200},SelectedItem=_baud,Style=(Style)Application.Current.Resources["LimsCompactComboBoxStyle"]};
        AutomationProperties.SetName(speed,"Velocidad del scanner");
        var hint=PlanningUi.Text("Reconexión automática · CR, LF o CRLF. El modo USB teclado se confirma al recibir un código AR.",12,true);hint.MaxWidth=310;
        _scannerActivity.MaxWidth=310;
        var settings=PlanningUi.Stack(PlanningUi.Text("Conexión del scanner",15),connection,speed,_scannerSource,_scannerActivity,hint);settings.Spacing=8;
        var popup=new Flyout{Content=settings};configure.Flyout=popup;
        popup.Opening+=async(_,_)=>
        {
            try
            {
                var ports=await Task.Run(ReportScanner.Ports);connection.Items.Clear();connection.Items.Add("Automático · scanner USB / COM");connection.Items.Add("USB teclado");
                foreach(var item in ports)connection.Items.Add(item);
                connection.SelectedIndex=_port=="keyboard"?1:0;
                if(_port is not null&&_port!="keyboard")connection.SelectedItem=ports.FirstOrDefault(p=>p.Path==_port)??(object)connection.Items[0];
            }
            catch(Exception e)when(e is System.IO.IOException or UnauthorizedAccessException or System.Security.SecurityException){hint.Text="No se pudieron enumerar puertos. Revise los permisos de Windows.";}
        };
        settings.Children.Add(Compact("Aplicar conexión",()=>{_port=connection.SelectedIndex==1?"keyboard":(connection.SelectedItem as ReportScannerPort)?.Path;_baud=speed.SelectedItem is int b?b:9600;popup.Hide();RestartSerial();return Task.CompletedTask;}));
        var section=new Grid();section.Children.Add(configure);
        _serial.Status+=state=>DispatcherQueue.TryEnqueue(()=>
        {
            if(!_scannerActive)return;
            _readerConnected=state.Connected;_readerLabel=state.Connected?"Puerto abierto · "+state.Port:state.Message;SetScannerStatus(_readerLabel,state.Connected);
            if(!state.Connected&&state.Message.Contains("ocupado",StringComparison.OrdinalIgnoreCase)&&_reportedPortError!=state.Message){_reportedPortError=state.Message;ScanFeedback(state.Message,true);}
            if(state.Connected)_reportedPortError=null;
        });
        _serial.Received+=code=>DispatcherQueue.TryEnqueue(()=>ReceiveScan(code));
        _keyboardIdle.Tick+=(_,_)=>FlushKeyboard();
        _scanResume.Tick+=async(_,_)=>{_scanResume.Stop();await DrainScans();};
        Loaded+=(_,_)=>StartScanner();Unloaded+=(_,_)=>{StopScanner();_searchDelay.Stop();};
        return section;
    }
    private Button BuildClockSection()
    {
        var button=Compact("",()=>Task.CompletedTask);button.Height=30;button.MinHeight=30;button.Padding=new(9,3,9,3);button.VerticalAlignment=VerticalAlignment.Center;
        AutomationProperties.SetName(button,"Hora de digitación");
        _clockLabel.FontSize=12;_clockLabel.VerticalAlignment=VerticalAlignment.Center;
        var content=PlanningUi.Row(new LimsIcon{Icon=LimsIconKind.Calendar,Size=15},_clockLabel,new LimsIcon{Icon=LimsIconKind.ChevronDown,Size=10});content.Spacing=7;button.Content=content;
        _clockDate.Width=135;_clockDate.MinWidth=110;_clockTime.Width=85;_clockTime.MinWidth=70;
        var help=PlanningUi.Text("El lector registra la hora actual. Fija un horario para usarlo en varias pruebas.",12,true);help.MaxWidth=280;
        var error=PlanningUi.Text("",12,true);
        var panel=PlanningUi.Stack(PlanningUi.Text("Hora de digitación",16),help,PlanningUi.Row(_clockDate,_clockTime),error);panel.Spacing=9;
        var flyout=new Flyout{Content=panel};button.Flyout=flyout;
        flyout.Opening+=(_,_)=>{var now=_fixedClock.ForScan(DateTimeOffset.UtcNow);_clockDate.Text=now.ToString("yyyy-MM-dd",CultureInfo.InvariantCulture);_clockTime.Text=now.ToString("HH:mm",CultureInfo.InvariantCulture);error.Text="";};
        panel.Children.Add(Compact("Aplicar hora fija",()=>
        {
            if(!DateTime.TryParseExact(_clockDate.Text+" "+_clockTime.Text,"yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture,DateTimeStyles.None,out var local)){error.Text="Use fecha yyyy-MM-dd y hora HH:mm.";return Task.CompletedTask;}
            _fixedClock.Fix(new DateTimeOffset(DateTime.SpecifyKind(local,DateTimeKind.Unspecified),TimeSpan.FromHours(-6)));UpdateClockDisplay();flyout.Hide();Feedback(null);return Task.CompletedTask;
        },true));
        panel.Children.Add(Compact("Usar hora actual",()=>{_fixedClock.Clear();UpdateClockDisplay();flyout.Hide();Feedback(null);return Task.CompletedTask;}));
        _clockTick.Tick+=(_,_)=>UpdateClockDisplay();UpdateClockDisplay();
        return button;
    }
    private void UpdateClockDisplay()
    {
        var at=_fixedClock.ForScan(DateTimeOffset.UtcNow);_clockLabel.Text=at.ToString("HH:mm",CultureInfo.InvariantCulture)+(_fixedClock.FixedAt is null?" · hora actual":" · hora fija");
        _clockLabel.Foreground=PlanningUi.Brush(_fixedClock.FixedAt is null?"LimsTextSecondaryBrush":"LimsAccentBrush");
        ToolTipService.SetToolTip(_clockLabel,at.ToString("yyyy-MM-dd HH:mm",CultureInfo.InvariantCulture)+" · Guatemala");
    }
    private void SetScannerStatus(string text,bool connected)
    {
        _scannerStatus.Text=text.Contains("en espera",StringComparison.OrdinalIgnoreCase)?"Lector en espera":text;
        _scannerStatus.Foreground=PlanningUi.Brush(text.Contains("ocupado",StringComparison.OrdinalIgnoreCase)?"LimsDangerBrush":"LimsTextSecondaryBrush");
        _scannerDot.Foreground=PlanningUi.Brush(connected?"LimsAccentBrush":"LimsWarningBrush");
        ToolTipService.SetToolTip(_scannerStatus,text);AutomationProperties.SetItemStatus(this,text);
    }
    private void StartScanner()
    {
        if(_scannerActive||_disposed)return;_scannerActive=true;_clockTick.Start();
        _inputRoot=XamlRoot?.Content;
        _inputRoot?.AddHandler(CharacterReceivedEvent,_characterHandler,true);
        _inputRoot?.AddHandler(PreviewKeyDownEvent,_terminatorHandler,true);
        RestartSerial();
    }
    private void RestartSerial()
    {
        _serial.Disconnect();if(!_scannerActive)return;
        if(!_serialEnabled||_port=="keyboard"||!Has(PlanningPermissions.ReportMark)){_readerConnected=false;SetScannerStatus("USB teclado en espera de lectura",false);return;}
        _serial.Connect(_port,_baud);
    }
    private void StopScanner()
    {
        _scannerActive=false;_clockTick.Stop();_serial.Disconnect();_keyboardIdle.Stop();_scanResume.Stop();_keyboard.Clear();_scanQueue.Clear();
        _inputRoot?.RemoveHandler(CharacterReceivedEvent,_characterHandler);_inputRoot?.RemoveHandler(PreviewKeyDownEvent,_terminatorHandler);_inputRoot=null;
    }
    private bool CanCapture()=>_scannerActive&&IsLoaded&&_tabs.SelectedIndex==0&&Has(PlanningPermissions.ReportMark);
    private void OnScannerCharacter(UIElement sender,CharacterReceivedRoutedEventArgs e)
    {
        if(!CanCapture()||e.Character<32||Modifier(VirtualKey.Control)||Modifier(VirtualKey.Menu))return;
        var now=DateTimeOffset.UtcNow;if(now-_lastCharacter>TimeSpan.FromMilliseconds(650))_keyboard.Clear();_lastCharacter=now;
        _keyboard.Append(char.ConvertFromUtf32((int)e.Character));_keyboardIdle.Stop();_keyboardIdle.Start();
    }
    private static bool Modifier(VirtualKey key)=>(Microsoft.UI.Input.InputKeyboardSource.GetKeyStateForCurrentThread(key)&CoreVirtualKeyStates.Down)!=0;
    private void OnScannerTerminator(object sender,KeyRoutedEventArgs e)
    {if(e.Key is VirtualKey.Enter or VirtualKey.Tab&&CanCapture()&&FlushKeyboard())e.Handled=true;}
    private bool FlushKeyboard()
    {
        _keyboardIdle.Stop();var code=_keyboard.Flush();if(code is null||!ReportBarcode.IsReportCandidate(code)||!CanCapture())return false;
        if(FocusManager.GetFocusedElement(XamlRoot) is TextBox field&&field.Text.EndsWith(code,StringComparison.OrdinalIgnoreCase))field.Text=field.Text[..^code.Length];
        _searchDelay.Stop();_readerConnected=true;_readerLabel="Lector USB activo";SetScannerStatus("Lector USB activo",true);ReceiveScan(code);return true;
    }
    private void ReceiveScan(string code)
    {
        if(!_scannerActive)return;
        _receivedScans++;var received=DateTimeOffset.UtcNow.ToOffset(TimeSpan.FromHours(-6));
        var printable=new string(code.Where(c=>!char.IsControl(c)).Take(100).ToArray());
        _scannerActivity.Text=$"{_receivedScans:N0} lecturas recibidas · última {received:HH:mm:ss}\n{printable}";
        if(_scannerMessageOpen)return;
        if(!Has(PlanningPermissions.ReportMark)){ScanFeedback("Lectura recibida, pero no tiene permiso para marcar pruebas.",true);return;}
        if(_batchDialogActive){ScanFeedback("Esta lectura no se agregó al lote. Puedes continuar agregando JOBs desde el buscador.\n\nLas pruebas seleccionadas y la hora fija se conservan.",true,"LIMS · El lote sigue abierto");return;}
        if(!CanCapture()){ScanFeedback("Lectura recibida. Abra Digitación para marcar el JOB y la prueba.",true);return;}
        // A keyboard scan may have canceled the live search while its characters arrived.
        // Retire that request before writing; its late response still sees its canceled token.
        if(_request.IsCancellationRequested){_request.Dispose();_request=new();_loading=false;_progress.IsActive=false;_grid.SetBusy(false);UpdatePager();}
        if(_scanQueue.Count>=32){ScanFeedback("Lecturas pendientes: espere antes de volver a escanear.",true);return;}
        ScanFeedback("Lectura recibida · buscando JOB y prueba…",false);
        _scanQueue.Enqueue(code);_ = DrainScans();
    }
    private void ScanFeedback(string message,bool error,string? title=null,bool warning=false)
    {
        _scanReceipt.Text=error?"⚠  Lectura pendiente de revisión":message;_scanReceipt.Foreground=PlanningUi.Brush(error?"LimsDangerBrush":"LimsAccentBrush");
        ToolTipService.SetToolTip(_scanReceipt,message);AutomationProperties.SetItemStatus(this,message);
        Feedback(null);
        if(error)_ = ShowScannerMessage(title??(message.Contains("Puerto ocupado",StringComparison.OrdinalIgnoreCase)?"LIMS · Lector ocupado":"LIMS · Lectura no registrada"),message,warning);
    }
    private async Task ShowScannerMessage(string title,string message,bool warning)
    {
        if(_scannerMessageOpen||_disposed||!IsLoaded)return;
        _scannerMessageOpen=true;
        try
        {
            if(_batchDialog is{} batch)await batch.ShowScannerMessage(title,message,warning);
            else await ReportScannerDialog.Create(XamlRoot,title,message,warning).ShowAsync();
        }
        finally{_scannerMessageOpen=false;if(!_disposed&&_scannerActive)await DrainScans();}
    }
    private static string ScanError(string? error)
    {
        if(error?.Contains("no tiene una identidad de tracking",StringComparison.OrdinalIgnoreCase)==true
            ||error?.Contains("no aparece en las tablas de reportes de React",StringComparison.OrdinalIgnoreCase)==true)
            return "Reporte no encontrado.\n\nNo se pudo identificar el JOB y la prueba. No se marcó ninguna prueba.";
        if(error?.Contains("checksum",StringComparison.OrdinalIgnoreCase)==true||error?.Contains("inválido",StringComparison.OrdinalIgnoreCase)==true)
            return "Código de barras no válido. Vuelva a escanear el código del reporte.";
        return error??"El servidor rechazó la lectura. No se confirmó ninguna prueba marcada.";
    }
    private async Task DrainScans()
    {
        if(_draining||_scannerMessageOpen||!CanCapture())return;
        if(_loading||_mutating){_scanResume.Start();return;}
        _draining=true;
        try
        {
            while(_scanQueue.Count>0&&!_scannerMessageOpen&&CanCapture())
            {
                if(_loading||_mutating){_scanResume.Start();break;}
                await Scan(_scanQueue.Dequeue());
            }
        }
        finally{_draining=false;}
    }
}
