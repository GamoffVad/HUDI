using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
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
    internal static void Package(string path,string neutral="neutral",int width=4,bool missing=false,bool badHold=false,string displayName="Тестовый персонаж",string? petVersion=null,bool desktopEdges=false)
    {
        using var zip=ZipFile.Open(path,ZipArchiveMode.Create);
        var steps=new[]{new{frame=neutral,seconds=.05},new{frame="second",seconds=.05}};
        var clips=new Dictionary<string,object>{{"rest",steps},{"ponder",steps},{"celebrate",steps}};
        var edge=new{side="right",contactX=3,minX=0,maxX=3,minY=0,maxY=3,contactStart=0,contactEnd=1};
        if(desktopEdges){clips["edgelean"]=steps;clips["edgepeek"]=steps;}
        var manifest=new
        {
            version="pet-1",petVersion,name=displayName,frameSize=new[]{width,4},alphaPrepared=true,neutral,frames=new[]{neutral,"second"},
            clips,displayBounds=desktopEdges?new[]{0,0,4,3}:null,
            desktopEdge=desktopEdges?new{edgelean=edge,edgepeek=edge}:null,
            roles=new{lookaround="rest",think="ponder",dance="celebrate"},
            holds=badHold?new Dictionary<string,object>{{"think",new{start=38,end=48,seconds=3.2}}}:new Dictionary<string,object>()
        };
        using(var writer=new StreamWriter(zip.CreateEntry("manifest.json").Open()))writer.Write(JsonSerializer.Serialize(manifest,new JsonSerializerOptions{DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull}));
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
            checks["legacy_package_displays_its_fingerprint"]=library.PetVersion is null&&pet.VersionLabel(library)=="сборка "+pet.Hash[..8].ToLowerInvariant();
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
        // An externally imported pet must reach an already-created picker.
        var pickerCatalog=new PetCatalog(Path.Combine(testRoot,"picker-collection"));
        var pickerOwner=new PetWindow(false,true,pickerCatalog);
        new System.Windows.Interop.WindowInteropHelper(pickerOwner).EnsureHandle();
        var picker=new PetSettingsWindow(pickerOwner);
        try
        {
            var combo=picker.Body.Children.OfType<ComboBox>().Single();
            checks["picker_initial_catalog"]=combo.Items.Count==1;
            LocalPet? last=null;
            for(int i=0;i<7;i++)
            {
                string path=Path.Combine(testRoot,$"picker-{i}.pet");Package(path,neutral:$"neutral-{i}",displayName:$"Персонаж {i}");last=pickerCatalog.Import(path);
            }
            picker.RefreshAvailablePets();
            checks["picker_refreshes_external_imports"]=combo.Items.Count==8&&combo.Items.Cast<LocalPet>().Any(p=>p.Id==last!.Id);
            checks["picker_refresh_preserves_selection"]=combo.SelectedItem is LocalPet {Id:"hudi"}&&pickerCatalog.Selected().Id=="hudi";
            combo.ApplyTemplate();
            var popup=(Popup)combo.Template.FindName("PART_Popup",combo);
            var border=(Border)popup.Child;var scroll=(ScrollViewer)border.Child;
            checks["picker_popup_uses_configured_height"]=border.MaxHeight==420;
            checks["picker_long_list_scrolls"]=scroll.VerticalScrollBarVisibility==ScrollBarVisibility.Auto&&scroll.CanContentScroll;
            combo.SelectedItem=last;
            picker.RefreshAvailablePets();
            checks["picker_last_import_selectable"]=combo.SelectedItem is LocalPet p&&p.Id==last!.Id;
        }
        finally{picker.Close();pickerOwner.Close();}
        await VerifyDuplicates(testRoot,checks);
    }
    private static async Task VerifyDuplicates(string testRoot,Dictionary<string,bool> checks)
    {
        string root=Path.Combine(testRoot,"dedup-collection");var catalog=new PetCatalog(root);
        string first=Path.Combine(testRoot,"revision-first.pet"),second=Path.Combine(testRoot,"revision-second.pet");
        Package(first,displayName:"Розовая девочка",petVersion:"1.0.0");Package(second,neutral:"updated",displayName:"  РОЗОВАЯ   ДЕВОЧКА  ",petVersion:"1.1.0");
        var pet=catalog.Import(first);catalog.Select(pet);
        using(var live=catalog.Open(pet))
        {
            var updated=catalog.Import(second);
            using var version=catalog.Open(updated);
            checks["same_name_update_keeps_id"]=updated.Id==pet.Id&&catalog.List().Length==2;
            checks["active_package_update_is_atomic"]=version.NeutralFrame=="updated"&&live.NeutralFrame=="neutral"&&File.Exists(pet.Package);
            checks["pet_version_changes_independently"]=pet.VersionLabel(live)=="1.0.0"&&updated.VersionLabel(version)=="1.1.0"&&Version.TryParse(AppRelease.Version,out _);
            checks["update_preserves_selection"]=catalog.Selected().Id==pet.Id;
            checks["updated_package_reimport_reused"]=catalog.Import(second).Package==updated.Package;
            pet=updated;
        }
        var window=new PetWindow(false,true,catalog);
        try
        {
            var updatedAgain=catalog.Import(first);
            checks["active_and_installed_revisions_are_distinguished"]=window.HasPetUpdate(updatedAgain)&&window.ActivePetVersion=="1.1.0";
            await window.SelectPetAsync(updatedAgain);
            checks["active_same_id_revision_can_be_selected"]=window.SelectedPetId==pet.Id&&window.PetName=="Розовая девочка"&&catalog.Selected().Hash==updatedAgain.Hash;
            checks["active_version_updates_without_changing_identity"]=!window.HasPetUpdate(updatedAgain)&&window.ActivePetVersion=="1.0.0";
        }
        finally{window.Close();}
        pet=catalog.Selected();
        string clone=Guid.NewGuid().ToString("N"),cloneFolder=Path.Combine(root,clone);Directory.CreateDirectory(cloneFolder);
        File.Copy(pet.Package!,Path.Combine(cloneFolder,"pet.zip"));
        File.WriteAllText(Path.Combine(cloneFolder,"pet.json"),JsonSerializer.Serialize(new{name=pet.Name,sha256=pet.Hash.ToLowerInvariant()}));
        File.WriteAllText(Path.Combine(root,"selected.txt"),clone);
        var repaired=new PetCatalog(root);
        checks["legacy_duplicate_migration_preserves_selected_id"]=repaired.Selected().Id==clone&&repaired.List().Length==2;
        checks["legacy_duplicates_archived_recoverably"]=Directory.Exists(Path.Combine(root,"duplicates"))&&!Directory.Exists(Path.Combine(root,pet.Id));
        checks["repeated_migration_idempotent"]=new PetCatalog(root).List().Length==2;
        repaired.Select(PetCatalog.Hudi);repaired.Remove(repaired.List().Single(p=>!p.BuiltIn));
        checks["removed_duplicate_does_not_reappear"]=new PetCatalog(root).List().Length==1;
        string concurrentRoot=Path.Combine(testRoot,"concurrent-collection");
        var a=new PetCatalog(concurrentRoot);var b=new PetCatalog(concurrentRoot);
        var imported=await Task.WhenAll(a.ImportAsync(first),b.ImportAsync(first));
        checks["concurrent_duplicate_imports_serialize"]=imported[0].Id==imported[1].Id&&new PetCatalog(concurrentRoot).List().Length==2;
        string reserved=Path.Combine(testRoot,"reserved.pet");Package(reserved,displayName:"hudi");
        try{a.Import(reserved);checks["builtin_name_cannot_be_duplicated"]=false;}
        catch(InvalidDataException){checks["builtin_name_cannot_be_duplicated"]=a.List().Length==2;}
        string versionId=Guid.NewGuid().ToString("N"),versionFolder=Path.Combine(concurrentRoot,versionId);Directory.CreateDirectory(versionFolder);
        File.Copy(second,Path.Combine(versionFolder,"pet.zip"));
        File.SetLastWriteTimeUtc(Path.Combine(versionFolder,"pet.zip"),DateTime.UtcNow.AddMinutes(1));
        File.WriteAllText(Path.Combine(versionFolder,"pet.json"),JsonSerializer.Serialize(new{name="Розовая девочка",sha256="different-version"}));
        var latest=new PetCatalog(concurrentRoot);
        checks["legacy_same_name_versions_keep_newest"]=latest.List().Length==2&&latest.List()[1].Id==versionId;
    }
}
