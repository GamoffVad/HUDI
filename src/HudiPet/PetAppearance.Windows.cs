using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace HudiPet;

internal sealed partial record PetAppearance
{
    internal static PetAppearance Current {get;private set;}=Preset("HUDI",false);
    internal Color AccentColor=>ColorOf(Accent);
    internal Color SurfaceColor=>ColorOf(Surface);
    internal Color TextColor=>ColorOf(Text);
    internal static Color ColorOf(string value)=>(Color)ColorConverter.ConvertFromString(value);
    internal static Color Mix(Color a,Color b,double amount)=>Color.FromRgb((byte)(a.R+(b.R-a.R)*amount),(byte)(a.G+(b.G-a.G)*amount),(byte)(a.B+(b.B-a.B)*amount));
    internal Brush Brush(string color)=>new SolidColorBrush(ColorOf(color));
    internal Brush Ink=>Brush(Text);
    internal string IconFor(FavoriteSite site)=>Icons.FirstOrDefault(i=>i.Id==site.LibraryIcon)?.Image??site.Icon;
    internal Brush Muted=>new SolidColorBrush(Mix(TextColor,SurfaceColor,.3));
    internal Brush Edge=>new SolidColorBrush(Mix(AccentColor,SurfaceColor,.35));
    internal Brush Panel=>new LinearGradientBrush(Mix(SurfaceColor,Colors.White,.05),SurfaceColor,90);
    internal Brush Disc=>new RadialGradientBrush {
        Center=new(.45,.38),GradientOrigin=new(.34,.22),RadiusX=.67,RadiusY=.67,
        GradientStops=new GradientStopCollection {
            new(Mix(SurfaceColor,Colors.White,Finish=="ink"?.06:.35),0),
            new(Mix(SurfaceColor,AccentColor,.12),.72),new(Mix(SurfaceColor,AccentColor,.45),.9),
            new(Mix(AccentColor,Colors.Black,Finish=="ink"?.45:.16),1)} };
    internal void Apply()
    {
        Current=this;
        ApplyResources(Application.Current.Resources);
    }
    internal void ApplyResources(ResourceDictionary resources)
    {
        resources["TextBrush"]=Ink;resources["MutedBrush"]=Muted;resources["AccentBrush"]=Brush(Accent);
        resources["ThemePanel"]=Panel;resources["ThemeEdge"]=Edge;
        resources["ThemeField"]=new SolidColorBrush(Mix(SurfaceColor,TextColor,.06));
        resources["ThemeHover"]=new SolidColorBrush(Mix(SurfaceColor,AccentColor,.22));
        resources["ThemePrimaryText"]=new SolidColorBrush(Color.FromRgb(31,27,33));
        resources["SettingsGlass"]=resources["MenuGlass"]=Panel;
        resources["SettingsEdge"]=Edge;
        resources["SettingsField"]=resources["ThemeField"];
        resources["SettingsButton"]=new LinearGradientBrush(Mix(SurfaceColor,AccentColor,.17),Mix(SurfaceColor,AccentColor,.08),90);
        resources["SettingsPrimary"]=resources["PrimaryGlass"]=new LinearGradientBrush(Mix(AccentColor,Colors.White,.3),AccentColor,90);
    }
}

internal static partial class PetIconLibrary
{
    internal static PetIcon[] Create(PetAppearance theme)=>symbols.Select(s=>new PetIcon(s.Id,s.Name,Render(s.Path,theme))).ToArray();
    private static string Render(string path,PetAppearance theme)
    {
        var visual=new DrawingVisual();var geometry=Geometry.Parse(path);
        using(var dc=visual.RenderOpen())
        {
            Pen Stroke(Brush brush,double width)=>new(brush,width){StartLineCap=PenLineCap.Round,EndLineCap=PenLineCap.Round,LineJoin=PenLineJoin.Round};
            dc.PushTransform(new TranslateTransform(0,2.5));
            dc.DrawGeometry(null,Stroke(new SolidColorBrush(PetAppearance.Mix(theme.AccentColor,Colors.Black,.35)),9),geometry);dc.Pop();
            var face=new LinearGradientBrush(PetAppearance.Mix(theme.AccentColor,Colors.White,.65),theme.AccentColor,90);
            dc.DrawGeometry(null,Stroke(face,theme.Finish=="ink"?7:9),geometry);
            dc.PushTransform(new TranslateTransform(0,-1));
            dc.DrawGeometry(null,Stroke(new SolidColorBrush(Color.FromArgb(90,255,255,255)),2),geometry);dc.Pop();
        }
        var bitmap=new RenderTargetBitmap(128,128,96,96,PixelFormats.Pbgra32);bitmap.Render(visual);
        var png=new PngBitmapEncoder();png.Frames.Add(BitmapFrame.Create(bitmap));using var output=new MemoryStream();png.Save(output);return Convert.ToBase64String(output.ToArray());
    }
}
