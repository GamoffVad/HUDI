using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using Hudi.Distribution;

namespace HudiPet;
internal static class UiVerification
{
    internal static void Advance(int milliseconds)
    {
        var frame=new System.Windows.Threading.DispatcherFrame();var timer=new System.Windows.Threading.DispatcherTimer {Interval=TimeSpan.FromMilliseconds(milliseconds)};
        timer.Tick+=(_,_)=>{timer.Stop();frame.Continue=false;};timer.Start();System.Windows.Threading.Dispatcher.PushFrame(frame);
    }
    internal sealed class PreviewClient: IModelClient
    {
        internal string Reply="Привет! Я рядом. Спрашивай — помогу разобраться.";
        public Task<ModelReply> CompleteAsync(ModelSettings settings,string key,IReadOnlyList<ChatMessage> messages,IProgress<string>? progress,CancellationToken ct)=>Task.FromResult(new ModelReply(Reply));
        public Task<string[]> ListModelsAsync(ModelSettings settings,string key,CancellationToken ct)=>Task.FromResult(new[]{"preview"});
    }
    internal static int PreviewTransparency(bool matte=false,bool answer=false)
    {
        var app=new Application {ShutdownMode=ShutdownMode.OnMainWindowClose};Ui.Initialize();
        var pet=new PetWindow(false,true){Opacity=0,ShowInTaskbar=false};pet.Show();
        pet.SetMatteDialogs(matte);
        var client=new PreviewClient();using var session=new ChatSession(client);
        var chat=new ChatWindow(pet,session,_=>{},()=>{}){FollowAnchor=false,ShowActivated=false};chat.ShowInput("Чёткий текст поверх прозрачного фона",focus:false);
        if(answer){session.SendAsync("Пример",new(){Model="preview",UseApiKey=false},"").GetAwaiter().GetResult();chat.ShowAnswer();}
        var host=new Window {Title=answer?"HUDI — проверка формы ответа":"HUDI — проверка прозрачности",Width=760,Height=700,WindowStartupLocation=WindowStartupLocation.CenterScreen,Background=new SolidColorBrush(Color.FromRgb(35,43,52)),Topmost=true};
        var body=new StackPanel {Margin=new Thickness(24)};host.Content=body;
        body.Children.Add(Ui.Text("Проверка настоящей прозрачности",22));
        body.Children.Add(pet.CreateTransparencyControl(chat));
        if(answer)body.Children.Add(Ui.Button("Короткий / длинный ответ",()=>{client.Reply=client.Reply.Length<100?string.Join("\n",Enumerable.Repeat("Длинный ответ остаётся внутри стеклянного окна. Текст можно выделить и прокрутить.",12)):"Привет! Я рядом. Спрашивай — помогу разобраться.";session.SendAsync("Пример",new(){Model="preview",UseApiKey=false},"").GetAwaiter().GetResult();chat.ShowAnswer();}));
        body.Children.Add(Ui.Button("Повторить появление",()=>new PopupMotion(chat.Surface).Play()));
        body.Children.Add(Ui.Text("Под диалогом — цветные полосы и надписи. При 90 % они должны быть отчётливо видны.",13,Ui.Muted));
        var scene=new Grid {Height=380,Margin=new Thickness(0,18,0,0)};body.Children.Add(scene);
        Color[] colors=[Color.FromRgb(205,109,75),Color.FromRgb(224,187,71),Color.FromRgb(88,168,141),Color.FromRgb(72,132,181)];
        for(int i=0;i<colors.Length;i++)
        {
            scene.ColumnDefinitions.Add(new ColumnDefinition());
            var stripe=new Border {Background=new SolidColorBrush(colors[i])};Grid.SetColumn(stripe,i);scene.Children.Add(stripe);
            var label=new TextBlock {Text="ФОН "+(i+1),FontSize=28,Foreground=Brushes.Black,VerticalAlignment=VerticalAlignment.Center,HorizontalAlignment=HorizontalAlignment.Center};Grid.SetColumn(label,i);scene.Children.Add(label);
        }
        void PlaceChat(){if(!host.IsLoaded)return;var point=scene.PointToScreen(new Point());var dpi=VisualTreeHelper.GetDpi(host);chat.Left=point.X/dpi.DpiScaleX+(scene.ActualWidth-chat.Width)/2;chat.Top=point.Y/dpi.DpiScaleY+scene.ActualHeight/2-chat.ActualHeight/2;if(answer){pet.Left=chat.Left+chat.Width/2-pet.Width/2;pet.Top=chat.Top+chat.ActualHeight+38;}}
        chat.SizeChanged+=(_,_)=>PlaceChat();
        host.Loaded+=(_,_)=>{chat.Owner=host;chat.PrepareOpening();chat.Show();PlaceChat();chat.AnimateOpening();};host.LocationChanged+=(_,_)=>PlaceChat();host.SizeChanged+=(_,_)=>PlaceChat();
        host.Closed+=(_,_)=>pet.Close();app.MainWindow=host;return app.Run(host);
    }
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Initialize();
        var pet=new PetWindow(false,true){Opacity=0,ShowInTaskbar=false};pet.Show();
        VerifyWindowLifecycle(pet,directory);
        var client=new PreviewClient();var settings=new ModelSettings{Model="",UseApiKey=true};
        InteractionUiVerification.Run(pet,client,directory);
        NotesVerification.Run(pet,directory);
        pet.VerifyChatPresentation(directory);
        var settingsPanel=new SettingsWindow(pet,settings,"",client,(_,_)=>{});
        VisualPreview.Save(settingsPanel,Path.Combine(directory,"model-settings.png"));
        VisualPreview.Save(new PetSettingsWindow(pet),Path.Combine(directory,"pets.png"));
        File.WriteAllText(Path.Combine(directory,"layout.json"),System.Text.Json.JsonSerializer.Serialize(new{width=((FrameworkElement)settingsPanel.Content).ActualWidth,height=((FrameworkElement)settingsPanel.Content).ActualHeight,activationRequired=Hudi.Licensing.LicenseCodec.ActivationRequired}));
        SettingsWindow.VerifyControls(pet,client,directory);
        using var session=new ChatSession(client);var chat=new ChatWindow(pet,session,_=>{},()=>{});
        VisualPreview.Save(chat,Path.Combine(directory,"question.png"));
        session.SendAsync("Пример",new(){Model="preview",UseApiKey=false},"").GetAwaiter().GetResult();chat.ShowAnswer();VisualPreview.Save(chat,Path.Combine(directory,"answer-example.png"));
        chat.Close();pet.Close();return 0;
    }
    private static void VerifyWindowLifecycle(PetWindow pet,string directory)
    {
        var checks=new Dictionary<string,bool>();
        var window=new GlassWindow(pet,"Lifecycle check",280){Opacity=0};
        window.Closing+=(_,_)=>
        {
            checks["closing_state_set_before_native_deactivation"]=window.IsClosing;
            window.DismissWhenInactive();
        };
        window.Show();window.Close();
        checks["closed_state_kept"]=window.IsClosing;
        for(int i=0;i<4;i++)
        {
            pet.OpenMenu();pet.OpenPetSettings();
            var panel=pet.OwnedWindows.OfType<PetSettingsWindow>().Single();
            checks["menu_to_pet_settings_"+i]=panel.IsVisible&&pet.IsVisible;
            panel.Close();
            pet.OpenMenu();pet.OpenChat(true);
            checks["menu_to_chat_"+i]=pet.OwnedWindows.OfType<ChatWindow>().Any(w=>w.IsVisible)&&pet.IsVisible;
            foreach(var chat in pet.OwnedWindows.OfType<ChatWindow>().ToArray())chat.Close();
        }
        var cancel=new GlassWindow(pet,"Cancel close",280){Opacity=0};
        bool cancelOnce=true;cancel.Closing+=(_,e)=>{if(cancelOnce){e.Cancel=true;cancelOnce=false;}};
        cancel.Show();cancel.Close();checks["cancelled_close_restores_state"]=!cancel.IsClosing&&cancel.IsVisible;cancel.Close();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        checks["late_dismiss_callbacks_are_safe"]=pet.IsVisible;
        File.WriteAllText(Path.Combine(directory,"window-lifecycle.json"),System.Text.Json.JsonSerializer.Serialize(new{success=checks.Values.All(x=>x),checks},new System.Text.Json.JsonSerializerOptions{WriteIndented=true}));
        if(checks.Values.Any(x=>!x))throw new InvalidOperationException("Window lifecycle verification failed");
    }
}
