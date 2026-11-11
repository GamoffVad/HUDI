using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Media;

namespace HudiPet;

internal sealed partial class PetWindow
{
    private HttpModelClient modelClient = null!;
    private ModelSettingsStore modelStore = null!;
    private ModelSettings modelSettings = null!;
    private string apiKey = "";
    private ChatSession chat = null!;
    private ChatWindow? chatWindow;
    private string chatDraft="";
    private SettingsWindow? settingsWindow;
    private GlassWindow? menuWindow;
    private NotesWindow? notesWindow;
    private ThinkingWindow? thinkingWindow;
    private bool shuttingDown;
    private ChatState displayedState;
    private void InitializeChat()
    {
        modelClient = new(); modelStore = new(uiTest?System.IO.Path.Combine(SettingsDirectory,"ui-test"):null);
        (modelSettings,apiKey)=modelStore.Load();
        chat = new(modelClient);
        BindChatEvents();
        Closing+=(_,e)=>{if(notesWindow is {IsClosing:false}&&!notesWindow.Flush())e.Cancel=true;};
    }
    private void BindChatEvents()
    {
        chat.Started += () => { paused=false; behavior.BeginChatThinking(); };
        chat.Finished += success => { if(!shuttingDown)behavior.EndChatThinking(success); };
        chat.Changed += () =>
        {
            if(shuttingDown || chat.State==displayedState)return;
            displayedState=chat.State;
            if(chat.State==ChatState.Thinking){ShowThinking();return;}
            thinkingWindow?.Close();thinkingWindow=null;
            if(chat.State==ChatState.Error)EnsureChat(false,window=>window.ShowError());
            else if(chat.State==ChatState.Canceled)EnsureChat(false,window=>{if(window.View==ChatView.Answer)window.ShowAnswer();else window.ShowInput(chat.LastQuestion);});
            else if(chat.State==ChatState.Answered)EnsureChat(false,window=>window.ShowAnswer());
        };
    }
    private void DisposeChat() { shuttingDown=true; chat.Dispose(); modelClient.Dispose(); apiKey=""; }
    private ModelSettings ChatSettings(){var value=modelSettings.Copy();if(value.SystemPrompt==ModelSettings.DefaultSystemPrompt)value.SystemPrompt="Твоё имя — "+PetName+". "+value.SystemPrompt;return value;}
    private ChatWindow EnsureChat(bool activate,Action<ChatWindow> configure)
    {
        if(chatWindow is null || chatWindow.IsClosing)
        {
            chatWindow=new ChatWindow(this,chat,question=>_ = chat.SendAsync(question,ChatSettings(),apiKey),OpenSettings);
            chatWindow.ShowActivated=activate;
            var created=chatWindow;created.ShowInput(chatDraft,focus:activate);created.Closed+=(_,_)=>chatDraft=created.Draft;
        }
        // Configure the final view and its initial circle before showing the
        // native window, including when an answer arrives without a click.
        configure(chatWindow);chatWindow.PrepareOpening();
        if(!chatWindow.IsVisible)chatWindow.Show();
        if(activate)chatWindow.Activate();
        chatWindow.AnimateOpening();return chatWindow;
    }
    private void ShowThinking()
    {
        if(chatWindow is {IsClosing:false,IsVisible:true}){chatWindow.ShowAnswer();return;}
        chatWindow?.Close();chatWindow=null;
        if(thinkingWindow is not null)return;
        thinkingWindow=new ThinkingWindow(this);thinkingWindow.Show();
    }
    internal void OpenChat(bool question)
    {
        CloseFavoriteSites();
        menuWindow?.Close();menuWindow=null;
        if(chat.Busy){ShowThinking();return;}
        EnsureChat(true,window=>
        {
            if(string.IsNullOrEmpty(chat.Answer))window.ShowInput();
            else window.ShowAnswer();
        });
    }
    internal void OpenSettings()
    {
        CloseFavoriteSites();
        menuWindow?.Close();menuWindow=null;
        if(settingsWindow is { IsClosing:false }) { settingsWindow.Show();settingsWindow.Activate();return; }
        settingsWindow = new SettingsWindow(this,modelSettings,apiKey,modelClient,(value,key)=>
        {
            modelStore.Save(value,key); modelSettings=value; apiKey=key;
            chat.ClearHistory();
        });
        settingsWindow.Show();
    }
    internal void OpenMenu()
    {
        CloseFavoriteSites();
        if(menuWindow is { IsClosing:false })menuWindow.Close();
        var menu=new GlassWindow(this,PetName,270);menuWindow=menu;
        SettingsAppearance.Apply(menu);
        ((StackPanel)menu.Surface.Child).Margin=new Thickness(10,9,10,9);menu.Surface.Background=(Brush)menu.FindResource("MenuGlass");
        menu.Heading.FontSize=16;menu.Header.Margin=new Thickness(4,0,0,5);
        foreach(var mark in menu.Header.Children.OfType<Border>()){mark.Width=26;mark.Height=22;mark.Margin=new Thickness(0,0,8,0);}
        Button Row(string glyph,string label,Action action,string? hint=null){var row=Ui.MenuRow(glyph,label,action,hint,compact:true);row.Style=(Style)menu.FindResource("GlassMenuRow");return row;}
        Border Divider(){var line=Ui.Separator();line.Margin=new Thickness(6,5,6,5);return line;}
        void Page(string title, Action<StackPanel> fill) { menu.Heading.Text=title;menu.Body.Children.Clear(); menu.Body.Children.Add(Row("\uE72B","Назад",Main));fill(menu.Body); }
        void Main()
        {
            menu.Heading.Text=PetName;menu.Body.Children.Clear();
            menu.Body.Children.Add(Row("\uE8BD","Открыть чат",()=>OpenChat(true),ClickBindings.HintFor(PetClickAction.Chat)));
            if(!string.IsNullOrEmpty(chat.Answer))menu.Body.Children.Add(Row("\uE8A5","Последний ответ",()=>OpenChat(false)));
            menu.Body.Children.Add(Row("\uE70B","Заметки",OpenNotes,ClickBindings.HintFor(PetClickAction.Notes)));
            menu.Body.Children.Add(Row("\uE71B","Сайты и программы…",OpenFavoriteSitesSettings));
            if(chat.Busy)menu.Body.Children.Add(Row("\uE711","Отменить запрос",()=>{chat.Cancel();menu.Close();}));
            menu.Body.Children.Add(Row("\uE768","Движения",()=>Page("Движения",body=>
            {
                // Keep Back and the header visible when the growing action list scrolls.
                var movements=new StackPanel();
                var scroll=new ScrollViewer
                {
                    Content=movements,VerticalScrollBarVisibility=ScrollBarVisibility.Auto,
                    HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled,
                    MaxHeight=Math.Max(80,Math.Min(440,ScreenPlacement.WorkArea(this).Height-100)),
                    Padding=new Thickness(0),CanContentScroll=false
                };
                System.Windows.Automation.AutomationProperties.SetName(scroll,"Движения питомца");
                foreach(var(action,label) in PetVerification.ActionLabels)
                {
                    if(!behavior.Supports(action))continue;
                    var row=Row("\uE734",label,()=> { paused=false;behavior.Trigger(action);menu.Close(); });
                    row.IsEnabled=!chat.Busy;movements.Children.Add(row);
                }
                body.Children.Add(scroll);
            }),"›"));
            menu.Body.Children.Add(Row("\uE713","Модель и API",OpenSettings));
            menu.Body.Children.Add(Row("\uE713","Управление",OpenClickSettings));
            menu.Body.Children.Add(Row("\uE77B","Питомцы",OpenPetSettings));
            menu.Body.Children.Add(Row("\uE740","Внешний вид",()=>Page("Внешний вид",body=>
            {
                body.Children.Add(CreateSizeControl(menu));
                body.Children.Add(CreateTransparencyControl(menu));
                body.Children.Add(Divider());body.Children.Add(Ui.Text("Темп движения",12,Ui.Muted));
                foreach(var(label,value) in new[] { ("Спокойный",.75),("Обычный",1d),("Бодрый",1.3) })
                    body.Children.Add(Row("\uE768",label,()=> { speed=value;SaveSettings();menu.Close(); },Math.Abs(speed-value)<.01?"✓":null));
                var top=Ui.Toggle("Поверх окон",Topmost);top.Click+=(_,_)=> { Topmost=top.IsChecked==true;SaveSettings(); };body.Children.Add(top);
            }),"›"));
            menu.Body.Children.Add(Divider());
            menu.Body.Children.Add(Row("\uE8BB","Закрыть питомца",Close));
        }
        Main();menu.Deactivated+=(_,_)=>menu.DismissWhenInactive();menu.Show();menu.Activate();
    }
    internal void OpenNotes()
    {
        CloseFavoriteSites();
        menuWindow?.Close();menuWindow=null;
        if(notesWindow is {IsClosing:false}){notesWindow.Activate();return;}
        notesWindow=new NotesWindow(this,new NotesStore(uiTest?System.IO.Path.Combine(SettingsDirectory,"ui-test"):null));notesWindow.Show();
    }
    internal FrameworkElement CreateSizeControl(GlassWindow menu)
    {
        var panel=new StackPanel {Margin=new Thickness(2,12,2,4)};
        var caption=new DockPanel();var value=Ui.Text("",12,Ui.Accent);DockPanel.SetDock(value,Dock.Right);caption.Children.Add(value);caption.Children.Add(Ui.Text("Размер питомца",13));panel.Children.Add(caption);
        var slider=new Slider {Minimum=180,Maximum=Math.Max(180,Math.Min(720,ScreenPlacement.WorkArea(this).Height*library.DisplayBounds.Height/library.Height)),Value=DisplayHeight,SmallChange=5,LargeChange=30,IsMoveToPointEnabled=true,Margin=new Thickness(0,10,0,2),Style=(Style)menu.FindResource("PetSizeSlider")};
        System.Windows.Automation.AutomationProperties.SetName(slider,"Размер питомца");
        void Label()=>value.Text=Math.Round(DisplayHeight/360*100)+" %";
        slider.ValueChanged+=(_,_)=>{ResizePet(slider.Value*library.Height/library.DisplayBounds.Height);Label();};
        // Keep the track under the pointer while the pet changes height below it.
        slider.AddHandler(Thumb.DragStartedEvent,new DragStartedEventHandler((_,_)=>menu.FollowAnchor=false));
        slider.AddHandler(Thumb.DragCompletedEvent,new DragCompletedEventHandler((_,_)=>{menu.FollowAnchor=true;menu.Place();}));
        slider.PreviewMouseLeftButtonDown+=(_,_)=>menu.FollowAnchor=false;
        slider.PreviewMouseLeftButtonUp+=(_,_)=>{menu.FollowAnchor=true;menu.Place();};
        slider.LostMouseCapture+=(_,_)=>{menu.FollowAnchor=true;menu.Place();};
        Label();panel.Children.Add(slider);return panel;
    }
    internal FrameworkElement CreateTransparencyControl(GlassWindow menu)
    {
        var panel=new StackPanel {Margin=new Thickness(2,12,2,4)};
        var caption=new DockPanel();var value=Ui.Text("",12,Ui.Accent);DockPanel.SetDock(value,Dock.Right);caption.Children.Add(value);caption.Children.Add(Ui.Text("Прозрачность окон",13));panel.Children.Add(caption);
        var slider=new Slider {Minimum=0,Maximum=90,Value=DialogTransparency,SmallChange=5,LargeChange=10,IsMoveToPointEnabled=true,Margin=new Thickness(0,10,0,2),Style=(Style)menu.FindResource("PetSizeSlider")};
        System.Windows.Automation.AutomationProperties.SetName(slider,"Прозрачность окон");slider.ToolTip="0 % — непрозрачный фон · 90 % — виден рабочий стол. Текст и кнопки остаются чёткими";
        void Label()=>value.Text=Math.Round(DialogTransparency)+" %";
        slider.ValueChanged+=(_,_)=>{SetDialogTransparency(slider.Value);Label();};
        Label();panel.Children.Add(slider);
        var matte=Ui.Toggle("Матовый фон",MatteDialogs);matte.Margin=new Thickness(0,7,0,0);matte.ToolTip="Мягкое размытие фона под окном";
        matte.Checked+=(_,_)=>SetMatteDialogs(true);matte.Unchecked+=(_,_)=>SetMatteDialogs(false);panel.Children.Add(matte);
        return panel;
    }
}
