using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media.Imaging;

namespace HudiPet;

// Package authoring commands use the same importer and renderer as the desktop UI.
// Import adds to the collection; selecting a different pet remains an explicit UI action.
internal static class PetPackageCommands
{
    private static void Report(string path,object value)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path,JsonSerializer.Serialize(value,new JsonSerializerOptions{WriteIndented=true}));
    }
    internal static int Import(string package,string report)
    {
        try
        {
            var catalog=new PetCatalog();string before=catalog.Selected().Id;
            var pet=catalog.Import(package);
            Report(report,new{success=true,pet.Id,pet.Name,pet.Package,pet.Hash,selectedPetUnchanged=catalog.Selected().Id==before});
            return 0;
        }
        catch(Exception ex){Report(report,new{success=false,error=ex.Message});return 1;}
    }
    internal static int Verify(string package,string directory)
    {
        Directory.CreateDirectory(directory);
        var checks=new Dictionary<string,bool>();var actions=new List<object>();string? error=null;
        try
        {
            var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Initialize();
            using var library=SpriteLibrary.Open(package);library.ValidateFrames();
            checks["all_png_frames_decode"]=true;
            var visual=new PetVisual(library);
            void Save(string name)
            {
                var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(visual.Snapshot(library.Width,library.Height)));
                using var file=File.Create(Path.Combine(directory,name+".png"));png.Save(file);
            }
            foreach(var action in Enum.GetValues<PetAction>())
            {
                var engine=new PetBehavior(7,library);if(!engine.Supports(action))continue;
                engine.Trigger(action);double elapsed=0;int ticks=0;
                // Bound an author-supplied clip while allowing the longest supported hold.
                double limit=library.Clips[PetBehavior.ClipName(action)].Sum(step=>step.Seconds)+70;
                do
                {
                    visual.Apply(engine.Update(.05,autonomous:false));elapsed+=.05;ticks++;
                    if(ticks==1)Save(action+"-entry");
                    if(ticks==30)Save(action+"-motion");
                }while(engine.IsPlaying&&elapsed<limit);
                Save(action+"-end");checks[action+"_completes"]=!engine.IsPlaying&&engine.SeenActions.Contains(action);
                actions.Add(new{action=action.ToString(),elapsed,ticks});
            }
            checks["at_least_one_supported_action"]=actions.Count>0;
            var thinking=new PetBehavior(11,library);thinking.BeginChatThinking();
            for(int i=0;i<500;i++)visual.Apply(thinking.Update(.05));
            thinking.EndChatThinking(true);
            for(int i=0;i<800;i++)visual.Apply(thinking.Update(.05));
            checks["chat_actions_supported"]=(!thinking.Supports(PetAction.Think)||thinking.SeenActions.Contains(PetAction.Think))&&(!thinking.Supports(PetAction.Dance)||thinking.SeenActions.Contains(PetAction.Dance));
            visual.Apply(new PetPose(library.NeutralFrame));Save("neutral-rendered");
            if(!library.HorizontalMirrored)
            {
                byte[] actual=new byte[library.Width*library.Height*4];visual.Snapshot(library.Width,library.Height).CopyPixels(actual,library.Width*4,0);
                byte[] expected=library.Pixels(library.NeutralFrame);
                checks["source_pixels_preserved"]=actual.Zip(expected,(a,b)=>Math.Abs(a-b)).Max()<=1;
            }
            Report(Path.Combine(directory,"package.json"),new{library.DisplayName,library.Width,library.Height,frames=library.FrameIds.Length,library.HorizontalMirrored,actions});
        }
        catch(Exception ex){error=ex.ToString();}
        bool success=error is null&&checks.Values.All(value=>value);
        Report(Path.Combine(directory,"runtime-checks.json"),new{success,checks,error});return success?0:1;
    }
}
