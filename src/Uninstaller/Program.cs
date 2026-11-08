using System.Diagnostics;
using System.IO;
using System.Windows;
using Hudi.Distribution;

namespace Hudi.Uninstall;
internal static class Program
{
    [STAThread]private static int Main(string[] args)
    {
        if(string.Equals(Path.GetDirectoryName(Environment.ProcessPath),InstallEngine.Root,StringComparison.OrdinalIgnoreCase))
        {
            string path=Path.Combine(Path.GetTempPath(),"HUDI-Uninstall-"+Guid.NewGuid().ToString("N")+".exe");File.Copy(Environment.ProcessPath!,path);Process.Start(new ProcessStartInfo(path){UseShellExecute=true});return 0;
        }
        var app=new Application();BrandWindow.Initialize();var window=new BrandWindow("Удалить HUDI?","Приложение и его ярлыки будут удалены. Лицензия и настройки модели сохранятся для повторной установки.");
        var status=BrandWindow.Text("",12,BrandWindow.Muted);status.Margin=new Thickness(0,20,0,20);window.Body.Children.Add(status);
        window.Body.Children.Add(BrandWindow.Button("Удалить приложение",()=>
        {
            try{InstallEngine.Uninstall();window.TitleText.Text="HUDI удалён";window.Subtitle.Text="Ваши настройки сохранены.";window.Body.Children.Clear();window.Body.Children.Add(BrandWindow.Button("Закрыть",window.Close,true));}
            catch(Exception ex) when(ex is IOException or UnauthorizedAccessException or System.Runtime.InteropServices.COMException){status.Text="Не удалось завершить удаление. Закройте HUDI и попробуйте ещё раз.";}
        },true));return app.Run(window);
    }
}
