using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HudiPet;

internal static class PetCatalogVerification
{
    internal static int Run(string directory)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Initialize();
        app.Startup+=async(_,_)=>
        {
            var checks=new Dictionary<string,bool>();string? error=null;
            try{await Verify(directory,checks);}catch(Exception ex){error=ex.ToString();}
            Directory.CreateDirectory(directory);bool success=error is null&&checks.Values.All(x=>x);
            File.WriteAllText(Path.Combine(directory,"pet-catalog-tests.json"),JsonSerializer.Serialize(new{success,checks,error},new JsonSerializerOptions{WriteIndented=true}));app.Shutdown(success?0:1);
        };
        return app.Run();
    }
    private static void Package(string path,string neutral="neutral",int width=4,bool missing=false,bool badHold=false)
    {
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        var steps=new[]{new{frame=neutral,seconds=.05},new{frame="second",seconds=.05}};
        var manifest=new
        {
            version="pet-1",name="Тестовый персонаж",frameSize=new[]{width,4},alphaPrepared=true,neutral,frames=new[]{neutral,"second"},
            clips=new Dictionary<string,object>{{"rest",steps},{"ponder",steps},{"celebrate",steps}},
            roles=new{lookaround="rest",think="ponder",dance="celebrate"},
            holds=badHold?new Dictionary<string,object>{{"think",new{start=38,end=48,seconds=3.2}}}:new Dictionary<string,object>()
        };
        using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))writer.Write(JsonSerializer.Serialize(manifest));
        foreach(string id in missing?new[]{neutral}:new[]{neutral,"second"})
        {
            byte[] pixels=new byte[4*4*4];for(int i=0;i<pixels.Length;i+=4){pixels[i]=(byte)(id==neutral?150:90);pixels[i+1]=160;pixels[i+2]=220;pixels[i+3]=255;}
            var image=BitmapSource.Create(4,4,96,96,PixelFormats.Bgra32,null,pixels,16);var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(image));using var encoded=new MemoryStream();png.Save(encoded);encoded.Position=0;using var output=zip.CreateEntry(id+".png").Open();encoded.CopyTo(output);
        }
    }
    private static async Task Verify(string directory,Dictionary<string,bool> checks)
    {
        Directory.CreateDirectory(directory);string testRoot=Path.Combine(Path.GetFullPath(directory),Guid.NewGuid().ToString("N"));Directory.CreateDirectory(testRoot);
        var catalog=new PetCatalog(Path.Combine(testRoot,"collection"));string valid=Path.Combine(testRoot,"valid.pet");Package(valid);
        var pet=await catalog.ImportAsync(valid);checks["import_and_list"]=catalog.List().Length==2&&pet.Name=="Тестовый персонаж";
        checks["duplicate_import_reused"]=(await catalog.ImportAsync(valid)).Id==pet.Id&&catalog.List().Length==2;
        catalog.Select(pet);checks["selection_persists"]=new PetCatalog(Path.Combine(testRoot,"collection")).Selected().Id==pet.Id;
        using(var library=catalog.Open(pet))
        {
            foreach(var check in RandomActionVerification.Run(library))checks["imported_"+check.Key]=check.Value;
            var engine=new PetBehavior(3,library);var visual=new PetVisual(library);
            for(int i=0;i<500;i++)visual.Apply(engine.Update(.05));
            checks["partial_action_set_runs"]=engine.ActionsStarted>0&&engine.SeenActions.All(engine.Supports);
            engine.BeginChatThinking();for(int i=0;i<500;i++)visual.Apply(engine.Update(.05));
            engine.EndChatThinking(true);for(int i=0;i<100;i++)visual.Apply(engine.Update(.05));
            checks["short_thinking_and_success_clips"]=engine.SeenActions.Contains(PetAction.Think)&&engine.SeenActions.Contains(PetAction.Dance);
            visual.Apply(new PetPose(library.NeutralFrame));var result=visual.Snapshot(4,4);byte[] pixels=new byte[64];result.CopyPixels(pixels,16,0);
            checks["imported_pixels_preserved"]=pixels.SequenceEqual(library.Pixels(library.NeutralFrame));
            checks["no_hudi_hold_indices"]=library.Holds.Count==0;
            checks["no_unrequested_body_stretch"]=library.BreathAmplitude==0;
            checks["imported_pet_does_not_inherit_hudi_or_ink_emotions"]=!engine.Supports(PetAction.Salute)&&!engine.Supports(PetAction.Temple)&&!engine.Supports(PetAction.Shrug);
            var hudi=new PetBehavior(3,SpriteLibrary.Shared.Value);
            checks["hudi_does_not_inherit_ink_emotions"]=!hudi.Supports(PetAction.Temple)&&!hudi.Supports(PetAction.Skeptical)&&!hudi.Supports(PetAction.Shrug);
        }
        foreach(var(name,neutral,width,missing,badHold) in new[]{("missing","neutral",4,true,false),("traversal","../escape",4,false,false),("oversize","neutral",999999,false,false),("hold","neutral",4,false,true)})
        {
            string file=Path.Combine(testRoot,name+".pet");Package(file,neutral,width,missing,badHold);bool rejected=false;
            try{catalog.Import(file);}catch(InvalidDataException){rejected=true;}
            checks[name+"_rejected_atomically"]=rejected&&catalog.List().Length==2&&catalog.Selected().Id==pet.Id;
        }
        string bad=Path.Combine(testRoot,"not-zip.pet");File.WriteAllText(bad,"not a zip");
        try{catalog.Import(bad);checks["invalid_zip_cleanup"]=false;}catch(InvalidDataException){checks["invalid_zip_cleanup"]=Directory.GetDirectories(Path.Combine(testRoot,"collection")).Length==1;}
        catalog.Select(PetCatalog.Hudi);
        var window=new PetWindow(false,true,catalog);
        try
        {
            await window.SelectPetAsync(pet);checks["live_selection_changes_renderer"]=window.PetName==pet.Name&&Math.Abs(window.Width-window.Height)<.01;
            await window.SelectPetAsync(PetCatalog.Hudi);checks["built_in_can_be_restored"]=window.PetName=="HUDI"&&Math.Abs(window.Width/window.Height-SpriteLibrary.Shared.Value.Width/(double)SpriteLibrary.Shared.Value.Height)<.01;
            await window.SelectPetAsync(pet);
        }
        finally{window.Close();}
        var reopened=new PetWindow(false,true,catalog);checks["runtime_remembers_selection"]=reopened.SelectedPetId==pet.Id;reopened.Close();
        try{catalog.Remove(PetCatalog.Hudi);checks["builtin_delete_rejected"]=false;}catch(InvalidDataException){checks["builtin_delete_rejected"]=true;}
        try{catalog.Remove(pet with {Package=valid});checks["outside_delete_rejected"]=false;}catch(InvalidDataException){checks["outside_delete_rejected"]=File.Exists(valid)&&catalog.List().Length==2;}
        var deleting=new PetWindow(false,true,catalog);
        try
        {
            await deleting.RemovePetAsync(pet);
            checks["active_delete_restores_hudi"]=deleting.SelectedPetId=="hudi"&&catalog.Selected().Id=="hudi";
            checks["deleted_pet_not_listed"]=catalog.List().Length==1&&!File.Exists(pet.Package);
            checks["deleted_package_recoverable"]=Directory.GetFiles(Path.Combine(testRoot,"collection","removed"),"pet.zip",SearchOption.AllDirectories).Length==1&&File.Exists(valid);
            var reimported=await catalog.ImportAsync(valid);
            await deleting.RemovePetAsync(reimported);
            checks["inactive_delete_keeps_selection"]=deleting.SelectedPetId=="hudi"&&catalog.List().Length==1;
        }
        finally{deleting.Close();}
        checks["activation_disabled"]=!Hudi.Licensing.LicenseCodec.ActivationRequired;
    }
}
