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
    private static Brush HudiText=>PetAppearance.Current.Ink;
    private readonly DispatcherTimer refresh;
    internal readonly Button DismissButton;
    internal readonly Button ClearButton;
    private TextBox? input;
    private ConversationTranscript? answer;
    internal ConversationTranscript? Transcript=>answer;
    private Button? submitButton;
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
        UseSquareFrame();
        DismissButton=Ui.Button("Закрыть диалог",Close,quiet:true);
        DismissButton.Style=(Style)FindResource("DialogDismiss");
        DismissButton.Content=new System.Windows.Shapes.Path {Data=Geometry.Parse("M 0,0 L 6,6 M 6,0 L 0,6"),Width=6,Height=6,Stroke=Ui.Muted,StrokeThickness=1.2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};
        DismissButton.HorizontalAlignment=HorizontalAlignment.Right;DismissButton.VerticalAlignment=VerticalAlignment.Top;
        DismissButton.Margin=new Thickness(0,10,12,0);DismissButton.Visibility=Visibility.Collapsed;
        ((Grid)Content).Children.Add(DismissButton);
        ClearButton=Ui.Button("Очистить чат",ClearConversation,quiet:true);
        ClearButton.FontSize=10;ClearButton.Padding=new Thickness(4,1,4,1);
        ClearButton.HorizontalAlignment=HorizontalAlignment.Left;ClearButton.VerticalAlignment=VerticalAlignment.Top;
        ClearButton.Margin=new Thickness(10,1,0,0);ClearButton.Visibility=Visibility.Collapsed;
        ((Grid)Content).Children.Add(ClearButton);
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
        View=view; Title=title; Heading.Text=title;Body.Children.Clear();answer=null;input=null;submitButton=null;
        pendingFocus?.Abort();pendingFocus=null;focusInputWhenReady=false;
        Header.Visibility=Visibility.Visible;
        DismissButton.Visibility=IsMouseOver&&view!=ChatView.Error?Visibility.Visible:Visibility.Collapsed;
        RefreshClearButton();
    }
    internal void ShowInput(string? text=null,bool focus=true)
    {
        draft=text??draft;
        Reset(ChatView.Input,"Спросить "+petName);
        SizeToContent=SizeToContent.Height;Width=400;Height=double.NaN;
        ((StackPanel)Surface.Child).Margin=new Thickness(8);
        DismissButton.Margin=new Thickness(0,4,4,0);
        Header.Visibility=Visibility.Collapsed;
        Body.Children.Add(CreateComposer(false));
        focusInputWhenReady=focus;QueueInputFocus();
    }
    private Grid CreateComposer(bool embedded)
    {
        var line=new Grid();line.ColumnDefinitions.Add(new());
        var sendColumn=new ColumnDefinition {Width=new GridLength(embedded?40:0)};line.ColumnDefinitions.Add(sendColumn);
        line.ColumnDefinitions.Add(new(){Width=new GridLength(16)});
        input=Ui.Input(draft,"Вопрос модели");input.AcceptsReturn=true;input.TextWrapping=TextWrapping.Wrap;
        input.MinHeight=36;input.MaxHeight=embedded?88:144;input.Padding=new Thickness(8,8,6,8);input.FontSize=14;input.VerticalContentAlignment=VerticalAlignment.Top;
        input.Background=Brushes.Transparent;input.BorderThickness=new Thickness(0);
        input.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;input.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        input.ToolTip="Введите вопрос · Enter — отправить · Shift+Enter — новая строка";
        input.PreviewKeyDown+=(_,e)=> { if(HandleComposerKey(e.Key,Keyboard.Modifiers))e.Handled=true; };
        if(hudiStyle){input.Foreground=HudiText;input.CaretBrush=HudiText;}
        line.Children.Add(input);
        var placeholder=Ui.Text("Спросить "+petName+"…",14,hudiStyle?HudiText:Ui.Muted);placeholder.Opacity=.72;placeholder.Margin=new Thickness(8,8,6,8);placeholder.IsHitTestVisible=false;placeholder.VerticalAlignment=VerticalAlignment.Top;line.Children.Add(placeholder);
        var submit=submitButton=Ui.Button("Отправить",Submit,true);submit.Style=(Style)FindResource("ComposerSend");
        var arrow=new System.Windows.Shapes.Path {Data=Geometry.Parse("M 2,8 L 8,2 L 14,8 M 8,2 L 8,15"),StrokeThickness=1.5,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round,StrokeLineJoin=PenLineJoin.Round,Width=16,Height=17};
        arrow.SetBinding(System.Windows.Shapes.Shape.StrokeProperty,new Binding(nameof(Control.Foreground)){Source=submit});submit.Content=arrow;
        submit.ToolTip="Отправить · Enter";submit.VerticalAlignment=VerticalAlignment.Bottom;submit.HorizontalAlignment=HorizontalAlignment.Right;Grid.SetColumn(submit,1);line.Children.Add(submit);
        void Changed()
        {
            draft=input.Text;bool hasText=!string.IsNullOrWhiteSpace(draft);
            placeholder.Visibility=draft.Length==0?Visibility.Visible:Visibility.Collapsed;
            submit.IsEnabled=hasText&&!session.Busy;submit.Visibility=embedded||hasText?Visibility.Visible:Visibility.Collapsed;
            sendColumn.Width=new GridLength(embedded||hasText?40:0);
        }
        input.TextChanged+=(_,_)=>Changed();Changed();
        input.CaretIndex=input.Text.Length;
        return line;
    }
    private void QueueInputFocus()
    {
        if(!focusInputWhenReady||!IsLoaded||!IsVisible||IsClosing||View==ChatView.Error||input is null)return;
        var target=input;pendingFocus?.Abort();
        pendingFocus=Dispatcher.BeginInvoke(()=>
        {
            pendingFocus=null;
            if(!focusInputWhenReady||IsClosing||!IsVisible||View==ChatView.Error||!ReferenceEquals(input,target))return;
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
        string question=input.Text;input.Clear();draft="";send(question);
    }
    internal bool HandleComposerKey(Key key,ModifierKeys modifiers)
    {
        if(key!=Key.Enter||modifiers.HasFlag(ModifierKeys.Shift))return false;
        Submit();return true;
    }
    internal void ShowAnswer()
    {
        if(View==ChatView.Answer&&answer is not null&&input is not null){RefreshText();return;}
        Reset(ChatView.Answer,"Чат · "+petName);
        UseSquareFrame();DismissButton.Margin=new Thickness(0,5,7,0);
        Header.Visibility=Visibility.Collapsed;
        ((StackPanel)Surface.Child).Margin=new Thickness(14,20,14,12);
        var layout=new Grid{Height=Height-32};
        layout.RowDefinitions.Add(new(){Height=new GridLength(1,GridUnitType.Star)});
        layout.RowDefinitions.Add(new(){Height=GridLength.Auto});layout.RowDefinitions.Add(new(){Height=GridLength.Auto});
        answer=new ConversationTranscript(petName);
        layout.Children.Add(answer);
        var separator=new Border{Height=1,Background=PetAppearance.Current.Edge,Margin=new Thickness(0,6,0,6)};
        System.Windows.Automation.AutomationProperties.SetName(separator,"Разделитель переписки");Grid.SetRow(separator,1);layout.Children.Add(separator);
        var composer=CreateComposer(true);Grid.SetRow(composer,2);layout.Children.Add(composer);Body.Children.Add(layout);
        RefreshText();focusInputWhenReady=IsActive;QueueInputFocus();
    }
    internal void ShowError()
    {
        if(View==ChatView.Answer){if(input is not null&&string.IsNullOrEmpty(input.Text))input.Text=session.LastQuestion;RefreshText();return;}
        Reset(ChatView.Error,"Ошибка ответа");UseSquareFrame();var content=SquareBody();Body.Children.Add(content);
        content.RowDefinitions.Add(new());content.RowDefinitions.Add(new(){Height=GridLength.Auto});
        content.Children.Add(new ScrollViewer {Content=Ui.Text(session.Error,13,Ui.Error),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled});
        var row=Ui.Row(Ui.Button("Повторить",()=>send(session.LastQuestion),true),Ui.Button("Настройки",settings));row.Margin=new Thickness(0,14,0,0);Grid.SetRow(row,1);content.Children.Add(row);
    }
    private void RefreshText()
    {
        RefreshClearButton();
        if(View!=ChatView.Answer || answer is null)return;
        var messages=session.Messages.ToList();
        if(session.Busy||session.State is ChatState.Error or ChatState.Canceled||messages.Count==0)
        {
            string response=session.Busy?(string.IsNullOrEmpty(session.PartialAnswer)?"Печатает…":session.PartialAnswer):
                session.State==ChatState.Error?session.Error:session.State==ChatState.Canceled?"Ответ отменён.":session.Answer;
            if(!string.IsNullOrEmpty(session.LastQuestion))messages.Add(new("user",session.LastQuestion));
            if(!string.IsNullOrEmpty(response))messages.Add(new("assistant",response));
        }
        answer.UpdateMessages(messages);
        if(submitButton is not null)submitButton.IsEnabled=!session.Busy&&!string.IsNullOrWhiteSpace(input?.Text);
        answer.ToolTip=session.Truncated?"Ответ достиг лимита токенов. Лимит можно увеличить в настройках.":null;
    }
    private void RefreshClearButton()
    {
        ClearButton.Visibility=View==ChatView.Input?Visibility.Collapsed:Visibility.Visible;
        ClearButton.IsEnabled=!session.Busy&&(session.Messages.Count>0||session.LastQuestion.Length>0||session.Error.Length>0);
    }
    private void ClearConversation()
    {
        if(session.Busy)return;
        session.ClearHistory();
        if(View==ChatView.Error)ShowInput();else RefreshText();
        focusInputWhenReady=true;QueueInputFocus();
    }
}
