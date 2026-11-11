using System.ComponentModel;
using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

namespace HudiPet;

internal sealed class NotesWindow:GlassWindow
{
    private readonly NotesStore store;
    private readonly DispatcherTimer autosave=new(){Interval=TimeSpan.FromMilliseconds(450)};
    private PetNote? current,deleted;
    private TextBox? editor;
    private TextBlock? status;
    private Grid page=null!;
    private bool dirty;
    internal readonly Button DeleteButton;
    private readonly Button backButton;
    private readonly Button createButton;
    internal NotesWindow(PetWindow owner,NotesStore? store=null):base(owner,"Заметки",320,"\uE70B")
    {
        this.store=store??new NotesStore();SettingsAppearance.Apply(this);
        UseSquareFrame();Heading.FontSize=14;Header.Height=24;
        if(Header.Children[1] is TextBlock glyph){glyph.FontSize=20;glyph.Margin=new Thickness(0,0,8,0);}
        CloseButton.Width=24;CloseButton.Height=24;CloseButton.Padding=new Thickness(0);CloseButton.FontSize=18;
        createButton=SmallButton("Новая заметка",()=>Edit(null),"M 3,0 L 3,6 M 0,3 L 6,3");
        createButton.Width=createButton.Height=24;createButton.Visibility=Visibility.Visible;DockPanel.SetDock(createButton,Dock.Right);Header.Children.Insert(1,createButton);
        DeleteButton=SmallButton("Удалить заметку",Remove,"M 0,0 L 6,6 M 6,0 L 0,6");DeleteButton.HorizontalAlignment=HorizontalAlignment.Right;DeleteButton.VerticalAlignment=VerticalAlignment.Center;DeleteButton.Margin=new Thickness(0,0,8,0);
        backButton=SmallButton("Все заметки",ShowList,"M 5,0 L 1,3 L 5,6");backButton.HorizontalAlignment=HorizontalAlignment.Left;backButton.Margin=new Thickness(6,8,0,0);
        var root=(Grid)Content;root.Children.Add(DeleteButton);root.Children.Add(backButton);
        MouseEnter+=(_,_)=>ShowEditorActions(true);MouseLeave+=(_,_)=>ShowEditorActions(false);
        autosave.Tick+=(_,_)=>Flush();Closed+=(_,_)=>autosave.Stop();ShowList();
    }
    private static bool StorageError(Exception error)=>error is IOException or InvalidDataException or UnauthorizedAccessException or JsonException;
    private static Button SmallButton(string label,Action action,string geometry)
    {
        var button=Ui.Button(label,action,quiet:true);button.Style=(Style)Application.Current.FindResource("DialogDismiss");button.VerticalAlignment=VerticalAlignment.Top;button.Visibility=Visibility.Collapsed;
        button.Content=new System.Windows.Shapes.Path {Data=Geometry.Parse(geometry),Width=6,Height=6,Stroke=Ui.Muted,StrokeThickness=1.2,StrokeStartLineCap=PenLineCap.Round,StrokeEndLineCap=PenLineCap.Round};return button;
    }
    private void ShowEditorActions(bool hovered)
    {
        DeleteButton.Visibility=backButton.Visibility=hovered&&editor is not null?Visibility.Visible:Visibility.Collapsed;
    }
    private void Message(string text,bool error=false)
    {
        if(status is null)return;
        status.Text=text;status.Foreground=error?Ui.Error:Ui.Muted;
        // Successful autosaves are silent. Errors must remain visible.
        status.Visibility=error?Visibility.Visible:Visibility.Collapsed;
    }
    private void AddStatus()
    {
        status=Ui.Text("",11,Ui.Muted);status.Margin=new Thickness(0,6,0,0);status.Visibility=Visibility.Collapsed;
        AddRow(status,GridLength.Auto);
    }
    private void BeginPage(bool showHeader)
    {
        SizeToContent=SizeToContent.Height;Height=double.NaN;
        ((StackPanel)Surface.Child).Margin=showHeader?new Thickness(12):new Thickness(24,12,28,12);
        Header.Margin=new Thickness(0);
        Body.Children.Clear();Header.Visibility=showHeader?Visibility.Visible:Visibility.Collapsed;
        page=new Grid();Body.Children.Add(page);
    }
    private void AddRow(FrameworkElement element,GridLength height)
    {
        Grid.SetRow(element,page.RowDefinitions.Count);page.RowDefinitions.Add(new(){Height=height});page.Children.Add(element);
    }
    internal bool Flush()
    {
        autosave.Stop();
        if(!dirty||current is null||editor is null)return true;
        try
        {
            if(current.Text.Length==0&&string.IsNullOrWhiteSpace(editor.Text)){dirty=false;Message("");return true;}
            var updated=current with {Text=editor.Text,Updated=DateTimeOffset.Now};store.Put(updated);current=updated;dirty=false;Message("");return true;
        }
        catch(Exception error) when(StorageError(error)){Message("Не удалось сохранить. Текст остаётся в окне — попробуйте ещё раз.",true);return false;}
    }
    internal void ShowList()
    {
        if(!Flush())return;
        current=null;editor=null;Heading.Text="Заметки";BeginPage(true);ShowEditorActions(false);
        createButton.IsEnabled=true;
        var rows=new StackPanel();AddRow(new ScrollViewer {Content=rows,MaxHeight=Math.Min(260,Math.Max(120,ScreenPlacement.WorkArea(Owner).Height-200)),VerticalScrollBarVisibility=ScrollBarVisibility.Auto,HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled},new GridLength(1,GridUnitType.Star));
        AddStatus();
        try
        {
            var notes=store.Read();
            if(notes.Length>0)Header.Margin=new Thickness(0,0,0,6);
            foreach(var note in notes)
            {
                var title=Ui.Text(note.Title,13);title.MaxHeight=36;
                var row=Ui.Button(note.Title,()=>Edit(note),quiet:true);row.Content=title;row.HorizontalContentAlignment=HorizontalAlignment.Stretch;row.Padding=new Thickness(8,6,28,6);row.Background=new SolidColorBrush(Color.FromArgb(16,216,237,249));
                var card=new Grid {Margin=new Thickness(0,2,0,2)};card.Children.Add(row);
                var remove=SmallButton("Удалить заметку",()=>RemoveNote(note),"M 0,0 L 6,6 M 6,0 L 0,6");remove.HorizontalAlignment=HorizontalAlignment.Right;remove.VerticalAlignment=VerticalAlignment.Center;remove.Margin=new Thickness(0,0,6,0);card.Children.Add(remove);
                card.MouseEnter+=(_,_)=>remove.Visibility=Visibility.Visible;card.MouseLeave+=(_,_)=>remove.Visibility=Visibility.Collapsed;rows.Children.Add(card);
            }
            if(store.RecoveredBackup){Message("Заметки восстановлены из резервной копии.");status!.Visibility=Visibility.Visible;}
            if(deleted is not null)
            {
                var undo=Ui.Button("Отменить удаление",()=>
                {
                    try{store.Put(deleted);deleted=null;ShowList();}catch(Exception error) when(StorageError(error)){Message("Не удалось восстановить заметку.",true);}
                },quiet:true);undo.Margin=new Thickness(0,8,0,0);AddRow(undo,GridLength.Auto);
            }
        }
        catch(Exception error) when(StorageError(error)){createButton.IsEnabled=false;Message("Не удалось прочитать заметки. Файлы сохранены без изменений.",true);}
    }
    internal void Edit(PetNote? note)
    {
        if(!Flush())return;
        current=note??new PetNote(Guid.NewGuid().ToString("N"),"",DateTimeOffset.Now,DateTimeOffset.Now);Heading.Text="Заметка";BeginPage(false);
        editor=Ui.Input(current.Text,"Текст заметки");editor.Style=(Style)FindResource("SettingsInput");editor.AcceptsReturn=true;editor.AcceptsTab=true;editor.TextWrapping=TextWrapping.Wrap;editor.VerticalContentAlignment=VerticalAlignment.Top;editor.MinHeight=24;editor.MaxHeight=220;editor.MaxLength=NotesStore.MaxTextLength;editor.VerticalScrollBarVisibility=ScrollBarVisibility.Auto;editor.HorizontalScrollBarVisibility=ScrollBarVisibility.Disabled;
        editor.Padding=new Thickness(0,4,0,4);editor.FontSize=13;editor.BorderThickness=new Thickness(0);editor.Background=Brushes.Transparent;editor.SetResourceReference(TextBox.ForegroundProperty,"TextBrush");editor.SetResourceReference(TextBox.CaretBrushProperty,"TextBrush");
        editor.TextChanged+=(_,_)=>{dirty=true;Message("");autosave.Stop();autosave.Start();};AddRow(editor,new GridLength(1,GridUnitType.Star));
        AddStatus();ShowEditorActions(IsMouseOver);
        Dispatcher.BeginInvoke(()=>editor?.Focus(),DispatcherPriority.Input);
    }
    internal void Remove()
    {
        if(current is null||!Flush())return;
        RemoveNote(current);
    }
    private void RemoveNote(PetNote note)
    {
        if(!Flush())return;
        try{store.Delete(note.Id);deleted=note;dirty=false;current=null;ShowList();}
        catch(Exception error) when(StorageError(error)){Message("Не удалось удалить заметку.",true);}
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if(!Flush())e.Cancel=true;
        base.OnClosing(e);
    }
}
