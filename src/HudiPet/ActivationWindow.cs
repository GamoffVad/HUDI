using System.IO;
using System.Windows;
using System.Windows.Controls;
using Hudi.Licensing;

namespace HudiPet;
internal sealed class ActivationWindow:GlassWindow
{
    internal ActivationWindow():base(null,"Активация HUDI",450)
    {
        WindowStartupLocation=WindowStartupLocation.CenterScreen;ShowInTaskbar=true;
        Body.Children.Add(Ui.Text("Введите код, полученный при покупке питомца. Подключение к интернету не требуется.",13,Ui.Muted));
        var code=Ui.Input("","Код активации");code.TextWrapping=TextWrapping.Wrap;code.Height=100;code.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;Ui.Field(Body,"Код активации",code);
        var status=Ui.Text("",12,Ui.Error);status.Margin=new Thickness(0,12,0,12);Body.Children.Add(status);
        Body.Children.Add(Ui.Button("Активировать",()=>
        {
            try{LicenseCodec.Save(code.Text);DialogResult=true;}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException){status.Text=ex is InvalidOperationException?ex.Message:"Не удалось сохранить активацию.";}
        },true));
    }
}
