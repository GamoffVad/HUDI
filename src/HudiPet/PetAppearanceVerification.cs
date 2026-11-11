using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hudi.Distribution;

namespace HudiPet;

internal static class PetAppearanceVerification
{
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Initialize();
        var checks=new Dictionary<string,bool>();string? error=null;
        try
        {
            string fixture=Path.Combine(directory,Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
            var catalog=new PetCatalog(Path.Combine(fixture,"pets"));
            var cards=new StackPanel{Margin=new Thickness(16)};
            var themes=new List<PetAppearance>();
            foreach(string name in new[]{"HUDI","Девочка в розовом","Девушка в очках","Инк","Кудряшка"})
            {
                LocalPet pet=PetCatalog.Hudi;
                if(name!="HUDI"){string package=Path.Combine(fixture,name+".pet");PetCatalogVerification.Package(package,displayName:name);pet=catalog.Import(package);}
                var theme=catalog.Appearance(pet);themes.Add(theme);theme.Apply();
                checks[name+"_ten_distinct_icons"]=theme.Icons.Length==10&&theme.Icons.Select(i=>i.Image).Distinct().Count()==10;
                checks[name+"_theme_survives_reopen"]=new PetCatalog(Path.Combine(fixture,"pets")).Appearance(pet).Serialize()==theme.Serialize();
                if(!pet.BuiltIn)
                {
                    string exported=Path.Combine(fixture,name+"-export.pet");catalog.ExportWithAppearance(pet,exported);
                    var other=new PetCatalog(Path.Combine(fixture,"import-"+pet.Id));var imported=other.Import(exported);
                    checks[name+"_portable_theme_and_icons"]=other.Appearance(imported).Serialize()==theme.Serialize();
                    using var zip=ZipFile.OpenRead(exported);checks[name+"_animations_preserved"]=zip.GetEntry("neutral.png") is not null&&zip.GetEntry("appearance.json") is not null;
                }
                var body=new StackPanel();var title=Ui.Text(name,18,theme.Ink);title.Foreground=theme.Ink;body.Children.Add(title);
                var icons=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(0,12,0,10)};
                foreach(var icon in theme.Icons)
                {
                    var tile=new StackPanel{Width=78};tile.Children.Add(SiteOrb.CreateFace(icon.Image,null,52));
                    tile.Children.Add(new TextBlock{Text=icon.Name,FontSize=10,Foreground=theme.Ink,TextAlignment=TextAlignment.Center,Margin=new Thickness(0,6,0,0)});icons.Children.Add(tile);
                }
                body.Children.Add(icons);cards.Children.Add(new Border{Background=theme.Panel,BorderBrush=theme.Edge,BorderThickness=new Thickness(1),Padding=new Thickness(14),Margin=new Thickness(0,0,0,12),Child=body});
            }
            checks["each_pet_has_own_palette"]=themes.Select(t=>t.Accent).Distinct().Count()==5;
            checks["built_in_symbols_recolor_with_pet"]=themes.Select(t=>t.Icons[0].Image).Distinct().Count()==5;
            var sample=new FavoriteSite(Guid.NewGuid().ToString("N"),"Почта","https://example.com",themes[0].Icons[2].Image,LibraryIcon:"mail");
            checks["library_selection_follows_current_pet"]=themes.All(t=>t.IconFor(sample)==t.Icons.Single(i=>i.Id=="mail").Image);
            checks["custom_icon_is_preserved"]=themes.All(t=>t.IconFor(sample with{LibraryIcon=null})==sample.Icon);
            var store=new FavoriteSitesStore(Path.Combine(fixture,"favorites"));store.Put(sample);
            checks["selected_library_symbol_survives_restart"]=new FavoriteSitesStore(Path.Combine(fixture,"favorites")).Read().Single().LibraryIcon=="mail";
            var preview=new Window{Width=850,SizeToContent=SizeToContent.Height,Content=cards,Background=new SolidColorBrush(Color.FromRgb(24,24,27)),ShowInTaskbar=false};
            VisualPreview.Save(preview,Path.Combine(directory,"all-pet-icon-libraries.png"));preview.Close();
            var invalid=themes[1] with{Icons=themes[1].Icons[..9]};
            bool rejected=false;try{using var input=new MemoryStream(System.Text.Encoding.UTF8.GetBytes(invalid.Serialize()));PetAppearance.Parse(input);}catch(InvalidDataException){rejected=true;}
            checks["invalid_library_rejected"]=rejected;
        }
        catch(Exception ex){error=ex.ToString();}
        bool success=error is null&&checks.Values.All(v=>v);File.WriteAllText(Path.Combine(directory,"appearance-tests.json"),JsonSerializer.Serialize(new{success,checks,error},new JsonSerializerOptions{WriteIndented=true}));
        app.Shutdown();return success?0:1;
    }
}
