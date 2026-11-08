using System.IO;
using System.Text.Json;

namespace HudiPet;

internal static class FavoriteProgramsVerification
{
    internal static void Run(string fixture,string icon,Dictionary<string,bool> checks)
    {
        string directory=Path.Combine(fixture,"programs "+Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var legacy=new{Id=Guid.NewGuid().ToString("N"),Name="Старый сайт",Url="https://example.com/",Icon=icon,Text="Сайт"};
        File.WriteAllText(Path.Combine(directory,"favorite-sites.json"),JsonSerializer.Serialize(new{Version=1,Sites=new[]{legacy},MaximumSphereSize=88}));
        var store=new FavoriteSitesStore(directory);var site=store.Read().Single();
        checks["programs_legacy_missing_kind_still_opens_website"]=!FavoriteSitesStore.IsProgram(site)&&FavoriteSitesStore.CreateLaunchInfo(site).FileName==legacy.Url;
        // Empty files are deliberately never launched: only the launch description is inspected.
        string executable=Path.Combine(directory,"Программа с пробелами.exe"),shortcut=Path.Combine(directory,"Мой ярлык.lnk");
        File.WriteAllBytes(executable,[]);File.WriteAllBytes(shortcut,[]);
        var program=new FavoriteSite(Guid.NewGuid().ToString("N"),"Программа",executable,icon,"APP","program");
        var link=new FavoriteSite(Guid.NewGuid().ToString("N"),"Ярлык",shortcut,"","Ярлык","program");
        store.Put(program);store.Put(link);
        var reloaded=new FavoriteSitesStore(directory);var expected=reloaded.Read();
        checks["programs_exe_lnk_sites_text_icons_and_size_round_trip"]=expected.SequenceEqual(new[]{site,program,link})&&reloaded.MaximumSphereSize==88;
        foreach(var item in new[]{program,link})
        {
            var start=FavoriteSitesStore.CreateLaunchInfo(item);
            checks["programs_literal_launch_"+Path.GetExtension(item.Url)]=start.FileName==item.Url&&start.Arguments.Length==0&&start.ArgumentList.Count==0&&start.UseShellExecute&&start.WorkingDirectory==directory;
        }
        checks["programs_quoted_picker_path_normalizes"]=FavoriteSitesStore.NormalizeProgramPath("\""+executable+"\"")==executable;
        checks["programs_mixed_slash_path_normalizes"]=FavoriteSitesStore.NormalizeProgramPath(executable.Replace('\\','/'))==executable;
        File.Delete(executable);
        checks["programs_missing_executable_keeps_entire_saved_list"]=reloaded.Read().SequenceEqual(expected)&&reloaded.MaximumSphereSize==88;
        bool missing=false;try{FavoriteSitesStore.CreateLaunchInfo(program);}catch(FileNotFoundException){missing=true;}
        checks["programs_missing_executable_fails_before_launch"]=missing;
        foreach(var invalid in new[]{
            ("relative",program with{Url="relative.exe"}),
            ("script",program with{Url=Path.Combine(directory,"run.ps1")}),
            ("arguments",program with{Url=shortcut+" --argument"}),
            ("unknown_kind",program with{Kind="unknown"})})
        {
            bool rejected=false;try{store.Put(invalid.Item2);}catch(InvalidDataException){rejected=true;}
            checks["programs_reject_"+invalid.Item1+"_without_data_loss"]=rejected&&store.Read().SequenceEqual(expected);
        }
    }
}
