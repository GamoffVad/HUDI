using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HudiPet;

internal static class SettingsAppearance
{
    internal static void Apply(GlassWindow window)
    {
        window.Resources.MergedDictionaries.Add(new ResourceDictionary { Source=new Uri("/HUDI;component/SettingsTheme.xaml",UriKind.Relative) });
        window.Surface.Background=(Brush)window.FindResource("SettingsGlass");
        window.Surface.BorderBrush=(Brush)window.FindResource("SettingsEdge");
        ((StackPanel)window.Surface.Child).Margin=new Thickness(22,18,22,22);
        window.Heading.FontSize=21;window.Header.Margin=new Thickness(0,0,0,14);
        var scrollbar=new Style(typeof(ScrollBar),(Style)Application.Current.FindResource(typeof(ScrollBar)));
        scrollbar.Setters.Add(new Setter(FrameworkElement.WidthProperty,4d));scrollbar.Setters.Add(new Setter(FrameworkElement.MinWidthProperty,4d));
        window.Resources[typeof(ScrollBar)]=scrollbar;
    }
}

internal sealed partial class SettingsWindow
{
    internal SettingsWindow(PetWindow owner,ModelSettings settings,string key,IModelClient client,Action<ModelSettings,string> save)
        : base(owner,"Модель и API",400,"\uE713")
    {
        this.client=client;this.save=save;SettingsAppearance.Apply(this);
        try{credentialOrigin=settings.Origin;}catch(ModelException){credentialOrigin="";}
        Style Style(string name)=>(Style)FindResource(name);
        TextBox Input(string text,string name){var value=Ui.Input(text,name);value.Style=Style("SettingsInput");return value;}
        ComboBox Select(string[] options,int index){var value=Ui.Select(options,index);value.Style=Style("SettingsSelect");return value;}
        void Row(Panel parent,string label,FrameworkElement control)
        {
            var row=new Grid { Margin=new Thickness(0,0,0,9) };row.ColumnDefinitions.Add(new(){Width=new GridLength(132)});row.ColumnDefinitions.Add(new());
            var caption=Ui.Text(label,14);caption.VerticalAlignment=VerticalAlignment.Center;row.Children.Add(caption);Grid.SetColumn(control,1);row.Children.Add(control);parent.Children.Add(row);
        }
        void Above(Panel parent,string label,FrameworkElement control,Thickness margin)
        {var caption=Ui.Text(label,14);caption.Margin=margin;parent.Children.Add(caption);parent.Children.Add(control);}
        var pets=Ui.Button("Питомцы ›",owner.OpenPetSettings,quiet:true);pets.FontSize=11;pets.Padding=new Thickness(3,4,3,4);pets.Margin=new Thickness(4,0,4,0);pets.ToolTip="Выбрать или добавить питомца";DockPanel.SetDock(pets,Dock.Right);Header.Children.Insert(1,pets);
        Body.Children.Add(new Border {Height=1,Background=new SolidColorBrush(Color.FromArgb(65,155,179,199)),Margin=new Thickness(0,0,0,12)});
        var fields=new StackPanel();
        var scroll=new ScrollViewer {Content=fields,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,MaxHeight=Math.Max(180,ScreenPlacement.WorkArea(owner).Height-228)};
        Body.Children.Add(scroll);
        provider=Select(ProviderPresets.All.Select(p=>p.Label).ToArray(),ProviderPresets.Index(settings.Provider));provider.MaxDropDownHeight=330;Row(fields,"Провайдер",provider);
        endpoint=Input(settings.BaseUrl,"Адрес сервера");endpoint.ToolTip="Базовый адрес API, обычно заканчивается на /v1";Row(fields,"Адрес сервера",endpoint);

        var keyRow=new Grid();hiddenKey=new PasswordBox {Password=key,Style=Style("SettingsPassword")};System.Windows.Automation.AutomationProperties.SetName(hiddenKey,"API-ключ");
        visibleKey=Input(key,"API-ключ, видимый текст");visibleKey.Padding=new Thickness(10,6,34,6);visibleKey.Visibility=Visibility.Collapsed;keyRow.Children.Add(hiddenKey);keyRow.Children.Add(visibleKey);
        var reveal=Ui.Button("\uE890",()=>{bool show=visibleKey.Visibility!=Visibility.Visible;visibleKey.Visibility=show?Visibility.Visible:Visibility.Collapsed;hiddenKey.Visibility=show?Visibility.Collapsed:Visibility.Visible;},quiet:true);
        var eye=new Grid {Width=18,Height=12};eye.Children.Add(new System.Windows.Shapes.Path {Data=Geometry.Parse("M 1,6 C 5,-1 13,-1 17,6 C 13,13 5,13 1,6 Z"),Stroke=new SolidColorBrush(Color.FromRgb(229,239,246)),StrokeThickness=1.4});eye.Children.Add(new System.Windows.Shapes.Ellipse {Width=5,Height=5,Stroke=new SolidColorBrush(Color.FromRgb(229,239,246)),StrokeThickness=1.4,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center});reveal.Content=eye;
        reveal.Width=32;reveal.Padding=new Thickness(0);reveal.HorizontalAlignment=HorizontalAlignment.Right;reveal.Margin=new Thickness(0,1,1,1);reveal.ToolTip="Показать или скрыть ключ";System.Windows.Automation.AutomationProperties.SetName(reveal,"Показать или скрыть API-ключ");keyRow.Children.Add(reveal);
        hiddenKey.PasswordChanged+=(_,_)=>{if(syncingKey)return;syncingKey=true;visibleKey.Text=hiddenKey.Password;syncingKey=false;BindEditedKey();};
        visibleKey.TextChanged+=(_,_)=>{if(syncingKey)return;syncingKey=true;hiddenKey.Password=visibleKey.Text;syncingKey=false;BindEditedKey();};Row(fields,"API-ключ",keyRow);

        var modelRow=new Grid();model=Input(settings.Model,"Модель");model.Padding=new Thickness(10,6,34,6);modelRow.Children.Add(model);
        var placeholder=Ui.Text("Выбрать модель",14);placeholder.Foreground=new SolidColorBrush(Color.FromRgb(220,229,237));placeholder.Margin=new Thickness(10,0,32,0);placeholder.VerticalAlignment=VerticalAlignment.Center;placeholder.IsHitTestVisible=false;
        void Placeholder()=>placeholder.Visibility=string.IsNullOrEmpty(model.Text)?Visibility.Visible:Visibility.Collapsed;
        model.TextChanged+=(_,_)=>Placeholder();Placeholder();modelRow.Children.Add(placeholder);
        modelsButton=Ui.Button("Выбрать модель",async()=>await TestConnection(true),quiet:true);modelsButton.Content=new System.Windows.Shapes.Path {Data=Geometry.Parse("M 0 0 L 5 5 L 10 0"),Stroke=Brushes.White,StrokeThickness=1.3,HorizontalAlignment=HorizontalAlignment.Center,VerticalAlignment=VerticalAlignment.Center};modelsButton.Width=32;modelsButton.HorizontalAlignment=HorizontalAlignment.Right;modelsButton.Padding=new Thickness(0);modelsButton.Margin=new Thickness(0,1,1,1);modelsButton.ToolTip="Загрузить список моделей с сервера";modelRow.Children.Add(modelsButton);Row(fields,"Модель",modelRow);

        var numbers=new Grid {Margin=new Thickness(0,3,0,0)};numbers.ColumnDefinitions.Add(new());numbers.ColumnDefinitions.Add(new());
        var left=new StackPanel {Margin=new Thickness(0,0,20,0)};var right=new StackPanel {Margin=new Thickness(20,0,0,0)};Grid.SetColumn(right,1);numbers.Children.Add(left);numbers.Children.Add(right);
        temperature=Input(settings.Temperature.ToString("0.##",CultureInfo.CurrentCulture),"Температура");temperature.IsEnabled=settings.SendTemperature;temperature.ToolTip="Передача температуры включается в дополнительных параметрах";
        limit=Input(settings.MaxOutputTokens.ToString(CultureInfo.InvariantCulture),"Лимит ответа");Above(left,"Температура",temperature,new Thickness(0,0,0,6));Above(right,"Лимит ответа",limit,new Thickness(0,0,0,6));fields.Children.Add(numbers);
        systemPrompt=Input(settings.SystemPrompt,"Системная инструкция");systemPrompt.AcceptsReturn=true;systemPrompt.TextWrapping=TextWrapping.Wrap;systemPrompt.Height=68;systemPrompt.VerticalContentAlignment=VerticalAlignment.Top;systemPrompt.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;Above(fields,"Системная инструкция",systemPrompt,new Thickness(0,12,0,6));

        timeout=Input(settings.TimeoutSeconds.ToString(CultureInfo.InvariantCulture),"Тайм-аут");
        var extra=new StackPanel {Visibility=Visibility.Collapsed,Margin=new Thickness(0,8,0,6)};
        var caption=Ui.Text("",14);void TimeoutCaption()=>caption.Text="Тайм-аут "+timeout.Text+" с";timeout.TextChanged+=(_,_)=>TimeoutCaption();TimeoutCaption();
        var chevron=Ui.Text("\uE70D",11);chevron.FontFamily=new FontFamily("Segoe Fluent Icons, Segoe MDL2 Assets");chevron.Margin=new Thickness(4,0,10,0);chevron.VerticalAlignment=VerticalAlignment.Center;
        var advanced=Ui.Button("Дополнительные параметры",()=>{bool opened=extra.Visibility!=Visibility.Visible;extra.Visibility=opened?Visibility.Visible:Visibility.Collapsed;chevron.Text=opened?"\uE70E":"\uE70D";},quiet:true);advanced.HorizontalContentAlignment=HorizontalAlignment.Stretch;advanced.Padding=new Thickness(0,8,0,8);
        var disclosure=new DockPanel();var hint=Ui.Text("Дополнительно",10,Ui.Muted);hint.VerticalAlignment=VerticalAlignment.Center;DockPanel.SetDock(hint,Dock.Right);disclosure.Children.Add(hint);disclosure.Children.Add(chevron);disclosure.Children.Add(caption);advanced.Content=disclosure;
        fields.Children.Add(new Border {Height=1,Background=new SolidColorBrush(Color.FromArgb(75,155,179,199)),Margin=new Thickness(0,12,0,0)});fields.Children.Add(advanced);fields.Children.Add(extra);
        protocol=Select(["Responses API","Chat Completions"],settings.Protocol=="chat"?1:0);Row(extra,"Формат API",protocol);Row(extra,"Тайм-аут, с",timeout);
        endpoint.TextChanged+=(_,_)=>connectionRevision++;protocol.SelectionChanged+=(_,_)=>connectionRevision++;
        useKey=Ui.Toggle("Использовать API-ключ",settings.UseApiKey);extra.Children.Add(useKey);useKey.Checked+=(_,_)=>keyRow.IsEnabled=true;useKey.Unchecked+=(_,_)=>keyRow.IsEnabled=false;keyRow.IsEnabled=settings.UseApiKey;
        history=Ui.Toggle("Помнить диалог до закрытия",settings.KeepHistory);extra.Children.Add(history);
        sendTemperature=Ui.Toggle("Передавать температуру",settings.SendTemperature);extra.Children.Add(sendTemperature);sendTemperature.Checked+=(_,_)=>temperature.IsEnabled=true;sendTemperature.Unchecked+=(_,_)=>temperature.IsEnabled=false;
        tokenParameter=Select(["max_tokens","max_completion_tokens"],settings.ChatTokenParameter=="max_completion_tokens"?1:0);tokenParameter.Margin=new Thickness(0,8,0,0);Row(extra,"Лимит в API",tokenParameter);
        fields.Children.Add(new Border {Height=1,Background=new SolidColorBrush(Color.FromArgb(45,155,179,199))});
        streaming=Ui.Toggle("Потоковый ответ",settings.Stream);streaming.Style=Style("SettingsSwitch");fields.Children.Add(streaming);
        provider.SelectionChanged+=(_,_)=>ApplyProvider();
        status=Ui.Text("",12,Ui.Muted);status.Margin=new Thickness(0,12,0,0);status.Visibility=Visibility.Collapsed;Body.Children.Add(status);
        var actions=new Grid {Margin=new Thickness(0,22,0,0)};actions.ColumnDefinitions.Add(new(){Width=new GridLength(1.15,GridUnitType.Star)});actions.ColumnDefinitions.Add(new());
        testButton=Ui.Button("Проверить связь",async()=>await TestConnection(false));testButton.Style=Style("SettingsAction");testButton.Margin=new Thickness(0,0,7,0);
        saveButton=Ui.Button("Сохранить",Save,true);saveButton.Style=Style("SettingsSave");saveButton.Margin=new Thickness(7,0,0,0);Grid.SetColumn(saveButton,1);actions.Children.Add(testButton);actions.Children.Add(saveButton);Body.Children.Add(actions);
        Closed+=(_,_)=>{lifetime.Cancel();lifetime.Dispose();hiddenKey.Password="";visibleKey.Text="";};
    }
    private void Status(string message,Brush color){status.Text=message;status.Foreground=color;status.Visibility=string.IsNullOrEmpty(message)?Visibility.Collapsed:Visibility.Visible;}
}
