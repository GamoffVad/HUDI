using System.IO;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Hudi.Distribution;

namespace HudiPet;

internal static class FavoriteSitesVerification
{
    [DllImport("user32.dll")]private static extern IntPtr GetWindow(IntPtr window,uint command);
    private static IEnumerable<T> Children<T>(DependencyObject root) where T:DependencyObject
    {
        for(int i=0;i<VisualTreeHelper.GetChildrenCount(root);i++){var child=VisualTreeHelper.GetChild(root,i);if(child is T found)yield return found;foreach(var nested in Children<T>(child))yield return nested;}
    }
    internal static int Preview(string directory)
    {
        var app=new Application{ShutdownMode=ShutdownMode.OnMainWindowClose};Ui.Initialize();
        var pet=new PetWindow(false,true,favoriteSites:new FavoriteSitesStore(directory));app.MainWindow=pet;
        pet.Loaded+=(_,_)=>{var area=ScreenPlacement.WorkArea(pet);pet.Left=area.Left+area.Width*.55;pet.Top=area.Top+area.Height*.5;pet.ToggleFavoriteSites();};return app.Run(pet);
    }
    internal static int Run(string directory)
    {
        directory=Path.GetFullPath(directory);
        Directory.CreateDirectory(directory);var checks=new Dictionary<string,bool>();var observations=new List<object>();
        var app=new Application{ShutdownMode=ShutdownMode.OnExplicitShutdown};Ui.Initialize();
        string fixture=Path.Combine(directory,"fixtures",Guid.NewGuid().ToString("N"));Directory.CreateDirectory(fixture);
        string iconPath=Path.Combine(fixture,"icon.png");
        using(var source=Application.GetResourceStream(new Uri("pack://application:,,,/HUDI;component/Assets/thinking-dot.png")).Stream)
        using(var file=File.Create(iconPath))source.CopyTo(file);
        string icon=FavoriteSitesStore.ImportIcon(iconPath);File.Delete(iconPath);
        checks["icon_survives_deleted_source"]=FavoriteSitesStore.DecodeIcon(icon).PixelWidth==128;
        FavoriteProgramsVerification.Run(fixture,icon,checks);
        WaterOrbFrames.Ready.GetAwaiter().GetResult();
        VerifyTextContent(directory,fixture,icon,checks);
        LivingSpheresVerification.Run(directory,icon,checks);
        SitesArcVerification.Run(directory,checks);
        checks["bare_domain_gets_https"]=FavoriteSitesStore.NormalizeUrl("example.com")=="https://example.com/";
        foreach(string input in new[]{"file:///C:/Windows/notepad.exe","javascript://alert/test","https://name:secret@example.com"})
        {
            bool blocked=false;try{FavoriteSitesStore.NormalizeUrl(input);}catch(InvalidDataException){blocked=true;}checks["reject_"+input.Split(':')[0]+(input.Contains("secret")?"_credentials":"")]=blocked;
        }
        var store=new FavoriteSitesStore(fixture);var first=new FavoriteSite(Guid.NewGuid().ToString("N"),"Первый","https://example.com/",icon);store.Put(first);
        var second=new FavoriteSite(Guid.NewGuid().ToString("N"),"Второй","https://example.org/",icon);store.Put(second);
        checks["sites_and_icons_round_trip"]=new FavoriteSitesStore(fixture).Read().SequenceEqual(new[]{first,second});
        checks["existing_lists_keep_automatic_size"]=store.MaximumSphereSize is null;
        store.SetMaximumSphereSize(96);var reloaded=new FavoriteSitesStore(fixture);var unchanged=reloaded.Read();
        checks["maximum_size_survives_restart_without_changing_sites"]=reloaded.MaximumSphereSize==96&&unchanged.SequenceEqual(new[]{first,second});
        checks["invalid_size_is_bounded"]=FavoriteSitesStore.NormalizeSphereSize(0)==24&&FavoriteSitesStore.NormalizeSphereSize(500)==128&&FavoriteSitesStore.NormalizeSphereSize(double.NaN)==54;
        store.Put(first with {Url="https://example.com/updated"});checks["editing_preserves_order"]=store.Read()[0].Url.EndsWith("/updated")&&store.Read()[1].Id==second.Id;
        store.Delete(second.Id);checks["deletion_persists"]=new FavoriteSitesStore(fixture).Read().Length==1;
        checks["editing_and_deleting_keep_maximum_size"]=store.MaximumSphereSize==96;
        string storedFile=Path.Combine(fixture,"favorite-sites.json");File.WriteAllText(storedFile,"broken");
        checks["corrupt_primary_recovers_backup"]=store.Read().Length==2&&store.RecoveredBackup&&store.MaximumSphereSize==96;
        store.Put(first);checks["recovered_list_can_be_saved"]=new FavoriteSitesStore(fixture).Read().Length==2;
        for(int i=2;i<20;i++)store.Put(new(Guid.NewGuid().ToString("N"),"Сайт "+(i+1),"https://example.com/"+i,icon));
        var work=new Rect(-1920,0,1920,1080);var normal=new Rect(-500,650,276,360);
        var five=SitesArc.Arrange(normal,work,5);var twenty=SitesArc.Arrange(normal,work,20);checks["more_sites_shrink_spheres"]=twenty.Diameter<five.Diameter;
        checks["single_sphere_uses_selected_maximum"]=SitesArc.Arrange(normal,work,1,96).Diameter==96;
        checks["selected_maximum_applies_to_small_pet"]=SitesArc.Arrange(new Rect(-500,650,138,180),work,1,96).Diameter==96;
        checks["crowded_list_still_shrinks_with_large_maximum"]=SitesArc.Arrange(normal,work,20,128).Diameter<128;
        foreach(int count in new[]{1,2,5,20,100})
        foreach(var petBounds in new[]{normal,new Rect(-1920,0,276,360),new Rect(-276,720,276,360)})
        foreach(double maximum in new[]{24d,128d})
        {
            var layout=SitesArc.Arrange(petBounds,work,count,maximum);double d=layout.Diameter;
            checks[$"bounded_{count}_{petBounds.Left}_{maximum}"]=layout.Centers.All(p=>work.Contains(new Rect(p.X-d/2,p.Y-d/2,d,d)));
            checks[$"spaced_{count}_{petBounds.Left}_{maximum}"]=layout.Centers.Zip(layout.Centers.Skip(1),(a,b)=>(a-b).Length).All(distance=>distance>d);
            double livingDiameter=d*SiteOrb.MaximumLivingScale;
            checks[$"living_spaced_and_bounded_{count}_{petBounds.Left}_{maximum}"]=layout.Centers.All(p=>work.Contains(new Rect(p.X-livingDiameter/2,p.Y-livingDiameter/2,livingDiameter,livingDiameter)))&&layout.Centers.Zip(layout.Centers.Skip(1),(a,b)=>(a-b).Length).All(distance=>distance>livingDiameter);
        }
        string controlsDirectory=Path.Combine(fixture,"controls");var controlsStore=new ClickBindingsStore(controlsDirectory);
        var pet=new PetWindow(false,true,favoriteSites:store,clickBindings:controlsStore){ShowInTaskbar=false};pet.Show();pet.Left=ScreenPlacement.WorkArea(pet).Left+440;pet.Top=ScreenPlacement.WorkArea(pet).Top+340;
        pet.QueuePetClick(1);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+60);
        checks["single_click_does_nothing"]=pet.SitesOrbit is null&&!pet.OwnedWindows.OfType<GlassWindow>().Any(w=>w.IsVisible);
        pet.QueuePetClick(1,control:true);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+60);
        checks["ctrl_single_click_does_nothing"]=pet.SitesOrbit is null&&!pet.OwnedWindows.OfType<GlassWindow>().Any(w=>w.IsVisible);
        pet.QueuePetClick(1);pet.QueuePetClick(2);checks["double_waits_for_possible_triple"]=pet.SitesOrbit is null;
        UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+60);
        // An overdue dispatcher timer may still be queued behind rendering work.
        // Wait for that original click only; never reopen a prematurely closed orbit.
        var orbitReady=Stopwatch.StartNew();
        while(pet.SitesOrbit is null&&orbitReady.ElapsedMilliseconds<500)
            UiVerification.Advance((int)Math.Clamp(500-orbitReady.ElapsedMilliseconds,1,25));
        var orbit=pet.SitesOrbit??throw new InvalidOperationException("double_opens_saved_sites: the original double-click did not leave an open orbit after the click delay and 500 ms dispatcher readiness allowance.");
        checks["double_opens_saved_sites"]=orbit.IsVisible&&orbit.Orbs.Count==20;
        void ObserveOrbit(string phase)=>observations.Add(new{phase,visible=orbit.IsVisible,dismissing=orbit.IsDismissing,pet.Left,pet.Top,side=orbit.Layout.Side.ToString()});
        ObserveOrbit("opened");orbit.Closed+=(_,_)=>ObserveOrbit("closed");
        checks["spheres_fade_during_travel"]=!SystemParameters.ClientAreaAnimation||orbit.Orbs.All(o=>o.Opacity<1);
        UiVerification.Advance(630);
        ObserveOrbit("settled");
        checks["native_orbit_is_below_character"]=GetWindow(new WindowInteropHelper(orbit).Handle,3)==new WindowInteropHelper(pet).Handle;
        checks["orbit_is_not_owned_above_character"]=orbit.Owner is null;
        checks["opening_settles"]=orbit.Orbs.All(o=>Math.Abs(o.Growth.ScaleX-1)<.001&&o.Opacity>.999);
        checks["spheres_remain_clickable"]=orbit.Orbs.All(o=>o.InputHitTest(new Point(o.Width/2,o.Height/2)) is not null);
        VisualPreview.Save(orbit,Path.Combine(directory,"twenty-sites.png"));
        var orb=orbit.Orbs[0];orb.ClickFeedback();UiVerification.Advance(55);
        checks["click_has_independent_spring"]=!SystemParameters.ClientAreaAnimation||(orb.Press.ScaleX>1&&orb.Growth.ScaleX==1);
        UiVerification.Advance(310);checks["click_returns_to_original_size"]=Math.Abs(orb.Press.ScaleX-1)<.001;
        pet.QueuePetClick(1);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+60);checks["single_leaves_open_sites_visible"]=ReferenceEquals(pet.SitesOrbit,orbit)&&!orbit.IsDismissing;
        pet.QueuePetClick(1);pet.QueuePetClick(2);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+380);checks["repeat_double_closes_orbit"]=pet.SitesOrbit is null;
        checks["closing_orbit_stops_living_clocks"]=orbit.Orbs.All(o=>!o.LivingShape.HasAnimatedProperties&&!o.LivingTilt.HasAnimatedProperties);
        checks["closing_orbit_stops_water_clock"]=orbit.Orbs.All(o=>!o.Water.IsAnimating)&&WaterOrbSurface.ActiveCount==0;
        pet.QueuePetClick(1,control:true);pet.QueuePetClick(2,control:true);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+60);
        checks["ctrl_double_opens_notes_without_sites"]=pet.SitesOrbit is null&&pet.OwnedWindows.OfType<NotesWindow>().Any(w=>w.IsVisible);
        foreach(var note in pet.OwnedWindows.OfType<NotesWindow>().ToArray())note.Close();
        pet.QueuePetClick(1);pet.QueuePetClick(2);pet.QueuePetClick(3);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+60);
        checks["triple_opens_chat_without_sites"]=pet.SitesOrbit is null&&pet.OwnedWindows.OfType<ChatWindow>().Any(w=>w.IsVisible)&&!pet.OwnedWindows.OfType<NotesWindow>().Any(w=>w.IsVisible);
        foreach(var chat in pet.OwnedWindows.OfType<ChatWindow>().ToArray())chat.Close();
        VerifyClickSettings(pet,controlsDirectory,directory,checks);
        pet.OpenMenu();var menu=pet.OwnedWindows.OfType<GlassWindow>().Single(w=>w.IsVisible);
        checks["menu_has_sites_settings"]=Children<Button>(menu).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Сайты и программы…");
        pet.OpenFavoriteSitesSettings();var settings=pet.OwnedWindows.OfType<FavoriteSitesWindow>().Single();
        VisualPreview.Save(settings,Path.Combine(directory,"sites-settings.png"));checks["settings_contains_url_and_icon_picker"]=Children<TextBox>(settings).Any(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Адрес сайта")&&Children<Button>(settings).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Выбрать значок");
        var textInput=Children<TextBox>(settings).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Текст на сфере");
        void ClickButton(string name)
        {
            // Refresh replaces list rows; realize their visual children before lookup.
            settings.UpdateLayout();
            var matches=Children<Button>(settings).Where(b=>System.Windows.Automation.AutomationProperties.GetName(b)==name).ToArray();
            if(matches.Length!=1)throw new InvalidOperationException($"Expected one settings button '{name}', found {matches.Length}.");
            matches[0].RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
        }
        ClickButton("Изменить Первый");textInput.Text="Почта";settings.UpdateLayout();
        checks["text_preview_updates_while_typing"]=Children<Viewbox>(settings).Any(v=>v.Child is TextBlock t&&t.Text=="Почта");
        VisualPreview.Save(settings,Path.Combine(directory,"sites-settings-text.png"));ClickButton("Добавить");
        var textSaved=new FavoriteSitesStore(fixture).Read().Single(s=>s.Id==first.Id);
        checks["editing_saves_text_and_keeps_icon"]=textSaved.Text=="Почта"&&textSaved.Icon==icon;
        ClickButton("Изменить example.com");checks["editing_loads_saved_text"]=textInput.Text=="Почта";
        textInput.Text="";ClickButton("Добавить");textSaved=store.Read().Single(s=>s.Id==first.Id);
        checks["clearing_text_restores_existing_icon"]=textSaved.Text is null&&textSaved.Icon==icon;
        // Exercise the same completed file-picker path used by the editor,
        // including an existing text label and replacement of an existing image.
        var replacementPixels=new byte[]{32,80,240,255};
        var replacementImage=BitmapSource.Create(1,1,96,96,PixelFormats.Bgra32,null,replacementPixels,4);
        var replacementEncoder=new PngBitmapEncoder();replacementEncoder.Frames.Add(BitmapFrame.Create(replacementImage));
        string replacementPath=Path.Combine(fixture,"replacement.png");using(var output=File.Create(replacementPath))replacementEncoder.Save(output);
        string replacementIcon=FavoriteSitesStore.ImportIcon(replacementPath);
        ClickButton("Изменить example.com");textInput.Text="Старый текст";
        settings.ApplyIconFile(replacementPath);settings.UpdateLayout();
        checks["editing_icon_selection_replaces_text_in_preview"]=textInput.Text.Length==0&&Children<Image>(settings).Any();
        ClickButton("Добавить");var iconSaved=new FavoriteSitesStore(fixture).Read().Single(s=>s.Id==first.Id);
        checks["editing_replaces_icon_without_duplicate"]=iconSaved.Icon==replacementIcon&&iconSaved.Text is null&&store.Read().Count(s=>s.Id==first.Id)==1;
        ClickButton("Изменить example.com");settings.ApplyLibraryIcon("mail");ClickButton("Добавить");
        var librarySaved=store.Read().Single(s=>s.Id==first.Id);
        checks["editing_can_choose_pet_library_icon_without_duplicate"]=librarySaved.LibraryIcon=="mail"&&librarySaved.Icon==pet.Appearance.Icons.Single(i=>i.Id=="mail").Image&&store.Read().Count(s=>s.Id==first.Id)==1;
        settings.OpenIconLibrary();var picker=pet.OwnedWindows.OfType<GlassWindow>().Single(w=>w.Title.StartsWith("Значки ·"));picker.UpdateLayout();
        checks["library_picker_has_ten_icons"]=Children<Button>(picker).Count(b=>pet.Appearance.Icons.Any(i=>i.Name==System.Windows.Automation.AutomationProperties.GetName(b)))==10;
        VisualPreview.Save(picker,Path.Combine(directory,"pet-icon-picker.png"));picker.Close();
        ClickButton("Изменить example.com");textInput.Text="Проверка";
        try{settings.ApplyIconFile(Path.Combine(fixture,"missing.png"));}catch(IOException){}
        checks["failed_icon_import_keeps_editor_text"]=textInput.Text=="Проверка";
        settings.ApplyIconFile(replacementPath);ClickButton("Добавить");
        Children<TextBox>(settings).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Адрес сайта").Text="https://text.example";textInput.Text="ИИ";ClickButton("Добавить");
        var textOnly=store.Read().Single(s=>s.Url=="https://text.example/");checks["settings_saves_text_without_image"]=textOnly.Text=="ИИ"&&textOnly.Icon.Length==0;
        var kind=Children<ComboBox>(settings).Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)=="Тип избранного");
        kind.SelectedIndex=1;settings.UpdateLayout();
        var programPath=Children<TextBox>(settings).Single(t=>System.Windows.Automation.AutomationProperties.GetName(t)=="Путь к программе или ярлыку");
        checks["program_settings_switch_path_label_and_picker"]=Children<Button>(settings).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Выбрать программу").Visibility==Visibility.Visible&&settings.Title=="Сайты и программы";
        string fakeProgram=Path.GetFullPath(Path.Combine(fixture,"Тестовая программа.exe"));File.WriteAllBytes(fakeProgram,[]);
        programPath.Text=fakeProgram;textInput.Text="APP";ClickButton("Добавить");
        var savedProgram=store.Read().Single(s=>s.Url==fakeProgram);
        checks["program_settings_saves_selected_kind_and_text"]=savedProgram.Kind=="program"&&savedProgram.Text=="APP"&&savedProgram.Name=="Тестовая программа";
        ClickButton("Изменить Тестовая программа");settings.UpdateLayout();
        checks["program_settings_edit_restores_type_path_and_text"]=kind.SelectedIndex==1&&programPath.Text==fakeProgram&&textInput.Text=="APP";
        settings.ApplyIconFile(replacementPath);ClickButton("Добавить");
        savedProgram=new FavoriteSitesStore(fixture).Read().Single(s=>s.Id==savedProgram.Id);
        checks["editing_program_icon_replaces_text_and_preserves_target"]=savedProgram.Icon==replacementIcon&&savedProgram.Text is null&&savedProgram.Kind=="program"&&savedProgram.Url==fakeProgram;
        ClickButton("Изменить Тестовая программа");settings.UpdateLayout();
        VisualPreview.Save(settings,Path.Combine(directory,"program-settings.png"));
        var settingsBounds=new Rect(0,0,settings.ActualWidth,settings.ActualHeight);
        checks["program_settings_fields_and_actions_fit_window"]=new FrameworkElement[]{kind,programPath,textInput,Children<Button>(settings).Single(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Добавить")}.All(control=>settingsBounds.Contains(control.TransformToAncestor((Visual)settings.Content).TransformBounds(new Rect(control.RenderSize))))&&settings.ActualHeight<=ScreenPlacement.WorkArea(settings).Height;
        programPath.Text=Path.Combine(fixture,"missing-program.exe");ClickButton("Добавить");
        checks["program_settings_bad_path_keeps_saved_record"]=store.Read().Single(s=>s.Id==savedProgram.Id)==savedProgram&&Children<TextBlock>(settings).Any(t=>t.IsVisible&&t.Text=="Программа не найдена. Проверьте путь.");
        ClickButton("Удалить Тестовая программа");
        checks["program_settings_reset_restores_site_fields"]=kind.SelectedIndex==0&&System.Windows.Automation.AutomationProperties.GetName(programPath)=="Адрес сайта";
        var sizeSlider=Children<Slider>(settings).Single(s=>System.Windows.Automation.AutomationProperties.GetName(s)=="Максимальный размер сферы");
        checks["slider_loads_saved_maximum"]=sizeSlider.Value==96;
        sizeSlider.Value=80;UiVerification.Advance(360);var resizedStore=new FavoriteSitesStore(fixture);resizedStore.Read();checks["slider_autosaves"]=resizedStore.MaximumSphereSize==80;
        sizeSlider.Value=112;settings.Close();resizedStore.Read();checks["closing_flushes_pending_size"]=resizedStore.MaximumSphereSize==112;
        pet.OpenFavoriteSitesSettings();settings=pet.OwnedWindows.OfType<FavoriteSitesWindow>().Single();settings.UpdateLayout();checks["reopening_settings_keeps_size"]=Children<Slider>(settings).Single().Value==112;settings.Close();
        pet.ToggleFavoriteSites();checks["spheres_begin_fully_transparent"]=!SystemParameters.ClientAreaAnimation||pet.SitesOrbit!.Orbs.All(o=>o.Opacity<.01);UiVerification.Advance(50);
        var textOrb=pet.SitesOrbit!.Orbs.Last();checks["saved_text_renders_in_orbit_instead_of_icon"]=Children<TextBlock>(textOrb).Any(t=>t.Text=="ИИ")&&!Children<Image>(textOrb).Any();
        var following=pet.SitesOrbit!;var originBefore=following.Layout.Origin;
        pet.Top+=10;
        checks["moving_pet_keeps_same_orbit"]=ReferenceEquals(pet.SitesOrbit,following)&&following.IsVisible&&!following.IsDismissing;
        checks["orbit_follows_pet_center"]=Math.Abs(following.Layout.Origin.Y-originBefore.Y-10)<.01;
        pet.Activate();var grab=new Point(pet.ActualWidth/2,pet.ActualHeight/2);
        pet.BeginPointerGesture(grab,1,false);
        checks["drag_capture_started"]=pet.HasPointerGesture;
        pet.MovePointerGesture(grab+new Vector(20,12),true);
        following.HandlePointerPress(new Point(-100000,-100000));
        checks["drag_does_not_close_orbit_even_when_pointer_leaves_body"]=ReferenceEquals(pet.SitesOrbit,following)&&!following.IsDismissing;
        pet.EndPointerGesture();UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+80);
        checks["release_after_drag_keeps_orbit_without_click_action"]=ReferenceEquals(pet.SitesOrbit,following)&&!following.IsDismissing;
        var edgeWork=ScreenPlacement.WorkArea(pet);pet.Left=edgeWork.Right-pet.Width;pet.Top=edgeWork.Top+(edgeWork.Height-pet.Height)/2;
        UiVerification.Advance(620);var leftArc=pet.SitesOrbit!.Layout;
        double livingRadius=leftArc.Diameter*SiteOrb.MaximumLivingScale/2;
        checks["live_right_edge_opens_arc_on_left"]=leftArc.Side==SitesArcSide.Left&&leftArc.Centers.All(p=>p.X<=leftArc.Origin.X+.001&&edgeWork.Contains(new Rect(p.X-livingRadius,p.Y-livingRadius,livingRadius*2,livingRadius*2))&&!pet.DisplayedBounds.IntersectsWith(new Rect(p.X-livingRadius,p.Y-livingRadius,livingRadius*2,livingRadius*2)));
        checks["dragging_to_edge_keeps_same_orbit"]=ReferenceEquals(pet.SitesOrbit,following);
        pet.ResizePet(300);UiVerification.Advance(100);
        checks["resize_keeps_orbit_visible"]=ReferenceEquals(pet.SitesOrbit,following)&&following.Orbs.All(o=>o.Opacity>.99);
        var bodyPoint=(from x in Enumerable.Range(1,19) from y in Enumerable.Range(1,19)
            let point=pet.PointToScreen(new Point(pet.ActualWidth*x/20,pet.ActualHeight*y/20))
            where pet.ContainsPetScreenPoint(point) select point).First();
        following.HandlePointerPress(bodyPoint);checks["click_on_body_keeps_orbit"]=!following.IsDismissing;
        var target=following.Orbs[0];following.HandlePointerPress(target.PointToScreen(new Point(target.Width/2,target.Height/2)));
        checks["click_on_sphere_is_not_an_outside_click"]=!following.IsDismissing;
        following.HandlePointerPress(new Point(-100000,-100000));
        checks["outside_click_dismisses_orbit"]=following.IsDismissing;
        UiVerification.Advance(400);checks["outside_click_closes_orbit_and_stops_water"]=pet.SitesOrbit is null&&WaterOrbSurface.ActiveCount==0;
        pet.ToggleFavoriteSites();var last=pet.SitesOrbit;pet.Close();checks["closing_pet_closes_independent_orbit"]=last is not null&&!last.IsVisible;
        File.WriteAllText(Path.Combine(directory,"favorite-sites.json"),JsonSerializer.Serialize(new{success=checks.Values.All(v=>v),checks,observations},new JsonSerializerOptions{WriteIndented=true}));
        return checks.Values.All(v=>v)?0:1;
    }
    private static void VerifyClickSettings(PetWindow pet,string controlsDirectory,string directory,Dictionary<string,bool> checks)
    {
        var defaults=new ClickBindings();checks["new_click_settings_use_requested_defaults"]=pet.ClickBindings==defaults;
        pet.SetClickBindings(defaults);pet.OpenMenu();var menu=pet.OwnedWindows.OfType<GlassWindow>().Single(w=>w.IsVisible);
        checks["menu_has_click_settings"]=Children<Button>(menu).Any(b=>System.Windows.Automation.AutomationProperties.GetName(b)=="Управление");
        pet.OpenClickSettings();var settings=pet.OwnedWindows.OfType<ClickSettingsWindow>().Single();settings.UpdateLayout();
        ComboBox Select(string name)=>Children<ComboBox>(settings).Single(c=>System.Windows.Automation.AutomationProperties.GetName(c)==name);
        checks["click_settings_expose_all_six_gestures"]=Children<ComboBox>(settings).Count()==6;
        VisualPreview.Save(settings,Path.Combine(directory,"click-settings.png"));
        using(var locked=new FileStream(Path.Combine(controlsDirectory,"mouse-controls.json"),FileMode.Open,FileAccess.ReadWrite,FileShare.None))
        {
            Select("Один клик").SelectedIndex=(int)PetClickAction.FavoriteSites;
            checks["failed_binding_save_keeps_previous_choice"]=pet.ClickBindings==defaults&&Select("Один клик").SelectedIndex==(int)PetClickAction.None&&Children<TextBlock>(settings).Any(t=>t.Text.StartsWith("Не удалось сохранить назначение"));
        }
        Select("Один клик").SelectedIndex=(int)PetClickAction.FavoriteSites;Select("Двойной клик").SelectedIndex=(int)PetClickAction.None;Select("Тройной клик").SelectedIndex=(int)PetClickAction.None;
        settings.Close();checks["click_settings_survive_restart"]=new ClickBindingsStore(controlsDirectory).Read()==pet.ClickBindings;
        pet.QueuePetClick(1);checks["unambiguous_single_action_has_no_timer_delay"]=pet.SitesOrbit is not null;pet.CloseFavoriteSites();
        pet.OpenClickSettings();settings=pet.OwnedWindows.OfType<ClickSettingsWindow>().Single();settings.UpdateLayout();
        checks["reopened_click_settings_show_saved_choices"]=Select("Один клик").SelectedIndex==(int)PetClickAction.FavoriteSites&&Select("Тройной клик").SelectedIndex==(int)PetClickAction.None;
        Select("Ctrl + Один клик").SelectedIndex=(int)PetClickAction.Notes;Select("Ctrl + Двойной клик").SelectedIndex=(int)PetClickAction.None;Select("Ctrl + Тройной клик").SelectedIndex=(int)PetClickAction.Chat;settings.Close();
        pet.QueuePetClick(1,control:true);checks["configured_ctrl_single_waits_for_later_action"]=!pet.OwnedWindows.OfType<NotesWindow>().Any(w=>w.IsVisible);
        pet.QueuePetClick(2,control:true);pet.QueuePetClick(3,control:true);
        checks["configured_ctrl_triple_opens_chat_without_notes"]=pet.OwnedWindows.OfType<ChatWindow>().Any(w=>w.IsVisible)&&!pet.OwnedWindows.OfType<NotesWindow>().Any(w=>w.IsVisible);
        foreach(var chat in pet.OwnedWindows.OfType<ChatWindow>().ToArray())chat.Close();
        pet.SetClickBindings(defaults with{Single=PetClickAction.None,Double=PetClickAction.Chat,Triple=PetClickAction.None});pet.QueuePetClick(2);
        checks["double_click_can_be_reassigned_to_chat"]=pet.OwnedWindows.OfType<ChatWindow>().Any(w=>w.IsVisible);
        foreach(var chat in pet.OwnedWindows.OfType<ChatWindow>().ToArray())chat.Close();
        pet.SetClickBindings(defaults with{Triple=PetClickAction.Notes});pet.QueuePetClick(1);pet.QueuePetClick(2);pet.QueuePetClick(3);
        checks["triple_click_can_be_reassigned_to_notes"]=pet.OwnedWindows.OfType<NotesWindow>().Any(w=>w.IsVisible)&&pet.SitesOrbit is null;
        foreach(var note in pet.OwnedWindows.OfType<NotesWindow>().ToArray())note.Close();
        pet.SetClickBindings(defaults with{Single=PetClickAction.FavoriteSites});pet.QueuePetClick(1);pet.SetClickBindings(defaults);UiVerification.Advance((int)pet.ClickSequenceDelay.TotalMilliseconds+50);
        checks["changing_bindings_cancels_pending_click"]=pet.SitesOrbit is null;
        var backupStore=new ClickBindingsStore(Path.Combine(controlsDirectory,"backup-test"));backupStore.Save(defaults);backupStore.Save(defaults with{Single=PetClickAction.Chat});
        File.WriteAllText(Path.Combine(controlsDirectory,"backup-test","mouse-controls.json"),"broken");checks["click_bindings_recover_backup"]=backupStore.Read()==defaults;
    }
    private static void VerifyTextContent(string directory,string fixture,string icon,Dictionary<string,bool> checks)
    {
        string legacy=Path.Combine(fixture,"legacy");Directory.CreateDirectory(legacy);
        string id=Guid.NewGuid().ToString("N");
        File.WriteAllText(Path.Combine(legacy,"favorite-sites.json"),JsonSerializer.Serialize(new{Version=1,Sites=new[]{new{Id=id,Name="Старый сайт",Url="https://example.com/",Icon=icon}}}));
        var store=new FavoriteSitesStore(legacy);var original=store.Read().Single();checks["legacy_icon_list_loads_without_text_field"]=original.Text is null&&original.Icon==icon;
        var textOnly=new FavoriteSite(Guid.NewGuid().ToString("N"),"Почта","https://mail.example/","","  Почта  ");store.Put(textOnly);
        checks["text_only_site_survives_restart"]=new FavoriteSitesStore(legacy).Read().Last() is {Text:"Почта",Icon:""};
        bool rejected=false;try{store.Put(textOnly with{Text="  "});}catch(InvalidDataException){rejected=true;}checks["blank_text_without_icon_rejected_without_data_loss"]=rejected&&store.Read().Last().Text=="Почта";
        rejected=false;try{store.Put(textOnly with{Text=new string('Я',FavoriteSitesStore.MaximumTextLength+1)});}catch(InvalidDataException){rejected=true;}checks["overlong_text_rejected_without_data_loss"]=rejected&&store.Read().Last().Text=="Почта";
        var samples=new StackPanel{Orientation=Orientation.Horizontal,Margin=new Thickness(12)};
        foreach(var sample in new[]{("",54d),("ИИ",24d),("Почта",54d),("GitHub",96d),("Я".PadRight(32,'Я'),128d)})
        {
            var face=SiteOrb.CreateFace(icon,sample.Item1,sample.Item2);face.Margin=new Thickness(8);samples.Children.Add(face);
            face.Measure(new Size(sample.Item2,sample.Item2));face.Arrange(new Rect(0,0,sample.Item2,sample.Item2));face.UpdateLayout();
            if(sample.Item1.Length==0)checks["empty_text_uses_icon"]=Children<Image>(face).Any();
            else
            {
                var text=Children<TextBlock>(face).Single();var bounds=text.TransformToAncestor(face).TransformBounds(new Rect(0,0,text.ActualWidth,text.ActualHeight));
                checks[$"text_fits_sphere_{sample.Item2}"]=new Rect(0,0,sample.Item2,sample.Item2).Contains(bounds)&&!Children<Image>(face).Any();
            }
        }
        var preview=new Window{Width=460,SizeToContent=SizeToContent.Height,Content=samples,Background=new SolidColorBrush(Color.FromRgb(26,29,32))};VisualPreview.Save(preview,Path.Combine(directory,"sphere-text-sizes.png"));preview.Close();
    }
}
