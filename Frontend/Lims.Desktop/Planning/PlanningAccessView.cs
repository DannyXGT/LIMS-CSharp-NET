using Lims.Contracts.Planning;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace Lims.Desktop.Planning;

public sealed class PlanningAccessView : Grid
{
    private readonly PlanningApiClient _api;
    private readonly ComboBox _users=new(){Header="Usuario activo",MinWidth=400};
    private readonly StackPanel _choices=new(){Spacing=8};
    private readonly TextBox _reason=PlanningUi.Field("Motivo del cambio de acceso",max:1000);
    private readonly TextBlock _message=PlanningUi.Text("",13,true);
    private IReadOnlyList<PlanningAccessUser> _items=[];
    public PlanningAccessView(PlanningApiClient api)
    {
        _api=api;var content=PlanningUi.Stack(PlanningUi.Text("Acceso a Planificación",22),PlanningUi.Text("Usa las cuentas y la sesión existentes. Los permisos de laboratorio se conservan.",13,true),_users,_choices,_reason,PlanningUi.Button("Guardar permisos",Save,true),_message);
        Children.Add(PlanningUi.Surface(content));_users.SelectionChanged+=(_,_)=>Choices();
    }
    public async Task Load()
    {var result=await _api.Users(CancellationToken.None);if(!result.IsSuccess){_message.Text=result.Error??"No se pudieron leer los usuarios.";_users.ItemsSource=null;return;}_items=result.Value??[];_users.ItemsSource=_items.Select(u=>$"{u.Name} · {u.Department}").ToArray();_users.SelectedIndex=_items.Count>0?0:-1;}
    private void Choices()
    {
        _choices.Children.Clear();if(_users.SelectedIndex<0||_users.SelectedIndex>=_items.Count)return;var user=_items[_users.SelectedIndex];
        foreach(var code in PlanningPermissions.All)_choices.Children.Add(new CheckBox{Content=PlanningLabels.Permission(code),Tag=code,IsChecked=user.Permissions.Contains(code,StringComparer.Ordinal)});
    }
    private async Task Save()
    {
        if(_users.SelectedIndex<0)return;var user=_items[_users.SelectedIndex];var codes=_choices.Children.OfType<CheckBox>().Where(c=>c.IsChecked==true).Select(c=>(string)c.Tag).ToArray();
        if(!await PlanningUi.Confirm(XamlRoot,"Guardar acceso",$"Se actualizarán los permisos de Planificación de {user.Name}. La API comprobará el acceso en cada solicitud."))return;
        var result=await _api.SaveAccess(user.Id,new(codes,_reason.Text),CancellationToken.None);_message.Text=result.IsSuccess?"Permisos guardados y auditados.":result.Error??"No se pudieron guardar.";if(result.IsSuccess)await Load();
    }
}
