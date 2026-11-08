using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HudiPet;

internal enum ChatView { Input, Answer, Error }
internal sealed class ChatWindow : GlassWindow
{
    private readonly ChatSession session;
    private readonly Action<string> send;
    private readonly Action settings;
    private readonly string petName;
    private readonly bool hudiStyle;
    private static readonly Brush HudiText=new SolidColorBrush(Color.FromRgb(184,216,239));
    private readonly DispatcherTimer refresh;
    internal readonly Button DismissButton;
    private TextBox? answer, input;
    private string draft = "";
    private bool focusInputWhenReady;
    private DispatcherOperation? pendingFocus;
    private readonly PopupMotion opening;
    private bool entrancePlayed;
    internal bool OpeningAnimationRunning=>opening.IsRunning;
    internal string Draft => draft;
    internal ChatView View { get; private set; }
    internal ChatWindow(PetWindow owner,ChatSession session,Action<string> send,Action settings) : base(owner,"Спросить "+owner.PetName,430)
    {
        this.session=session;this.send=send;this.settings=settings;petName=owner.PetName;SettingsAppearance.Apply(this);opening=new PopupMotion(Surface);
        hudiStyle=owner.SelectedPetId==PetCatalog.Hudi.Id;
        if(hudiStyle)
        {
            Resources.MergedDictionaries.Add(new ResourceDictionary {Source=new Uri("/HUDI;component/HudiDialogTheme.xaml",UriKind.Relative)});
            Surface.ClearValue(Border.BackgroundProperty);Surface.ClearValue(Border.BorderBrushProperty);
            Surface.Style=(Style)FindResource("HudiDialogSurface");
        }
        UseSquareFrame();
        DismissButton=Ui.Button("Закрыть диалог",Close,quiet:true);
        DismissButton.Style=(Style)FindResource("DialogDismiss");
        DismissButton.Content=new System.Windows.Shapes.Path {Data=Geometry.Parse("M 0,0 L 6,6 M 6,0 L 0,6"),Width=6,Height=6,Stroke=Ui.Muted,StrokeThickness=1.2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
        DismissButton.HorizontalAlignment=HorizontalAlignment.Right;DismissButton.VerticalAlignment=VerticalAlignment.Top;
        DismissButton.Margin=new Thickness(0,10,12,0);DismissButton.Visibility=Visibility.Collapsed;
        ((Grid)Content).Children.Add(DismissButton);
        MouseEnter+=(_,_)=>DismissButton.Visibility=View==ChatView.Error?Visibility.Collapsed:Visibility.Visible;
        MouseLeave+=(_,_)=>DismissButton.Visibility=Visibility.Collapsed;
        refresh=new DispatcherTimer { Interval=TimeSpan.FromMilliseconds(80) };
        refresh.Tick+=(_,_)=>RefreshText(); refresh.Start();
        Loaded+=(_,_)=>QueueInputFocus();
        ContentRendered+=(_,_)=>QueueInputFocus();
        Closed+=(_,_)=>{refresh.Stop();pendingFocus?.Abort();focusInputWhenReady=false;opening.Stop();};
        ShowInput(focus:false);
    }
    internal void AnimateOpening()
    {
        if(entrancePlayed||IsClosing||View==ChatView.Error)return;
        entrancePlayed=true;UpdateLayout();opening.Play();
    }
    internal void PrepareOpening()
    {
        if(!entrancePlayed&&View!=ChatView.Error&&SystemParameters.ClientAreaAnimation)Surface.RevealProgress=0;
    }
    private void Reset(ChatView view,string title)
    {
        if(View!=view){opening.Stop();entrancePlayed=false;}
        View=view; Title=title; Heading.Text=title;Body.Children.Clear();answer=null;input=null;
        pendingFocus?.Abort();pendingFocus=null;focusInputWhenReady=false;
        Header.Visibility=Visibility.Visible;
        DismissButton.Visibility=IsMouseOver&&view!=ChatView.Error?Visibility.Visible:Visibility.Collapsed;
    }
    internal void ShowInput(string? text=null,bool focus=true)
    {
        draft=text??draft;
        Reset(ChatView.Input,"Спросить "+petName);
        SizeToContent=SizeToContent.Height;Width=400;Height=double.NaN;
        ((StackPanel)Surface.Child).Margin=new Thickness(8);
        DismissButton.Margin=new Thickness(0,4,4,0);
        Header.Visibility=Visibility.Collapsed;
        var line=new Grid();line.ColumnDefinitions.Add(new());
        var sendColumn=new ColumnDefinition {Width=new GridLength(0)};line.ColumnDefinitions.Add(sendColumn);
        line.ColumnDefinitions.Add(new(){Width=new GridLength(16)});
        input=Ui.Input(draft,"Вопрос модели");input.AcceptsReturn=true;input.TextWrapping=TextWrapping.Wrap;
        input.MinHeight=36;input.MaxHeight=144;input.Padding=new Thickness(8,8,6,8);input.FontSize=14;input.VerticalContentAlignment=VerticalAlignment.Top;
        input.Background=Brushes.Transparent;input.BorderThickness=new Thickness(0);
        input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;input.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        input.ToolTip="Введите вопрос · Enter — отправить · Shift+Enter — новая строка";
        input.PreviewKeyDown+=(_,e)=> { if(HandleComposerKey(e.Key,Keyboard.Modifiers))e.Handled=true; };
        if(hudiStyle){input.Foreground=HudiText;input.CaretBrush=HudiText;}
        line.Children.Add(input);
        var placeholder=Ui.Text("Спросить "+petName+"…",14,hudiStyle?HudiText:Ui.Muted);placeholder.Opacity=.72;placeholder.Margin=new Thickness(8,8,6,8);placeholder.IsHitTestVisible=false;placeholder.VerticalAlignment=VerticalAlignment.Top;line.Children.Add(placeholder);
        var submit=Ui.Button("Отправить",Submit,true);submit.Style=(Style)FindResource(hudiStyle?"HudiComposerSend":"ComposerSend");
        var arrow=new System.Windows.Shapes.Path {Data=Geometry.Parse("M 2,8 L 8,2 L 14,8 M 8,2 L 8,15"),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=16,Height=17};
        arrow.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new Binding(nameof(Control.Foreground)){Source=submit});submit.Content=arrow;
        submit.ToolTip="Отправить · Enter";submit.VerticalAlignment=VerticalAlignment.Bottom;submit.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(submit,1);line.Children.Add(submit);
        void Changed()
        {
            draft=input.Text;bool hasText=!string.IsNullOrWhiteSpace(draft);
            placeholder.Visibility=draft.Length==0?Visibility.Visible:Visibility.Collapsed;
            submit.IsEnabled=hasText&&!session.Busy;submit.Visibility=hasText?Visibility.Visible:Visibility.Collapsed;
            sendColumn.Width=new GridLength(hasText?40:0);
        }
        input.TextChanged+=(_,_)=>Changed();Changed();
        Body.Children.Add(line);
        input.CaretIndex=input.Text.Length;
        focusInputWhenReady=focus;QueueInputFocus();
    }
    private void QueueInputFocus()
    {
        if(!focusInputWhenReady||!IsLoaded||!IsVisible||IsClosing||View!=ChatView.Input||input is null)return;
        var target=input;pendingFocus?.Abort();
        pendingFocus=Dispatcher.BeginInvoke(()=>
        {
            pendingFocus=null;
            if(!focusInputWhenReady||IsClosing||!IsVisible||View!=ChatView.Input||!ReferenceEquals(input,target))return;
            Activate();
            FocusManager.SetFocusedElement(this,target);
            target.Focus();Keyboard.Focus(target);
            target.Select(target.Text.Length,0);
            focusInputWhenReady=!target.IsKeyboardFocused;
        },DispatcherPriority.ContextIdle);
    }
    private void Submit()
    {
        if(input is null||session.Busy||string.IsNullOrWhiteSpace(input.Text))return;
        string question=input.Text;draft="";send(question);
    }
    internal bool HandleComposerKey(Key key,ModifierKeys modifiers)
    {
        if(key!=Key.Enter||modifiers.HasFlag(ModifierKeys.Shift))return false;
        Submit();return true;
    }
    internal void ShowAnswer()
    {
        Reset(ChatView.Answer,"Ответ · "+petName);
        UseSquareFrame();DismissButton.Margin=new Thickness(0,10,12,0);
        Header.Visibility=Visibility.Collapsed;
        answer=Ui.Input("","Ответ модели");answer.IsReadOnly=true; answer.AcceptsReturn=true; answer.TextWrapping=TextWrapping.Wrap;answer.BorderThickness=new Thickness(0);answer.Background=System.Windows.Media.Brushes.Transparent;
        answer.Padding=new Thickness(0,4,0,4);answer.FontSize=13;answer.MinHeight=18;
        if(hudiStyle){answer.Foreground=HudiText;answer.CaretBrush=HudiText;}
        answer.Height=SquareBodyHeight;answer.VerticalContentAlignment=VerticalAlignment.Top;
        answer.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;answer.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        Body.Children.Add(answer);RefreshText();
    }
    internal void ShowError()
    {
        Reset(ChatView.Error,"Ошибка ответа");UseSquareFrame();var content=SquareBody();Body.Children.Add(content);
        content.RowDefinitions.Add(new());content.RowDefinitions.Add(new(){Height=GridLength.Auto});
        content.Children.Add(new ScrollViewer {Content=Ui.Text(session.Error,13,Ui.Error),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var row=Ui.Row(Ui.Button("Повторить",()=>send(session.LastQuestion),true),Ui.Button("Настройки",settings));row.Margin=new Thickness(0,14,0,0);Grid.SetRow(row,1);content.Children.Add(row);
    }
    private void RefreshText()
    {
        if(View!=ChatView.Answer || answer is null)return;
        string text=session.Busy?session.PartialAnswer:session.Answer;
        if(answer.Text!=text)answer.Text=text;
        answer.ToolTip=session.Truncated?"Ответ достиг лимита токенов. Лимит можно увеличить в настройках.":null;
    }
}
