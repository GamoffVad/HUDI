using System.IO;
using System.Text.Json;
using System.Windows.Media.Imaging;

namespace HudiPet;

internal static class PetVerification
{
    internal static readonly (PetAction action, string label)[] ActionLabels =
    [
        (PetAction.Think, "Задуматься"), (PetAction.Crouch, "Присесть"),
        (PetAction.Stretch, "Потянуться"), (PetAction.Angry, "Разозлиться"),
        (PetAction.Wink, "Подмигнуть"), (PetAction.Sad, "Загрустить"),
        (PetAction.Surprise, "Удивиться"), (PetAction.LookAround, "Осмотреться"),
        (PetAction.Dance, "Потанцевать"), (PetAction.Hood, "Поправить капюшон"),
        (PetAction.Pockets, "Руки в карманы"), (PetAction.Combat, "Боевая стойка"),
        (PetAction.Salute, "Воинское приветствие"), (PetAction.March, "Строевой шаг"),
        (PetAction.Notebook, "Записать в блокнот"), (PetAction.Fire, "Огонь · первая сцена"),
        (PetAction.FireCalm, "Огонь · спокойное начало"),
        (PetAction.Dash, "Чернильный рывок"), (PetAction.Jump, "Прыжок"),
        (PetAction.PaintMagic, "Стрит-арт магия"), (PetAction.ShadowMerge, "Слияние с тенью"),
        (PetAction.SideStep, "Шаг в сторону"), (PetAction.CutThroat, "Жест по горлу"),
        (PetAction.LookDown, "Смотреть вниз"), (PetAction.LookLeft, "Смотреть влево"),
        (PetAction.LookRight, "Смотреть вправо"), (PetAction.LookUp, "Смотреть вверх"),
        (PetAction.Temple, "Крутить у виска"), (PetAction.Skeptical, "Скрестить руки"),
        (PetAction.Shrug, "Пожать плечами"),
        (PetAction.Indignation, "Возмутиться · покрутить у виска"),
        (PetAction.FireTrick, "Щёлкнуть · затушить огонь"),
        (PetAction.Impatient, "Нетерпеливо ждать"),
        (PetAction.Stroll, "Любопытная прогулка"),
        (PetAction.AdjustGlasses, "Поправить очки и волосы"),
        (PetAction.Groove, "Танцевать в ритме фанк"),
        (PetAction.Ironic, "Ироничная реакция"),
        (PetAction.SurpriseClassic, "Удивиться · первый вариант"),
        (PetAction.IndignantLean, "Нахмуриться и наклониться"),
        (PetAction.Giggle, "Хитро посмеяться"),
        (PetAction.Reproach, "Укоризненно посмотреть"),
        (PetAction.HeadTap, "Постучать по голове"),
        (PetAction.PeekAround, "Обернуться по сторонам"),
        (PetAction.WalkAround, "Походить в разные стороны"),
        (PetAction.QuietIdle, "Тихо ждать"), (PetAction.HairBrush, "Убрать прядь с лица"),
        (PetAction.Doze, "Клевать носом"), (PetAction.Cheer, "Тихо радоваться"),
        (PetAction.EdgeLean, "Опереться на край"), (PetAction.EdgePeek, "Выглянуть из-за края"),
        (PetAction.Gymnastics, "Сделать гимнастику"), (PetAction.Peek, "Подглядеть")
    ];
    private static byte[] Pixels(BitmapSource image)
    {
        var pixels = new byte[image.PixelWidth * image.PixelHeight * 4];
        image.CopyPixels(pixels, image.PixelWidth * 4, 0); return pixels;
    }
    internal static int Run()
    {
        string output = Path.Combine(PetWindow.SettingsDirectory, "verification-v7");
        Directory.CreateDirectory(output);
        var library = SpriteLibrary.Shared.Value;
        var failures = new List<string>();
        var randomClickChecks=RandomActionVerification.Run(library);
        foreach(var check in randomClickChecks.Where(c=>!c.Value))failures.Add("Random click: "+check.Key);
        var emotionChecks=EmotionVerification.Run(library,Path.Combine(output,"emotions"));
        foreach(var check in emotionChecks.Where(c=>!c.Value))failures.Add("New emotions: "+check.Key);
        var geometryChecks=GeometryVerification.Run();
        foreach(var check in geometryChecks.Where(c=>!c.Value))failures.Add("Window geometry: "+check.Key);
        int maxSourceDifference = 0;
        var samples = new[] { "neutral", "think-040", "squatstretch-050", "combat-038", "angry-040", "hood-040", "pockets-040" }
            .Concat(library.FrameIds.Where(id => id.StartsWith("return-") && id.EndsWith("-036")));
        foreach (string id in samples)
        {
            var visual = new PetVisual(); visual.Apply(new PetPose(id));
            var actual = Pixels(visual.Snapshot(library.Width, library.Height));
            var expected = library.Pixels(id);
            for (int p = 0; p < actual.Length; p++) maxSourceDifference = Math.Max(maxSourceDifference, Math.Abs(actual[p] - expected[p]));
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(visual.Snapshot(library.Width,library.Height)));
            using var file = File.Create(Path.Combine(output, id + ".png")); encoder.Save(file);
        }
        if (maxSourceDifference > 1) failures.Add("Renderer changed source pixels, including the face");
        if (library.FrameIds.Any(id => id.StartsWith("hover-") || id.StartsWith("bridge-") ||
            id.StartsWith("idle-") && int.Parse(id[5..]) >= 108)) failures.Add("Rejected hover, morph or mouth frames are packaged");
        int preservedEndPoses = 0, seamlessStarts = 0;
        foreach (var action in Enum.GetValues<PetAction>())
        {
            var engine = new PetBehavior(5);
            if(!engine.Supports(action))continue;
            engine.Trigger(action);
            var visited = new HashSet<string>(); bool ended = false;
            for (int i = 0; i < 4800; i++)
            {
                var pose = engine.Update(1d / 120, false); visited.Add(pose.Frame);
                if (engine.ActionsStarted > 0 && !engine.IsPlaying)
                {
                    string last = library.Clips[PetBehavior.ClipName(action)][^1].Frame;
                    ended = pose.Frame == last && pose.BlendFrom is null;
                    var mixer = new FrameMixer();
                    mixer.Compose(pose);
                    byte[] endPixels = (byte[])mixer.Pixels.Clone();
                    for (int rest = 0; rest < 10; rest++)
                    {
                        pose = engine.Update(.1, false);
                        mixer.Compose(pose);
                        ended &= pose.Frame == last && endPixels.SequenceEqual(mixer.Pixels);
                    }
                    if (ended) preservedEndPoses++;
                    engine.Trigger(PetAction.Think);
                    mixer.Compose(engine.Update(1d / 60, false));
                    if (endPixels.SequenceEqual(mixer.Pixels)) seamlessStarts++;
                    else failures.Add(action + ": next action snaps at entry");
                    break;
                }
            }
            if (!ended || library.Clips[PetBehavior.ClipName(action)].Any(step => !visited.Contains(step.Frame))) failures.Add(action + ": invalid playback");
        }
        int longPocketVariants = 0, shortPocketVariants = 0;
        double minPocketHold = double.MaxValue, maxPocketHold = 0;
        for (int seed = 0; seed < 24; seed++)
        {
            var pockets = new PetBehavior(seed); pockets.Trigger(PetAction.Pockets);
            int holdTicks = 0, previousIndex = -1; bool reachedEnd = false;
            for (int tick = 0; tick < 40 * 120; tick++)
            {
                var pose = pockets.Update(1d / 120, false);
                if (pockets.HoldingPockets)
                {
                    int index = int.Parse(pose.Frame[8..]);
                    if (index < 72 || index > 86 || previousIndex >= 0 && Math.Abs(previousIndex - index) > 1)
                        failures.Add("Pocket holding loop has a frame discontinuity");
                    previousIndex = index; holdTicks++;
                }
                if (!pockets.IsPlaying) { reachedEnd = pose.Frame == "pockets-144"; break; }
            }
            if (!reachedEnd) failures.Add("Pocket hold never exits naturally");
            if (pockets.PocketHoldDuration > 0)
            {
                longPocketVariants++;
                double seconds = holdTicks / 120d;
                minPocketHold = Math.Min(minPocketHold, seconds); maxPocketHold = Math.Max(maxPocketHold, seconds);
                if (Math.Abs(seconds - pockets.PocketHoldDuration) > .03) failures.Add("Wrong pocket hold duration");
            }
            else shortPocketVariants++;
        }
        if (longPocketVariants == 0 || shortPocketVariants == 0) failures.Add("Pocket duration is not varied");
        var automatic = new PetBehavior(17);
        double automaticBudget=PetBehavior.AutomaticActions.Where(automatic.Supports).Sum(action=>
            library.Clips[PetBehavior.ClipName(action)].Sum(step=>step.Seconds)+6+
            (library.Holds.TryGetValue(PetBehavior.ClipName(action),out var hold)?hold.Seconds*4:0))+10;
        for (int i = 0; i < Math.Ceiling(automaticBudget*60); i++) automatic.Update(1d / 60);
        if (PetBehavior.AutomaticActions.Where(automatic.Supports).Any(a => !automatic.SeenActions.Contains(a))) failures.Add("Missing automatic gesture");
        var clicked = new PetBehavior(7); clicked.Trigger(PetAction.Crouch);
        for (int i = 0; i < 120; i++) clicked.Update(1d / 60, false);
        clicked.Trigger(PetAction.Combat); clicked.Update(1d / 60, false);
        if (clicked.CurrentAction != PetAction.Combat || clicked.Interruptions != 1) failures.Add("Click reaction failed");
        bool success = failures.Count == 0;
        File.WriteAllText(Path.Combine(output, "results.json"), JsonSerializer.Serialize(new
        {
            success, failures, randomClickChecks, emotionChecks, geometryChecks, frames = library.FrameIds.Length, maxSourceDifference,
            cursorTracking = false, hoverReaction = false, eyeReconstruction = false,
            preservedEndPoses, seamlessStarts, longPocketVariants, shortPocketVariants,
            minPocketHoldSeconds = minPocketHold, maxPocketHoldSeconds = maxPocketHold,
            automaticActions = automatic.SeenActions.Select(a => a.ToString()), clickInterruptions = clicked.Interruptions
        }, new JsonSerializerOptions { WriteIndented = true }));
        return success ? 0 : 2;
    }

    internal static int ExportMotionPreview()
    {
        string output = Path.Combine(PetWindow.SettingsDirectory, "verification-v6.3", "motion-preview");
        Directory.CreateDirectory(output);
        var library = SpriteLibrary.Shared.Value;
        var engine = new PetBehavior(1);
        var visual = new PetVisual();
        var queue = new Queue<PetAction>([PetAction.Pockets, PetAction.Think, PetAction.Crouch, PetAction.Hood]);
        double restTime = 0;
        int frame = 0;
        engine.Trigger(queue.Dequeue());
        while (frame < 60 * 24)
        {
            var pose = engine.Update(1d / 24, false);
            visual.Apply(pose);
            var encoder = new PngBitmapEncoder(); encoder.Frames.Add(BitmapFrame.Create(visual.Snapshot(library.Width,library.Height)));
            using (var file = File.Create(Path.Combine(output, $"{frame++:D5}.png"))) encoder.Save(file);
            if (!engine.IsPlaying)
            {
                restTime += 1d / 24;
                if (restTime >= 1.5)
                {
                    if (queue.Count == 0) break;
                    engine.Trigger(queue.Dequeue()); restTime = 0;
                }
            }
        }
        return 0;
    }
}
