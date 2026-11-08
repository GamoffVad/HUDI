using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Interop;
using System.Windows.Media.Imaging;
using System.Runtime.InteropServices;
using Hudi.Distribution;

namespace HudiPet;

internal static class InteractionUiVerification
{
    [DllImport("user32.dll",EntryPoint="GetWindowLongW")] private static extern int GetWindowLong(IntPtr window,int index);
    [DllImport("user32.dll")] private static extern int GetWindowRgn(IntPtr window,IntPtr region);
    [DllImport("gdi32.dll")] private static extern IntPtr CreateRectRgn(int left,int top,int right,int bottom);
    [DllImport("gdi32.dll")] private static extern bool PtInRegion(IntPtr region,int x,int y);
    [DllImport("gdi32.dll")] private static extern bool DeleteObject(IntPtr value);
    private static bool HasNativeAlpha(Window window)=>(GetWindowLong(new WindowInteropHelper(window).Handle,-20)&0x80000)!=0;
    private static bool NativeRegionPreservesCorners(GlassWindow window)
    {
        var content=(FrameworkElement)window.Content;content.UpdateLayout();
        var dpi=VisualTreeHelper.GetDpi(window);
        int width=(int)Math.Ceiling(content.ActualWidth*dpi.DpiScaleX),height=(int)Math.Ceiling(content.ActualHeight*dpi.DpiScaleY);
        var bitmap=new RenderTargetBitmap(width,height,96*dpi.DpiScaleX,96*dpi.DpiScaleY,PixelFormats.Pbgra32);bitmap.Render(content);
        var pixels=new byte[width*height*4];bitmap.CopyPixels(pixels,width*4,0);
        var region=CreateRectRgn(0,0,0,0);
        try
        {
            var handle=DialogBackdrop.ShapedHandle(window);
            if(GetWindowRgn(handle!=IntPtr.Zero?handle:new WindowInteropHelper(window).Handle,region)==0)return false;
            int cornerX=(int)Math.Ceiling(FrostedBackdrop.CornerRadius*dpi.DpiScaleX)+1,cornerY=(int)Math.Ceiling(FrostedBackdrop.CornerRadius*dpi.DpiScaleY)+1;
            for(int y=0;y<cornerY;y++)for(int x=0;x<cornerX;x++)
                foreach(var point in new[]{(x,y),(width-1-x,y),(x,height-1-y),(width-1-x,height-1-y)})
                    if(pixels[(point.Item2*width+point.Item1)*4+3]>0&&!PtInRegion(region,point.Item1,point.Item2))return false;
            return true;
        }
        finally{DeleteObject(region);}
    }
    private static byte PixelAlpha(GlassWindow window,int x,int y)
    {
        var content=(FrameworkElement)window.Content;content.UpdateLayout();
        var bitmap=new RenderTargetBitmap((int)Math.Ceiling(content.ActualWidth),(int)Math.Ceiling(content.ActualHeight),96,96,PixelFormats.Pbgra32);bitmap.Render(content);
        var pixel=new byte[4];bitmap.CopyPixels(new Int32Rect(x,y,1,1),pixel,4,0);return pixel[3];
    }
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++)
        {
            var child=VisualTreeHelper.GetChild(root,i);
            if(child is T value)yield return value;
            foreach(var nested in Children<T>(child))yield return nested;
        }
    }
    internal static void Run(PetWindow pet,IModelClient client,string directory)
    {
        var checks=new Dictionary<string,bool>();var sent=new List<string>();
        using var session=new ChatSession(client);
        var chat=new ChatWindow(pet,session,sent.Add,()=>{});
        double Render(string name)
        {
            VisualPreview.Save(chat,Path.Combine(directory,name+".png"));
            return ((FrameworkElement)chat.Content).ActualHeight;
        }
        double single=Render("question");
        var input=Children<TextBox>(chat.Body).Single();var send=Children<Button>(chat.Body).Single();
        checks["composer_starts_with_one_line"]=single<=56&&chat.Width==400&&chat.Header.Visibility==Visibility.Collapsed&&chat.Body.Children.Count==1;
        checks["only_one_inline_send_button"]=ReferenceEquals(input.Parent,send.Parent)&&!send.IsEnabled;
        checks["empty_composer_hides_send"]=send.Visibility==Visibility.Collapsed;
        checks["close_hidden_without_hover"]=chat.DismissButton.Visibility==Visibility.Collapsed;
        input.Text="Расскажи, как устроены повороты и смена настроения у моего питомца. Хочу, чтобы движения выглядели естественно и плавно.";
        double wrapped=Render("question-expanded");
        checks["wrapped_text_grows_composer"]=wrapped>single+20&&send.IsEnabled;
        checks["text_reveals_send"]=send.Visibility==Visibility.Visible&&send.Content is System.Windows.Shapes.Path;
        chat.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});Render("question-hover");
        var closeBounds=new Rect(chat.DismissButton.TransformToAncestor((Visual)chat.Content).Transform(new Point()),chat.DismissButton.RenderSize);
        var sendBounds=new Rect(send.TransformToAncestor((Visual)chat.Content).Transform(new Point()),send.RenderSize);
        checks["hover_shows_small_close_without_overlapping_send"]=chat.DismissButton.Visibility==Visibility.Visible&&chat.DismissButton.Width<=18&&!closeBounds.IntersectsWith(sendBounds);
        chat.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseLeaveEvent});
        checks["leaving_dialog_hides_close"]=chat.DismissButton.Visibility==Visibility.Collapsed;
        checks["send_stays_inside_last_line"]=send.TransformToAncestor(chat.Body).Transform(new Point(0,send.ActualHeight)).Y<=chat.Body.ActualHeight+1;
        input.Text=string.Join('\n',Enumerable.Repeat("Длинный вопрос",60));
        double capped=Render("question-long");
        checks["long_text_scrolls_at_height_limit"]=capped<=164&&input.ExtentHeight>input.ViewportHeight;
        input.Text="";double shrunk=Render("question");checks["deleting_text_shrinks_composer"]=Math.Abs(shrunk-single)<1;
        input.Text="  ";send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));checks["blank_question_not_sent"]=sent.Count==0&&send.Visibility==Visibility.Collapsed;
        input.Text="Вопрос кнопкой";send.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));checks["inline_button_sends"]=sent.SequenceEqual(new[]{"Вопрос кнопкой"})&&chat.Draft=="";
        chat.ShowInput("Вопрос клавишей Enter");Render("question-keyboard");input=Children<TextBox>(chat.Body).Single();
        // Supply modifiers explicitly: the user may be typing while this runs.
        checks["shift_enter_keeps_text_for_newline"]=!chat.HandleComposerKey(Key.Enter,ModifierKeys.Shift)&&input.AcceptsReturn&&sent.Count==1;
        checks["enter_sends_without_newline"]=chat.HandleComposerKey(Key.Enter,ModifierKeys.None)&&sent.Last()=="Вопрос клавишей Enter";
        chat.ShowInput("Несохранённый черновик");chat.ShowInput();Render("question-draft");
        checks["reopening_input_keeps_draft"]=Children<TextBox>(chat.Body).Single().Text=="Несохранённый черновик";
        chat.ShowAnswer();chat.ShowInput("Возвращённый вопрос");Render("question-restored");
        checks["cancel_can_restore_question"]=chat.Draft=="Возвращённый вопрос"&&chat.Header.Visibility==Visibility.Collapsed;
        chat.ShowInput("");chat.Opacity=0;chat.Show();chat.UpdateLayout();double nativeSingle=chat.ActualHeight;
        input=Children<TextBox>(chat.Body).Single();input.Text=string.Join('\n',Enumerable.Repeat("Проверка высоты окна",8));chat.UpdateLayout();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        checks["live_window_grows_with_matching_clip"]=chat.ActualHeight>nativeSingle+40&&Math.Abs(((FrameworkElement)chat.Content).Clip.Bounds.Height-chat.ActualHeight)<2;
        input.Text="";chat.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        checks["live_empty_window_shrinks"]=Math.Abs(chat.ActualHeight-nativeSingle)<2;
        chat.Close();

        pet.OpenChat(true);var live=pet.OwnedWindows.OfType<ChatWindow>().Single();live.ShowInput("Сохранить после Escape");
        live.RaiseEvent(new MouseEventArgs(Mouse.PrimaryDevice,0){RoutedEvent=Mouse.MouseEnterEvent});live.DismissButton.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        checks["hover_close_closes_dialog_only"]=live.IsClosing&&pet.IsVisible;pet.OpenChat(true);
        live=pet.OwnedWindows.OfType<ChatWindow>().Single();checks["closing_and_reopening_keeps_draft"]=live.Draft=="Сохранить после Escape";live.Close();

        var firstAction=pet.TriggerRandomClickAction();var secondAction=pet.TriggerRandomClickAction();
        checks["single_click_changes_action_without_opening_chat"]=firstAction is not null&&secondAction is not null&&firstAction!=secondAction&&!pet.OwnedWindows.OfType<ChatWindow>().Any(w=>w.IsVisible);
        pet.QueuePetClick(1);pet.QueuePetClick(2);
        checks["double_click_waits_for_possible_third_click"]=!pet.OwnedWindows.OfType<NotesWindow>().Any(w=>w.IsVisible)&&!pet.OwnedWindows.OfType<ChatWindow>().Any(w=>w.IsVisible);
        pet.QueuePetClick(3);live=pet.OwnedWindows.OfType<ChatWindow>().Single();
        checks["triple_click_starts_opening_animation"]=!SystemParameters.ClientAreaAnimation||live.OpeningAnimationRunning;
        var firstClip=((FrameworkElement)live.Content).Clip.Bounds;
        checks["opening_starts_as_small_circle"]=!SystemParameters.ClientAreaAnimation||(firstClip.Width<=20&&Math.Abs(firstClip.Width-firstClip.Height)<1);
        VisualPreview.Save(live,Path.Combine(directory,"opening-circle.png"));
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        var focused=Children<TextBox>(live.Body).Single();
        checks["triple_click_opens_composer"]=live.View==ChatView.Input&&live.IsVisible;
        checks["composer_receives_keyboard_focus"]=focused.IsKeyboardFocused&&ReferenceEquals(Keyboard.FocusedElement,focused);
        checks["caret_is_at_end_of_preserved_draft"]=focused.Text=="Сохранить после Escape"&&focused.CaretIndex==focused.Text.Length&&focused.SelectionLength==0;
        UiVerification.Advance(140);
        var growingClip=((FrameworkElement)live.Content).Clip.Bounds;
        checks["opening_expands_before_settling"]=!SystemParameters.ClientAreaAnimation||(live.Surface.RevealProgress>0&&live.Surface.RevealProgress<1&&growingClip.Width>firstClip.Width&&growingClip.Width<live.ActualWidth);
        var openingRegion=CreateRectRgn(0,0,0,0);
        try{checks["opening_matte_follows_reveal"]=!SystemParameters.ClientAreaAnimation||(GetWindowRgn(DialogBackdrop.ShapedHandle(live),openingRegion)!=0&&!PtInRegion(openingRegion,2,2));}
        finally{DeleteObject(openingRegion);}
        VisualPreview.Save(live,Path.Combine(directory,"opening-expanding.png"));
        UiVerification.Advance(300);
        checks["opening_settles_at_full_opacity_and_size"]=!live.OpeningAnimationRunning&&live.Surface.RevealProgress==1&&live.Surface.Child.Opacity==1&&Math.Abs(((FrameworkElement)live.Content).Clip.Bounds.Width-live.ActualWidth)<1;
        UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+40);
        checks["triple_click_cancels_pending_notes"]=!pet.OwnedWindows.OfType<NotesWindow>().Any(w=>w.IsVisible);
        pet.QueuePetClick(3);checks["already_open_chat_does_not_restart_animation"]=!live.OpeningAnimationRunning;
        live.ShowAnswer();
        System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        checks["old_focus_request_does_not_restore_composer"]=live.View==ChatView.Answer&&!focused.IsKeyboardFocused;
        live.Close();
        var answerClient=new UiVerification.PreviewClient();using var answerSession=new ChatSession(answerClient);
        answerSession.SendAsync("Пример",new(){Model="preview",UseApiKey=false},"").GetAwaiter().GetResult();
        live=new ChatWindow(pet,answerSession,_=>{},()=>{});live.ShowAnswer();live.Show();live.UpdateLayout();
        var reply=Children<TextBox>(live.Body).Single();
        VisualPreview.Save(live,Path.Combine(directory,"answer-shaped.png"));
        double shortAnswerHeight=live.ActualHeight;
        checks["answer_is_square_and_selectable"]=live.ActualWidth==320&&live.ActualHeight==320&&live.Header.Visibility==Visibility.Collapsed&&reply.IsReadOnly&&!Children<Button>(live.Body).Any();
        checks["hudi_question_and_answer_share_shoe_blue"]=pet.SelectedPetId!=PetCatalog.Hudi.Id||(focused.Foreground is SolidColorBrush questionColor&&reply.Foreground is SolidColorBrush answerColor&&questionColor.Color==Color.FromRgb(184,216,239)&&answerColor.Color==questionColor.Color);
        var bubbleClip=((FrameworkElement)live.Content).Clip;
        checks["answer_has_square_clip"]=live.Surface.SquareFrame&&bubbleClip is StreamGeometry&&bubbleClip.FillContains(new Point(1,1))&&bubbleClip.FillContains(new Point(live.Width-1,live.Height-1));
        var matteHandle=DialogBackdrop.ShapedHandle(live);
        checks["answer_has_live_shaped_matte"]=DialogBackdrop.IsMatteApplied(live)&&matteHandle!=IntPtr.Zero&&ShapedBackdrop.IsWindowVisible(matteHandle);
        checks["matte_layer_does_not_activate_or_take_clicks"]=(GetWindowLong(matteHandle,-20)&0x08000020)==0x08000020;
        checks["answer_fills_square_corners"]=PixelAlpha(live,0,0)>0&&PixelAlpha(live,319,319)>0;
        var hitRegion=CreateRectRgn(0,0,0,0);var matteDpi=VisualTreeHelper.GetDpi(live);
        try{checks["matte_hit_region_matches_square"]=GetWindowRgn(matteHandle,hitRegion)!=0&&PtInRegion(hitRegion,2,2)&&PtInRegion(hitRegion,(int)((live.Width-2)*matteDpi.DpiScaleX),(int)((live.Height-2)*matteDpi.DpiScaleY))&&!PtInRegion(hitRegion,(int)((live.Width+3)*matteDpi.DpiScaleX),5);}
        finally{DeleteObject(hitRegion);}
        live.Hide();checks["hiding_answer_hides_matte_layer"]=!ShapedBackdrop.IsWindowVisible(matteHandle);live.Show();
        live.Opacity=0;checks["transparent_answer_hides_matte_layer"]=!ShapedBackdrop.IsWindowVisible(matteHandle);live.Opacity=1;
        pet.SetMatteDialogs(false);
        checks["answer_matte_toggle_removes_native_layer"]=!DialogBackdrop.IsMatteApplied(live)&&!ShapedBackdrop.IsWindow(matteHandle);
        pet.SetMatteDialogs(true);matteHandle=DialogBackdrop.ShapedHandle(live);
        checks["answer_matte_toggle_restores_native_layer"]=DialogBackdrop.IsMatteApplied(live)&&ShapedBackdrop.IsWindowVisible(matteHandle);
        pet.SetDialogTransparency(90);
        checks["answer_transparency_keeps_matte_and_crisp_text"]=DialogBackdrop.ShapedHandle(live)==matteHandle&&Math.Abs(PixelAlpha(live,80,100)-26)<=1&&reply.Opacity==1;
        pet.SetDialogTransparency(60);
        checks["answer_has_no_separate_trail_window"]=live.OwnedWindows.Count==0;
        live.FollowAnchor=false;live.Left=pet.Left+pet.Width+12;live.Top=pet.Top;live.UpdateLayout();UiVerification.Advance(40);
        checks["answer_stays_square_beside_pet"]=live.Surface.SquareFrame&&live.ActualHeight==live.ActualWidth;
        VisualPreview.Save(live,Path.Combine(directory,"answer-square-side.png"));
        var sideReply=reply.TransformToAncestor((Visual)live.Content).TransformBounds(new Rect(reply.RenderSize));
        checks["side_answer_keeps_text_inside_shape"]=new[]{sideReply.TopLeft,sideReply.TopRight,sideReply.BottomLeft,sideReply.BottomRight}.All(p=>((FrameworkElement)live.Content).Clip.FillContains(p));
        live.FollowAnchor=true;live.Place();live.UpdateLayout();UiVerification.Advance(40);
        answerClient.Reply=string.Join('\n',Enumerable.Repeat("Длинный ответ должен помещаться внутри окна и прокручиваться.",40));
        answerSession.SendAsync("Длинный пример",new(){Model="preview",UseApiKey=false},"").GetAwaiter().GetResult();live.ShowAnswer();reply=Children<TextBox>(live.Body).Single();
        live.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        VisualPreview.Save(live,Path.Combine(directory,"answer-shaped-long.png"));
        checks["long_answer_scrolls_without_distorting_shape"]=Math.Abs(live.ActualHeight-shortAnswerHeight)<1&&reply.ExtentHeight>reply.ViewportHeight;
        var shape=((FrameworkElement)live.Content).Clip;
        var replyBounds=reply.TransformToAncestor((Visual)live.Content).TransformBounds(new Rect(reply.RenderSize));
        checks["answer_fits_text_inside_square"]=shape is StreamGeometry&&new[]{replyBounds.TopLeft,replyBounds.TopRight,replyBounds.BottomLeft,replyBounds.BottomRight}.All(p=>shape.FillContains(p));
        live.ShowInput("Форма вопроса восстановлена");live.UpdateLayout();System.Windows.Threading.Dispatcher.CurrentDispatcher.Invoke(()=>{},System.Windows.Threading.DispatcherPriority.ApplicationIdle);
        checks["returning_to_composer_restores_single_line"]=live.ActualHeight<=56&&live.Width==400&&live.Surface.SquareFrame&&((FrameworkElement)live.Content).Clip is StreamGeometry;
        checks["returning_to_composer_keeps_matte"]=DialogBackdrop.ShapedHandle(live)==matteHandle&&DialogBackdrop.IsMatteApplied(live);
        live.ShowAnswer();live.UpdateLayout();matteHandle=DialogBackdrop.ShapedHandle(live);
        live.Close();
        checks["closing_answer_disposes_native_matte"]=matteHandle!=IntPtr.Zero&&!ShapedBackdrop.IsWindow(matteHandle);

        var menu=new GlassWindow(pet,"Размер и движение",320);SettingsAppearance.Apply(menu);menu.Body.Children.Add(pet.CreateSizeControl(menu));
        VisualPreview.Save(menu,Path.Combine(directory,"size-slider.png"));var slider=Children<Slider>(menu.Body).Single();
        var area=ScreenPlacement.WorkArea(pet);pet.Left=area.Left+300;pet.Top=area.Top+100;pet.ResizePet(360);
        double bottom=pet.Top+pet.Height,center=pet.Left+pet.Width/2;
        slider.Value=240;checks["slider_resizes_immediately"]=Math.Abs(pet.Height-240)<.1;
        checks["resize_preserves_feet_and_center"]=Math.Abs(pet.Top+pet.Height-bottom)<.1&&Math.Abs(pet.Left+pet.Width/2-center)<.1;
        slider.Value=360;checks["slider_can_resize_back"]=Math.Abs(pet.Height-360)<.1;
        slider.RaiseEvent(new DragStartedEventArgs(0,0){RoutedEvent=Thumb.DragStartedEvent});checks["slider_drag_keeps_panel_still"]=!menu.FollowAnchor;
        slider.RaiseEvent(new DragCompletedEventArgs(0,0,false){RoutedEvent=Thumb.DragCompletedEvent});checks["slider_release_restores_anchoring"]=menu.FollowAnchor;
        checks["glass_tint_is_translucent"]=((GradientBrush)menu.Surface.Background).GradientStops.All(s=>s.Color.A<=120);
        menu.Body.Children.Add(pet.CreateTransparencyControl(menu));VisualPreview.Save(menu,Path.Combine(directory,"appearance-sliders.png"));
        var transparency=Children<Slider>(menu.Body).Single(s=>System.Windows.Automation.AutomationProperties.GetName(s)=="Прозрачность окон");
        menu.Opacity=0;menu.Show();menu.UpdateLayout();
        pet.OpenChat(true);live=pet.OwnedWindows.OfType<ChatWindow>().Single();live.ShowInput("Проверка прозрачности");live.UpdateLayout();UiVerification.Advance(420);
        double Alpha(GlassWindow w)=>((GradientBrush)w.Surface.Background).GradientStops.Average(s=>s.Color.A);
        double initial=Alpha(live);transparency.Value=0;
        checks["native_windows_support_per_pixel_alpha"]=menu.AllowsTransparency&&live.AllowsTransparency&&HasNativeAlpha(menu)&&HasNativeAlpha(live);
        checks["no_acrylic_layer_blocks_transparency"]=FrostedBackdrop.ReadMode(live)!=3&&FrostedBackdrop.ReadMode(menu)!=3&&live.Background is SolidColorBrush background&&background.Color.A==0;
        checks["zero_transparency_makes_background_opaque"]=Alpha(menu)==255&&Alpha(live)==255;
        checks["opaque_background_renders_opaque_pixels"]=PixelAlpha(live,30,(int)live.ActualHeight-16)==255;
        transparency.Value=90;
        checks["transparency_slider_updates_open_windows"]=pet.DialogTransparency==90&&Alpha(menu)<50&&Alpha(live)<initial;
        checks["transparency_keeps_text_and_buttons_opaque"]=live.Opacity==1&&live.Surface.Opacity==1&&Children<TextBox>(live.Body).Single().Opacity==1&&Children<Button>(live.Body).Single().Opacity==1;
        checks["ninety_percent_reaches_window_alpha_pixels"]=Math.Abs(PixelAlpha(live,30,(int)live.ActualHeight-16)-26)<=1;
        checks["all_four_square_corners_are_drawn"]=new[]{new Point(0,0),new Point((int)live.ActualWidth-1,0),new Point(0,(int)live.ActualHeight-1),new Point((int)live.ActualWidth-1,(int)live.ActualHeight-1)}.All(p=>PixelAlpha(live,(int)p.X,(int)p.Y)>0);
        var matteToggle=Children<CheckBox>(menu.Body).Single();
        checks["matte_blurs_open_dialogs_by_default"]=pet.MatteDialogs&&matteToggle.IsChecked==true&&DialogBackdrop.IsMatteApplied(menu)&&DialogBackdrop.IsMatteApplied(live);
        checks["matte_region_preserves_antialiased_corners"]=NativeRegionPreservesCorners(live)&&NativeRegionPreservesCorners(menu);
        matteToggle.IsChecked=false;
        checks["matte_toggle_clears_all_open_dialogs"]=!pet.MatteDialogs&&!DialogBackdrop.IsMatteApplied(menu)&&!DialogBackdrop.IsMatteApplied(live);
        checks["matte_toggle_keeps_transparency"]=pet.DialogTransparency==90&&Math.Abs(PixelAlpha(live,30,(int)live.ActualHeight-16)-26)<=1;
        matteToggle.IsChecked=true;
        checks["matte_toggle_reapplies_to_open_dialogs"]=DialogBackdrop.IsMatteApplied(menu)&&DialogBackdrop.IsMatteApplied(live);
        VisualPreview.Save(live,Path.Combine(directory,"question-transparent.png"));live.Close();
        pet.OpenChat(true);live=pet.OwnedWindows.OfType<ChatWindow>().Single();live.UpdateLayout();
        checks["new_dialog_inherits_transparency"]=Alpha(live)<initial;
        checks["new_dialog_inherits_matte"]=DialogBackdrop.IsMatteApplied(live);
        live.Close();transparency.Value=60;
        string appearancePath=Path.Combine(directory,"appearance-settings.json");File.WriteAllText(appearancePath,JsonSerializer.Serialize(new PetWindow.Settings {DialogTransparency=85}));
        checks["transparency_round_trips_in_saved_settings"]=JsonSerializer.Deserialize<PetWindow.Settings>(File.ReadAllText(appearancePath))?.DialogTransparency==85;
        checks["older_settings_keep_original_glass"]=JsonSerializer.Deserialize<PetWindow.Settings>("{\"Height\":360}")?.DialogTransparency==60;
        checks["older_settings_enable_requested_matte"]=JsonSerializer.Deserialize<PetWindow.Settings>("{\"Height\":360}")?.MatteDialogs==true;
        checks["clear_background_preference_round_trips"]=JsonSerializer.Deserialize<PetWindow.Settings>(JsonSerializer.Serialize(new PetWindow.Settings {MatteDialogs=false}))?.MatteDialogs==false;
        checks["invalid_transparency_is_bounded"]=PetWindow.NormalizeDialogTransparency(-5)==0&&PetWindow.NormalizeDialogTransparency(150)==90&&PetWindow.NormalizeDialogTransparency(double.NaN)==60;
        menu.Close();
        pet.OpenMenu();var mainMenu=pet.OwnedWindows.OfType<GlassWindow>().Single(w=>w.IsVisible);mainMenu.UpdateLayout();
        checks["pause_removed_from_menu"]=!Children<Button>(mainMenu.Body).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b) is "Пауза" or "Продолжить");mainMenu.Close();
        File.WriteAllText(Path.Combine(directory,"interaction-controls.json"),JsonSerializer.Serialize(new{success=checks.Values.All(v=>v),singleLineHeight=single,wrappedHeight=wrapped,cappedHeight=capped,checks},new JsonSerializerOptions{WriteIndented=true}));
        if(checks.Values.Any(v=>!v))throw new InvalidOperationException("Interaction UI verification failed; see interaction-controls.json");
    }
}
