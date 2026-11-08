using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using System.Text.Json;
using Microsoft.Win32;
using Hudi.Licensing;

namespace Hudi.Distribution;
internal static class InstallEngine
{
    internal static string Root=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs","HUDI");
    internal static string Exe=>Path.Combine(Root,"HUDI.exe");
    private const string UninstallKey=@"Software\Microsoft\Windows\CurrentVersion\Uninstall\HUDI";
    internal static string StartShortcut=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Programs),"HUDI.lnk");
    internal static string DesktopShortcut=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),"HUDI.lnk");
    internal static string StartupShortcut=>Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Startup),"HUDI.lnk");
    private static Stream Resource(string name)=>Assembly.GetExecutingAssembly().GetManifestResourceStream(name)??throw new IOException("Не найден ресурс установщика.");
    internal static async Task Install(string code,IProgress<double>? progress,bool desktop,bool startup,string? testRoot=null)
    {
        if(LicenseCodec.ActivationRequired && !LicenseCodec.Validate(code,LicenseCodec.PublicKey,out _,out var error))throw new InvalidOperationException(error);
        string root=Path.GetFullPath(testRoot??Root);Directory.CreateDirectory(root);
        string target=Path.Combine(root,"HUDI.exe"),temporary=Path.Combine(root,"HUDI.pending-"+Guid.NewGuid().ToString("N"));
        // Files are staged and verified before replacing a runnable release.
        using(var input=Resource("HUDI.payload.exe"))
        await using(var output=new FileStream(temporary,FileMode.CreateNew,FileAccess.Write,FileShare.None,131072,true))
        {
            byte[] buffer=new byte[131072];long written=0;int count;
            while((count=await input.ReadAsync(buffer))>0){await output.WriteAsync(buffer.AsMemory(0,count));written+=count;progress?.Report(.1+.7*written/input.Length);}
        }
        using(var source=Resource("HUDI.payload.sha256"))
        using(var expected=new StreamReader(source))
        await using(var staged=File.OpenRead(temporary))
        {
            string hash=Convert.ToHexString(await SHA256.HashDataAsync(staged));
            if(!hash.Equals((await expected.ReadToEndAsync()).Trim(),StringComparison.OrdinalIgnoreCase))throw new IOException("Проверка файла не прошла. Получите установщик заново.");
        }
        if(testRoot is null)StopInstalled();
        string? oldLicense=testRoot is null&&File.Exists(LicenseCodec.LicensePath)?File.ReadAllText(LicenseCodec.LicensePath):null;
        string previous=target+".previous";bool hadPrevious=File.Exists(target);
        if(hadPrevious)File.Copy(target,previous,true);
        try
        {
            File.Move(temporary,target,true);
            using(var source=Resource("HUDI.uninstall.exe"))using(var output=File.Create(Path.Combine(root,"HUDI.Uninstall.exe")))source.CopyTo(output);
            if(testRoot is null)
            {
                if(LicenseCodec.ActivationRequired)LicenseCodec.Save(code);CreateShortcut(StartShortcut);
                if(desktop)CreateShortcut(DesktopShortcut);
                if(startup)CreateShortcut(StartupShortcut);
                using var key=Registry.CurrentUser.CreateSubKey(UninstallKey);
                key.SetValue("DisplayName","HUDI — питомец рабочего стола");key.SetValue("DisplayVersion","7.1.14");key.SetValue("Publisher","HUDI");
                key.SetValue("InstallLocation",root);key.SetValue("DisplayIcon",target);key.SetValue("UninstallString","\""+Path.Combine(root,"HUDI.Uninstall.exe")+"\"");
                key.SetValue("NoModify",1);key.SetValue("NoRepair",1);key.SetValue("EstimatedSize",(int)(new FileInfo(target).Length/1024),RegistryValueKind.DWord);
                File.WriteAllText(Path.Combine(root,"installation.json"),JsonSerializer.Serialize(new{version="7.1.14",desktop,startup,installed=DateTimeOffset.UtcNow}));
            }
            progress?.Report(1);
        }
        catch
        {
            if(hadPrevious)File.Copy(previous,target,true);
            if(testRoot is null&&oldLicense is not null)File.WriteAllText(LicenseCodec.LicensePath,oldLicense);
            throw;
        }
    }
    internal static void CreateShortcut(string path)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        Type type=Type.GetTypeFromProgID("WScript.Shell")??throw new IOException("Не удалось создать ярлык.");
        dynamic shell=Activator.CreateInstance(type)!;
        try{dynamic shortcut=shell.CreateShortcut(path);try{shortcut.TargetPath=Exe;shortcut.WorkingDirectory=Root;shortcut.Description="HUDI — ваш компаньон";shortcut.IconLocation=Exe+",0";shortcut.Save();}finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);}}
        finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
    }
    internal static void StopInstalled()
    {
        foreach(var process in Process.GetProcessesByName("HUDI"))using(process)
        {
            try
            {
                if(!string.Equals(process.MainModule?.FileName,Exe,StringComparison.OrdinalIgnoreCase))continue;
                process.CloseMainWindow();if(!process.WaitForExit(4000))throw new IOException("Закройте установленный HUDI и повторите установку.");
            }
            catch(System.ComponentModel.Win32Exception) { }
        }
    }
    internal static void Uninstall()
    {
        // Only remove this product's known install tree; never accept a caller-supplied path.
        string root=Path.GetFullPath(Root);string programs=Path.GetFullPath(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),"Programs"))+Path.DirectorySeparatorChar;
        if(!root.StartsWith(programs,StringComparison.OrdinalIgnoreCase)||Path.GetFileName(root)!="HUDI")throw new IOException("Неверная папка удаления.");
        if(Directory.Exists(root)&&(File.GetAttributes(root)&FileAttributes.ReparsePoint)!=0)throw new IOException("Папка установки является ссылкой.");
        StopInstalled();
        if(Directory.Exists(root))
        {
            foreach(var file in new[]{"HUDI.exe","HUDI.exe.previous","HUDI.Uninstall.exe","installation.json"})File.Delete(Path.Combine(root,file));
            if(!Directory.EnumerateFileSystemEntries(root).Any())Directory.Delete(root);
        }
        foreach(string path in new[]{StartShortcut,DesktopShortcut,StartupShortcut})
        {
            if(!File.Exists(path))continue;
            dynamic shell=Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
            try{dynamic shortcut=shell.CreateShortcut(path);try{if(string.Equals((string)shortcut.TargetPath,Exe,StringComparison.OrdinalIgnoreCase))File.Delete(path);}finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shortcut);}}
            finally{System.Runtime.InteropServices.Marshal.FinalReleaseComObject(shell);}
        }
        Registry.CurrentUser.DeleteSubKeyTree(UninstallKey,false);
        // User settings, encrypted API credentials and license intentionally survive reinstall.
    }
}
