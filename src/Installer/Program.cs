using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hudi.Distribution;
using Hudi.Licensing;
using Microsoft.Win32;

namespace Hudi.Setup;
internal static class Program
{
    [STAThread]private static int Main(string[] args)
    {
        var app=new Application();BrandWindow.Initialize();
        if(args.Length==2&&args[0]=="--render-ui"){VisualPreview.Save(new SetupWindow(),args[1]);return 0;}
        if((args.Length==2||args.Length==3)&&args[0]=="--verify-install")
        {
            try{InstallEngine.Install(args.Length==3?File.ReadAllText(args[1]):"",null,false,false,Path.GetFullPath(args[^1])).GetAwaiter().GetResult();return 0;}catch{return 1;}
        }
        return app.Run(new SetupWindow());
    }
}
internal sealed class SetupWindow:BrandWindow
{
    private TextBox code=null!;private TextBlock status=null!;private Button install=null!;
    private CheckBox desktop=null!,startup=null!;private bool installing;
    internal SetupWindow():base("Поселите HUDI рядом","Живые движения, эмоции и ваш AI-помощник — прямо на рабочем столе.")
    {
        code=Input();code.AcceptsReturn=true;code.TextWrapping=TextWrapping.Wrap;code.Height=92;code.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;code.FontSize=12;
        if(LicenseCodec.ActivationRequired){Field(Body,"Код активации",code);var openCode=Button("Открыть файл с кодом…",LoadCode);openCode.Margin=new Thickness(0,12,0,0);Body.Children.Add(openCode);}
        desktop=new CheckBox{Content="Ярлык на рабочем столе",IsChecked=true,Margin=new Thickness(0,16,0,0)};
        startup=new CheckBox{Content="Запускать вместе с Windows",IsChecked=false};Body.Children.Add(desktop);Body.Children.Add(startup);
        status=Text(LicenseCodec.ActivationRequired?"Для установки не нужны права администратора.\nИнтернет для активации не требуется.":"Установка без кода активации.\nПрава администратора не нужны.",12,Muted);status.Margin=new Thickness(0,18,0,18);Body.Children.Add(status);
        install=Button(LicenseCodec.ActivationRequired?"Активировать и установить  ↗":"Установить HUDI  ↗",()=>_ = Install(),true);Body.Children.Add(install);
        var path=Text("Папка установки: "+InstallEngine.Root,10,Muted);path.Margin=new Thickness(0,12,0,0);Body.Children.Add(path);
        Closing+=(_,e)=>{if(installing)e.Cancel=true;};
    }
    private void LoadCode()
    {
        var dialog=new OpenFileDialog{Filter="Код питомца (*.pet-key;*.hudi-key;*.key;*.txt)|*.pet-key;*.hudi-key;*.key;*.txt"};
        try{if(dialog.ShowDialog(this)==true){if(new FileInfo(dialog.FileName).Length>8192)throw new IOException();code.Text=File.ReadAllText(dialog.FileName);}}
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException){status.Text="Не удалось прочитать код из файла.";}
    }
    private async Task Install()
    {
        LicenseClaims? claims=null;
        if(LicenseCodec.ActivationRequired&&!LicenseCodec.Validate(code.Text,LicenseCodec.PublicKey,out claims,out string error)){status.Text=error;return;}
        installing=true;install.IsEnabled=CloseButton.IsEnabled=false;code.IsEnabled=desktop.IsEnabled=startup.IsEnabled=false;
        try
        {
            string license=code.Text;bool shortcut=desktop.IsChecked==true,autorun=startup.IsChecked==true;
            status.Text="Проверяю и устанавливаю…";
            await InstallEngine.Install(license,new Progress<double>(value=>status.Text=$"Устанавливаю HUDI · {value:P0}"),shortcut,autorun);
            TitleText.Text="HUDI готов к знакомству";Subtitle.Text="Двойной клик — заметки. Тройной — чат. Правый — меню.";Body.Children.Clear();
            var ready=Text(LicenseCodec.ActivationRequired?"Активация завершена\n"+claims!.Customer:"Установка завершена",15,Accent);ready.Margin=new Thickness(0,22,0,14);Body.Children.Add(ready);
            Body.Children.Add(Text("Подключите свою модель в разделе «Модель и API». Новых персонажей можно добавить в настройках «Питомцы».",13,Muted));
            var launch=Button("Запустить HUDI  ↗",()=>
            {
                try{Process.Start(new ProcessStartInfo(InstallEngine.Exe,"--show-settings"){UseShellExecute=true});Close();}
                catch(Exception ex) when(ex is System.ComponentModel.Win32Exception or IOException){ready.Text="Не удалось запустить. Используйте ярлык HUDI.";}
            },true);launch.Margin=new Thickness(0,24,0,0);Body.Children.Add(launch);
        }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Runtime.InteropServices.COMException)
        {status.Text=ex is InvalidOperationException?ex.Message:"Установка не завершилась. Закройте работающий HUDI и проверьте свободное место.";install.IsEnabled=true;code.IsEnabled=desktop.IsEnabled=startup.IsEnabled=true;}
        finally{installing=false;CloseButton.IsEnabled=true;}
    }
}
