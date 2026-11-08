using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;

namespace Hudi.Distribution;

internal class BrandWindow : Window
{
    internal readonly StackPanel Body=new();
    internal readonly TextBlock TitleText,Subtitle,BrandName,BrandCaption,ContextLabel;
    internal readonly Button CloseButton;
    internal Image? Hero;
    internal static Brush Accent=new SolidColorBrush(Color.FromRgb(157,232,255)),Muted=new SolidColorBrush(Color.FromRgb(157,178,198));
    internal static void Initialize()
    {
        string assembly=Assembly.GetExecutingAssembly().GetName().Name!;
        Application.Current.Resources.MergedDictionaries.Add(new ResourceDictionary{Source=new Uri("/"+assembly+";component/Theme.xaml",UriKind.Relative)});
    }
    internal static TextBlock Text(string text,double size=14,Brush? color=null)=>new(){Text=text,FontSize=size,Foreground=color??Brushes.White,TextWrapping=TextWrapping.Wrap};
    internal static Button Button(string text,Action action,bool primary=false)
    {
        var button=new Button{Content=text,Style=(Style)Application.Current.FindResource(primary?"PrimaryButton":typeof(Button))};button.Click+=(_,_)=>action();return button;
    }
    internal static TextBox Input(string value="")=>new(){Text=value};
    internal static void Field(Panel body,string label,FrameworkElement control)
    {var name=Text(label,12,Muted);name.Margin=new Thickness(0,16,0,7);body.Children.Add(name);body.Children.Add(control);}
    internal static StackPanel Row(params FrameworkElement[] controls)
    {var row=new StackPanel{Orientation=Orientation.Horizontal};foreach(var c in controls){c.Margin=new Thickness(0,0,8,0);row.Children.Add(c);}return row;}
    internal BrandWindow(string title,string subtitle)
    {
        Title=title;Width=790;SizeToContent=SizeToContent.Height;WindowStartupLocation=WindowStartupLocation.CenterScreen;
        WindowStyle=WindowStyle.None;ResizeMode=ResizeMode.NoResize;FrostedBackdrop.Attach(this);
        FontFamily=new FontFamily("Segoe UI");Foreground=Brushes.White;
        var root=new Grid();
        var card=new Border{CornerRadius=new CornerRadius(FrostedBackdrop.CornerRadius),Background=FrostedBackdrop.Tint,BorderBrush=FrostedBackdrop.Edge,BorderThickness=new Thickness(1)};
        root.Children.Add(card);var grid=new Grid();grid.ColumnDefinitions.Add(new(){Width=new GridLength(270)});grid.ColumnDefinitions.Add(new());card.Child=grid;
        var left=new Grid{MinHeight=500,ClipToBounds=true};
        var brand=new StackPanel{Margin=new Thickness(28,30,20,0),VerticalAlignment=VerticalAlignment.Top};BrandName=Text("HUDI",32,Accent);BrandCaption=Text("Маленький характер.\nБольшой мир рядом.",13,Muted);brand.Children.Add(BrandName);brand.Children.Add(BrandCaption);left.Children.Add(brand);
        using var source=Assembly.GetExecutingAssembly().GetManifestResourceStream("HUDI.hero.png");
        if(source is not null){var bmp=new BitmapImage();bmp.BeginInit();bmp.CacheOption=BitmapCacheOption.OnLoad;bmp.StreamSource=source;bmp.EndInit();bmp.Freeze();Hero=new Image{Source=bmp,Width=245,Height=320,VerticalAlignment=VerticalAlignment.Bottom,Margin=new Thickness(0,0,0,45)};left.Children.Add(Hero);}
        var footer=Text("WINDOWS 10 / 11  ·  x64",10,Muted);footer.VerticalAlignment=VerticalAlignment.Bottom;footer.Margin=new Thickness(28,0,0,22);left.Children.Add(footer);
        grid.Children.Add(new Border{CornerRadius=new CornerRadius(0),Background=new LinearGradientBrush(Color.FromArgb(100,82,131,156),Color.FromArgb(70,30,46,61),60),Child=left});
        var right=new StackPanel{Margin=new Thickness(30,28,30,28)};
        var scroll=new ScrollViewer{Content=right,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(500,SystemParameters.WorkArea.Height-48)};
        Grid.SetColumn(scroll,1);grid.Children.Add(scroll);
        var head=new DockPanel();CloseButton=Button("×",Close);CloseButton.Style=(Style)Application.Current.FindResource("QuietButton");CloseButton.Padding=new Thickness(6,0,6,0);CloseButton.FontSize=22;DockPanel.SetDock(CloseButton,Dock.Right);head.Children.Add(CloseButton);ContextLabel=Text("HUDI  /  DESKTOP COMPANION",10,Muted);head.Children.Add(ContextLabel);right.Children.Add(head);
        TitleText=Text(title,26);TitleText.FontWeight=FontWeights.SemiBold;TitleText.Margin=new Thickness(0,28,0,9);right.Children.Add(TitleText);
        Subtitle=Text(subtitle,13,Muted);right.Children.Add(Subtitle);Body.Margin=new Thickness(0,10,0,0);right.Children.Add(Body);Content=root;
        head.MouseLeftButtonDown+=(_,e)=>{if(e.OriginalSource is TextBlock)DragMove();};
        PreviewKeyDown+=(_,e)=>{if(e.Key==Key.Escape&&CloseButton.IsEnabled)Close();};
    }
}
