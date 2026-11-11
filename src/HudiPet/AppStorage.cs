using System.IO;

namespace HudiPet;

internal static class AppStorage
{
    internal static string? VerificationDirectory { get; set; }
    internal static string Directory => VerificationDirectory ?? DefaultDirectory;
    private static readonly string DefaultDirectory = OperatingSystem.IsMacOS()
        ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Library", "Application Support", "HUDI")
        : Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "HUDI");
}
