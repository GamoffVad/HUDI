using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Styling;
using Avalonia.Themes.Fluent;

namespace HudiPet;

internal sealed class MacApp : Application
{
    [STAThread]
    private static int Main(string[] args)
    {
        try { return Start(args); }
        catch (Exception error)
        {
            Console.Error.WriteLine(error);
            try
            {
                Directory.CreateDirectory(AppStorage.Directory);
                File.WriteAllText(Path.Combine(AppStorage.Directory, "startup-error.txt"), $"HUDI {AppRelease.Version}\n{error}");
            }
            catch (Exception logError) when (logError is IOException or UnauthorizedAccessException) { }
            return 1;
        }
    }

    private static int Start(string[] args)
    {
        if (args.Length == 2 && args[0] == "--verify-runtime")
        {
            ReportPath = Path.Combine(Path.GetFullPath(args[1]), "mac-runtime.json");
            AppStorage.VerificationDirectory = Path.Combine(Path.GetFullPath(args[1]), Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(AppStorage.VerificationDirectory);
        }
        return AppBuilder.Configure<MacApp>()
            .UsePlatformDetect().StartWithClassicDesktopLifetime(args, ShutdownMode.OnMainWindowClose);
    }

    private static string? ReportPath { get; set; }

    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
        RequestedThemeVariant = ThemeVariant.Dark;
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var pet = new MacPetWindow();
            desktop.MainWindow = pet;
            if (ReportPath is { } report)
            {
                pet.Opened += (_, _) => pet.VerifyRuntime(report, desktop);
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
