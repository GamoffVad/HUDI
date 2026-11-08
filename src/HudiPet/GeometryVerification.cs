using System.Windows;

namespace HudiPet;

internal static class GeometryVerification
{
    private static bool Near(double a,double b)=>Math.Abs(a-b)<.000001;
    private static bool Same(Rect a,Rect b)=>Near(a.X,b.X)&&Near(a.Y,b.Y)&&Near(a.Width,b.Width)&&Near(a.Height,b.Height);
    internal static Dictionary<string,bool> Run()
    {
        var checks=new Dictionary<string,bool>();
        var canvas=new Size(512,560);var viewport=new Rect(64,48,384,500);
        var legacy=new PetWindow.Settings{Left=2972,Top=1178,Height=246};
        var restored=PetWindow.RestoreWindowBounds(legacy,true,canvas,viewport);
        checks["legacy_padding_preserves_body_position_and_scale"]=Same(restored,new Rect(2940.512,1154.384,251.904,275.52));
        var saved=new PetWindow.Settings
        {
            Left=restored.Left,Top=restored.Top,Height=restored.Height,PetId="hudi",
            CanvasWidth=512,CanvasHeight=560,DisplayX=64,DisplayY=48,DisplayWidth=384,DisplayHeight=500
        };
        checks["saved_padding_is_not_applied_twice"]=Same(restored,PetWindow.RestoreWindowBounds(saved,true,canvas,viewport));

        var smokeCanvas=new Size(640,640);var smokeViewport=new Rect(128,80,384,500);
        var migrated=PetWindow.RestoreWindowBounds(saved,true,smokeCanvas,smokeViewport);
        checks["v731_smoke_canvas_preserves_v73_body_bounds"]=Same(migrated,new Rect(2909.024,1138.64,314.88,314.88));
        double migratedScale=migrated.Height/smokeCanvas.Height;
        checks["v731_smoke_canvas_preserves_body_height_feet_and_center"]=Near(migratedScale*smokeViewport.Height,246)&&
            Near(migrated.Top+smokeViewport.Bottom*migratedScale,1424)&&
            Near(migrated.Left+(smokeViewport.X+smokeViewport.Width/2)*migratedScale,3066.464);
        var savedSmoke=new PetWindow.Settings
        {
            Left=migrated.Left,Top=migrated.Top,Height=migrated.Height,PetId="hudi",
            CanvasWidth=640,CanvasHeight=640,DisplayX=128,DisplayY=80,DisplayWidth=384,DisplayHeight=500
        };
        checks["v731_save_reload_does_not_add_padding_again"]=Same(migrated,PetWindow.RestoreWindowBounds(savedSmoke,true,smokeCanvas,smokeViewport));
        checks["v731_legacy_and_v73_settings_land_on_same_body"]=Same(migrated,PetWindow.RestoreWindowBounds(legacy,true,smokeCanvas,smokeViewport));

        var oldViewport=new Rect(2972,1178,188.928,246);
        checks["explicit_viewport_mapping_preserves_physical_bounds"]=Same(restored,PetWindow.WindowForDisplay(oldViewport,canvas,viewport));
        var resized=PetWindow.WindowForDisplay(new Rect(oldViewport.Left,oldViewport.Bottom-540,oldViewport.Width,540),canvas,viewport);
        checks["size_change_keeps_feet_and_horizontal_center"]=Near(resized.Bottom-(canvas.Height-viewport.Bottom)*resized.Height/canvas.Height,oldViewport.Bottom)&&
            Near(resized.Left+(viewport.X+viewport.Width/2)*resized.Height/canvas.Height,oldViewport.Left+oldViewport.Width/2)&&
            Near(resized.Height*viewport.Height/canvas.Height,540);

        var customCanvas=new Size(300,400);var customViewport=new Rect(0,0,300,400);
        var custom=PetWindow.RestoreWindowBounds(new PetWindow.Settings{Left=90,Top=110,Height=246},false,customCanvas,customViewport);
        checks["legacy_custom_pet_does_not_receive_hudi_padding"]=Same(custom,new Rect(90,110,184.5,246));
        var switched=PetWindow.RestoreWindowBounds(saved,false,new Size(600,600),new Rect(0,0,600,600));
        checks["different_pet_canvas_keeps_physical_height_and_anchor"]=Same(switched,new Rect(2943.464,1178,246,246));

        var invalid=new PetWindow.Settings{Left=2972,Top=1178,Height=246,CanvasWidth=512,CanvasHeight=560,DisplayX=500,DisplayY=48,DisplayWidth=384,DisplayHeight=500};
        checks["invalid_saved_viewport_uses_legacy_geometry"]=Same(restored,PetWindow.RestoreWindowBounds(invalid,true,canvas,viewport));
        var small=PetWindow.RestoreWindowBounds(new PetWindow.Settings{Left=0,Top=0,Height=30},true,canvas,viewport);
        var large=PetWindow.RestoreWindowBounds(new PetWindow.Settings{Left=0,Top=0,Height=2000},true,canvas,viewport);
        checks["size_limits_apply_to_visible_body"]=Near(small.Height*500/560,180)&&Near(large.Height*500/560,720);
        foreach(var (name,petCanvas,petViewport) in new[]{
            ("ink",new Size(416,416),new Rect(139,89,137,254)),
            ("hudi",smokeCanvas,smokeViewport)})
        foreach(var screen in new[]{new Rect(0,0,1920,1080),new Rect(-1920,-240,1920,1080)})
        {
            Rect Visible(Rect window)
            {
                double scale=window.Height/petCanvas.Height;
                return new(window.Left+petViewport.X*scale,window.Top+petViewport.Y*scale,petViewport.Width*scale,petViewport.Height*scale);
            }
            double h=246,w=h*petViewport.Width/petViewport.Height;
            string id=$"{name}_{screen.Left}";
            foreach(string edge in new[]{"left","right","top","bottom"})
            {
                double x=edge=="left"?screen.Left-90:edge=="right"?screen.Right-w+90:screen.Left+(screen.Width-w)/2;
                double y=edge=="top"?screen.Top-90:edge=="bottom"?screen.Bottom-h+90:screen.Top+(screen.Height-h)/2;
                var input=PetWindow.WindowForDisplay(new(x,y,w,h),petCanvas,petViewport);
                var clamped=PetWindow.ClampWindowToScreen(input,petCanvas,petViewport,screen);var body=Visible(clamped);
                bool flush=edge switch{"left"=>Near(body.Left,screen.Left),"right"=>Near(body.Right,screen.Right),"top"=>Near(body.Top,screen.Top),_=>Near(body.Bottom,screen.Bottom)};
                bool canvasOutside=edge switch{"left"=>clamped.Left<screen.Left,"right"=>clamped.Right>screen.Right,"top"=>clamped.Top<screen.Top,_=>clamped.Bottom>screen.Bottom};
                checks[$"{id}_{edge}_body_reaches_edge_with_padding_outside"]=flush&&canvasOutside&&Near(clamped.Height,input.Height)&&Near(clamped.Width,input.Width);
                var edgeSettings=new PetWindow.Settings{Left=clamped.Left,Top=clamped.Top,Height=clamped.Height,
                    CanvasWidth=(int)petCanvas.Width,CanvasHeight=(int)petCanvas.Height,
                    DisplayX=petViewport.X,DisplayY=petViewport.Y,DisplayWidth=petViewport.Width,DisplayHeight=petViewport.Height};
                var reloaded=PetWindow.RestoreWindowBounds(edgeSettings,name=="hudi",petCanvas,petViewport);
                checks[$"{id}_{edge}_save_reload_keeps_position"]=Same(clamped,PetWindow.ClampWindowToScreen(reloaded,petCanvas,petViewport,screen));
            }
            var bottom=new Rect(screen.Left+(screen.Width-w)/2,screen.Bottom-h,w,h);
            var grown=PetWindow.WindowForDisplay(new(bottom.X,bottom.Bottom-420,bottom.Width,420),petCanvas,petViewport);
            var fitted=PetWindow.ClampWindowToScreen(grown,petCanvas,petViewport,screen);var fittedBody=Visible(fitted);
            checks[$"{id}_resize_preserves_feet_at_screen_bottom"]=Near(fittedBody.Bottom,screen.Bottom)&&
                Near(fittedBody.Left+fittedBody.Width/2,bottom.Left+bottom.Width/2)&&Near(fittedBody.Height,420)&&fitted.Bottom>screen.Bottom;
        }
        return checks;
    }
}
