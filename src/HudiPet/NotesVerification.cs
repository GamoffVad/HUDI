using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Automation;
using System.Windows.Documents;
using System.Windows.Media.Imaging;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using Hudi.Distribution;

namespace HudiPet;

internal static class NotesVerification
{
    internal static int Run(string directory)
    {
        Directory.CreateDirectory(directory);
        var app = new Application { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        Ui.Initialize();
        var pet = new PetWindow(false, true) { Opacity = 0, ShowInTaskbar = false };
        pet.Show();
        try { Run(pet, directory); return 0; }
        finally { pet.Close(); app.Shutdown(); }
    }

    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);if(child is T value)yield return value;
            foreach(var nested in Children<T>(child))yield return nested;
        }
    }
    private static string PlainText(RichTextBox box) => new TextRange(box.Document.ContentStart, box.Document.ContentEnd).Text;

    internal static void Run(PetWindow pet,string directory)
    {
        var checks=new Dictionary<string,bool>();string fixtures=Path.Combine(directory,"notes-fixture-"+Guid.NewGuid().ToString("N"));
        var store=new NotesStore(fixtures);var note=new PetNote(Guid.NewGuid().ToString("N"),"Идея на завтра\nПопробовать нового питомца 🐾",DateTimeOffset.Now,DateTimeOffset.Now);
        store.Put(note);checks["unicode_survives_reopen"]=new NotesStore(fixtures).Read().Single().Text==note.Text;
        note=note with {Text=note.Text+"\nНе забыть сохранить результат.",Updated=DateTimeOffset.Now};store.Put(note);
        var second=new PetNote(Guid.NewGuid().ToString("N"),"Список дел\n• Закончить проект\n• Сделать перерыв",DateTimeOffset.Now,DateTimeOffset.Now.AddSeconds(1));store.Put(second);
        checks["edited_note_keeps_identity"]=store.Read().Single(n=>n.Id==note.Id).Created==note.Created;
        checks["latest_note_is_first"]=store.Read()[0].Id==second.Id;
        store.Delete(second.Id);checks["delete_affects_only_selected_note"]=store.Read().Single().Id==note.Id;store.Put(second);checks["undo_can_restore_note"]=store.Read().Length==2;
        var empty = new NotesWindow(pet, new NotesStore(Path.Combine(fixtures, "empty")));
        VisualPreview.Save(empty, Path.Combine(directory, "notes-empty.png"));
        checks["empty_board_is_compact"] = ((FrameworkElement)empty.Content).ActualHeight <= 55;
        empty.Show();
        UiVerification.Advance(30);
        var plus = Children<Button>(empty.Header).Single(b => AutomationProperties.GetName(b) == "Новая заметка");
        checks["create_is_only_a_plus"] = Equals(plus.Content, "+") && plus.ToolTip is null;
        plus.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        VisualPreview.Save(empty, Path.Combine(directory, "notes-new.png"));
        checks["plus_adds_empty_editor"] = empty.OwnedWindows.Cast<Window>().Count(noteWindow => noteWindow.IsVisible) == 1;
        empty.Close();

        var window = new NotesWindow(pet, store);
        window.Show();
        window.Activate();
        UiVerification.Advance(50);
        var noteWindows = window.OwnedWindows.Cast<Window>().ToArray();
        checks["existing_notes_are_open_in_separate_windows"] = noteWindows.Length == 2
            && noteWindows.All(noteWindow => noteWindow.IsVisible)
            && noteWindows.Select(noteWindow => (noteWindow.Left, noteWindow.Top)).Distinct().Count() == 2;
        var editingWindow = noteWindows.Single(noteWindow => PlainText(Children<RichTextBox>(noteWindow).Single()).Contains(note.Title));
        var otherWindow = noteWindows.Single(noteWindow => noteWindow != editingWindow);
        var editor = Children<RichTextBox>(editingWindow).Single();
        Button[] Tools(string name) => noteWindows.SelectMany(noteWindow => Children<Button>(noteWindow))
            .Where(button => AutomationProperties.GetName(button) == name).ToArray();
        bool Focus(Window noteWindow)
        {
            noteWindow.Activate();
            Keyboard.Focus(Children<RichTextBox>(noteWindow).Single());
            UiVerification.Advance(40);
            return noteWindow.IsActive;
        }
        var bold = Tools("Жирный");
        checks["focus_shows_one_toolbar"] = Focus(editingWindow) && bold.Count(button => button.IsVisible) == 1;
        var grip = Children<System.Windows.Controls.Primitives.Thumb>(editingWindow)
            .Single(control => AutomationProperties.GetName(control) == "Изменить размер заметки");
        double widthBefore = editingWindow.Width, heightBefore = editingWindow.Height;
        double otherWidth = otherWindow.Width, otherHeight = otherWindow.Height;
        grip.RaiseEvent(new System.Windows.Controls.Primitives.DragDeltaEventArgs(36, 22)
        { RoutedEvent = System.Windows.Controls.Primitives.Thumb.DragDeltaEvent });
        checks["resize_changes_only_the_active_note"] = grip.IsVisible && editingWindow.Width > widthBefore
            && editingWindow.Height > heightBefore && otherWindow.Width == otherWidth && otherWindow.Height == otherHeight;
        UiVerification.Advance(330);
        checks["opening_animation_completes"] = noteWindows.All(noteWindow => ((FrameworkElement)noteWindow.Content).Opacity == 1);
        var paragraph = new Paragraph(new Run("Список для проверки"));
        editor.Document.Blocks.Clear();
        editor.Document.Blocks.Add(paragraph);
        editor.Selection.Select(paragraph.ContentStart, paragraph.ContentEnd);
        Tools("Жирный").Single(button => button.IsVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Tools("Зачёркивание").Single(button => button.IsVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        Tools("Маркированный список").Single(button => button.IsVisible).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var bitmap = BitmapSource.Create(2, 2, 96, 96, PixelFormats.Bgra32, null, Enumerable.Repeat((byte)255, 16).ToArray(), 8);
        editor.Document.Blocks.Add(new Paragraph(new InlineUIContainer(new Image { Source = bitmap, Width = 16, Height = 16 })));
        VisualPreview.Save(editingWindow, Path.Combine(directory, "note-active.png"));
        bool switched = Focus(otherWindow);
        checks["switching_notes_moves_toolbar"] = switched && bold.Count(button => button.IsVisible) == 1
            && editor.IsKeyboardFocusWithin == false;
        var away = new Window { Width = 80, Height = 80, ShowInTaskbar = false, Opacity = 0 };
        away.Show();
        away.Activate();
        UiVerification.Advance(80);
        checks["inactive_board_hides_every_toolbar"] = noteWindows.All(noteWindow => !noteWindow.IsActive) && bold.All(button => !button.IsVisible);
        VisualPreview.Save(editingWindow, Path.Combine(directory, "note-inactive.png"));
        away.Close();
        UiVerification.Advance(500);
        var saved = new NotesStore(fixtures).Read().Single(n => n.Id == note.Id);
        checks["typing_autosaves_without_button"] = saved.Text.Contains("Список для проверки") && saved.RichText is { Length: > 0 };
        window.Close();
        var reopened = new NotesWindow(pet, store);
        reopened.Show();
        reopened.Activate();
        UiVerification.Advance(60);
        var loadedWindow = reopened.OwnedWindows.Cast<Window>()
            .Single(noteWindow => PlainText(Children<RichTextBox>(noteWindow).Single()).Contains("Список для проверки"));
        var loaded = Children<RichTextBox>(loadedWindow).Single();
        checks["note_size_is_restored"] = saved.WindowSize is { } storedSize
            && Math.Abs(loadedWindow.Width - storedSize.Width) < .1 && Math.Abs(loadedWindow.Height - storedSize.Height) < .1;
        var list = loaded.Document.Blocks.OfType<System.Windows.Documents.List>().FirstOrDefault();
        var formatted = list?.ListItems.FirstListItem?.Blocks.FirstBlock as Paragraph;
        var formattedRange = formatted is null ? null : new TextRange(formatted.ContentStart, formatted.ContentEnd);
        checks["list_bold_and_strike_survive_reopen"] = formattedRange is not null
            && Equals(formattedRange.GetPropertyValue(TextElement.FontWeightProperty), FontWeights.Bold)
            && formattedRange.GetPropertyValue(Inline.TextDecorationsProperty) is TextDecorationCollection decorations
            && decorations.Any(decoration => decoration.Location == TextDecorationLocation.Strikethrough);
        checks["image_survives_reopen"] = loaded.Document.Blocks.OfType<Paragraph>()
            .SelectMany(block => block.Inlines.OfType<InlineUIContainer>()).Any(item => item.Child is Image);
        Focus(loadedWindow);
        Children<Button>(loadedWindow).Single(b => AutomationProperties.GetName(b) == "Скрыть заметку")
            .RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checks["closing_one_note_keeps_others_and_does_not_delete"] = !loadedWindow.IsVisible
            && reopened.OwnedWindows.Cast<Window>().Count(noteWindow => noteWindow.IsVisible) == 1 && store.Read().Length == 2;
        reopened.ShowAll();
        UiVerification.Advance(300);
        checks["reopening_notes_restores_hidden_card"] = loadedWindow.IsVisible;
        Focus(loadedWindow);
        var noteMenu = Children<Button>(loadedWindow).Single(b => AutomationProperties.GetName(b) == "Действия с заметкой").ContextMenu;
        ((MenuItem)noteMenu.Items[0]).RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        checks["delete_affects_one_card"] = store.Read().Single().Id == second.Id;
        Children<Button>(reopened.Body).Single(b => Equals(b.Content, "Отменить удаление")).RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checks["undo_restores_rich_note"] = store.Read().Single(n => n.Id == note.Id).RichText?.SequenceEqual(saved.RichText!) == true;
        reopened.Close();

        string primary = Path.Combine(fixtures, "notes.json");
        File.WriteAllText(primary, "broken fixture");
        var recovered = new NotesStore(fixtures);
        checks["corrupt_primary_recovers_backup"] = recovered.Read().Length > 0 && recovered.RecoveredBackup;
        recovered.Put(note);
        checks["recovered_store_can_save_again"] = recovered.Read().Any(n => n.Id == note.Id && n.Text == note.Text);
        string invalid = Path.Combine(fixtures, "unsupported");
        Directory.CreateDirectory(invalid);
        File.WriteAllText(Path.Combine(invalid, "notes.json"), "{\"Version\":99,\"Notes\":[]}");
        try { new NotesStore(invalid).Put(note); checks["unknown_format_not_overwritten"] = false; }
        catch (InvalidDataException) { checks["unknown_format_not_overwritten"] = File.ReadAllText(Path.Combine(invalid, "notes.json")).Contains("99"); }
        pet.OpenMenu();var menu=pet.OwnedWindows.OfType<GlassWindow>().Single(w=>w.Title==pet.PetName);VisualPreview.Save(menu,Path.Combine(directory,"context-menu.png"));
        checks["compact_menu"]=menu.Width==270&&menu.ActualHeight<350;
        var notesButton=Children<Button>(menu.Body).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Заметки");notesButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        var liveNotes=pet.OwnedWindows.OfType<NotesWindow>().Single(w=>w.IsVisible);checks["menu_opens_notes_without_closing_pet"]=pet.IsVisible&&liveNotes.IsVisible;liveNotes.Close();
        pet.QueuePetClick(2,control:true);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+40);
        liveNotes=pet.OwnedWindows.OfType<NotesWindow>().Single(w=>w.IsVisible);
        checks["ctrl_double_click_opens_notes_without_chat"]=liveNotes.IsVisible&&pet.IsVisible&&!pet.OwnedWindows.OfType<ChatWindow>().Any(w=>w.IsVisible);
        pet.QueuePetClick(2,control:true);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+40);
        checks["ctrl_double_click_reuses_open_notes"]=pet.OwnedWindows.OfType<NotesWindow>().Count(w=>w.IsVisible)==1&&ReferenceEquals(liveNotes,pet.OwnedWindows.OfType<NotesWindow>().Single(w=>w.IsVisible));
        liveNotes.Close();
        File.WriteAllText(Path.Combine(directory,"notes-checks.json"),JsonSerializer.Serialize(new{success=checks.Values.All(v=>v),checks},new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Values.Any(v=>!v))throw new InvalidOperationException("Notes verification failed");
    }
}

internal sealed partial class PetWindow
{
    private sealed class PendingPreviewClient:IModelClient
    {
        internal readonly TaskCompletionSource<ModelReply> Reply=new();
        public Task<ModelReply> CompleteAsync(ModelSettings settings,string key,IReadOnlyList<ChatMessage> messages,IProgress<string>? progress,CancellationToken ct)=>Reply.Task;
        public Task<string[]> ListModelsAsync(ModelSettings settings,string key,CancellationToken ct)=>Task.FromResult(new[]{"fixture"});
    }
    internal void VerifyChatPresentation(string directory)
    {
        chatWindow?.Close();chatWindow=null;chat.Dispose();var client=new PendingPreviewClient();chat=new ChatSession(client);displayedState=ChatState.Idle;BindChatEvents();
        var checks=new Dictionary<string,bool>();var area=ScreenPlacement.WorkArea(this);Top=area.Top+400;
        var request=chat.SendAsync("Тест автоматического показа ответа",new ModelSettings{Model="fixture",UseApiKey=false},"");
        checks["thinking_uses_only_transparent_dots"]=thinkingWindow is {IsVisible:true,AllowsTransparency:true}&&thinkingWindow.Content is StackPanel dots&&dots.Children.Count==3&&chatWindow is null;
        checks["thinking_has_gap_above_pet"]=thinkingWindow is not null&&Top-(thinkingWindow.Top+thinkingWindow.Height)>=11;
        var indicator=thinkingWindow!;UiVerification.Advance(300);
        checks["thinking_uses_generated_images"]=indicator.Dots.Count==3&&indicator.Dots.All(dot=>dot.Source is not null);
        var firstPhases=indicator.Dots.Select(dot=>dot.Opacity).ToArray();
        checks["thinking_dots_move_in_staggered_phases"]=!SystemParameters.ClientAreaAnimation||(firstPhases.Max()-firstPhases.Min()>.05&&indicator.Dots.Any(dot=>((TranslateTransform)((TransformGroup)dot.RenderTransform).Children[1]).Y<-.2));
        VisualPreview.Save(indicator,Path.Combine(directory,"thinking-dots.png"));UiVerification.Advance(180);
        checks["thinking_animation_changes_over_time"]=!SystemParameters.ClientAreaAnimation||indicator.Dots.Select((dot,index)=>Math.Abs(dot.Opacity-firstPhases[index])).Any(change=>change>.02);
        client.Reply.SetResult(new ModelReply("Ответ появился сам — нажимать на питомца не нужно."));
        if(!request.IsCompleted)
        {
            var frame=new DispatcherFrame();var poll=new DispatcherTimer{Interval=TimeSpan.FromMilliseconds(20)};int ticks=0;poll.Tick+=(_,_)=>{if(request.IsCompleted||++ticks>100){poll.Stop();frame.Continue=false;}};poll.Start();Dispatcher.PushFrame(frame);
        }
        request.GetAwaiter().GetResult();
        chatWindow?.UpdateLayout();chatWindow?.Place();
        checks["answer_opens_without_click"]=chatWindow is {IsVisible:true,View:ChatView.Answer}&&thinkingWindow is null;
        checks["answer_does_not_take_keyboard_focus"]=chatWindow?.ShowActivated==false;
        var answerClip=((FrameworkElement)chatWindow!.Content).Clip.Bounds;
        checks["automatic_answer_opens_from_circle"]=!SystemParameters.ClientAreaAnimation||(chatWindow.OpeningAnimationRunning&&answerClip.Width<=20&&Math.Abs(answerClip.Width-answerClip.Height)<1);
        VisualPreview.Save(chatWindow,Path.Combine(directory,"automatic-answer-circle.png"));
        UiVerification.Advance(140);
        checks["automatic_answer_expands_with_matte"]=DialogBackdrop.IsMatteApplied(chatWindow)&&(!SystemParameters.ClientAreaAnimation||(chatWindow.Surface.RevealProgress>0&&chatWindow.Surface.RevealProgress<1));
        VisualPreview.Save(chatWindow,Path.Combine(directory,"automatic-answer-expanding.png"));
        UiVerification.Advance(300);
        checks["automatic_answer_finishes_reveal"]=!chatWindow.OpeningAnimationRunning&&chatWindow.Surface.RevealProgress==1&&chatWindow.Surface.Child.Opacity==1;
        checks["answer_has_gap_above_pet"]=chatWindow is not null&&Top-(chatWindow.Top+chatWindow.ActualHeight)>=11;
        checks["thinking_animation_stops_when_answer_arrives"]=indicator.Dots.All(dot=>!dot.HasAnimatedProperties&&((TransformGroup)dot.RenderTransform).Children.All(t=>!t.HasAnimatedProperties));
        if(chatWindow is not null)VisualPreview.Save(chatWindow,Path.Combine(directory,"automatic-answer.png"));
        chatWindow?.Close();chatWindow=null;
        OpenChat(false);
        checks["last_answer_reopens_with_same_animation"]=chatWindow is {View:ChatView.Answer,IsVisible:true}&&(!SystemParameters.ClientAreaAnimation||chatWindow.OpeningAnimationRunning);
        UiVerification.Advance(420);var existingAnswer=chatWindow;OpenChat(false);
        checks["visible_answer_does_not_restart_animation"]=ReferenceEquals(existingAnswer,chatWindow)&&!chatWindow!.OpeningAnimationRunning;
        chatWindow?.Close();chatWindow=null;
        File.WriteAllText(Path.Combine(directory,"chat-presentation.json"),JsonSerializer.Serialize(new{success=checks.Values.All(v=>v),checks},new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Values.Any(v=>!v))throw new InvalidOperationException("Chat presentation verification failed");
    }
}
