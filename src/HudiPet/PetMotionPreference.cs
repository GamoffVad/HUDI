using System.IO;
using System.Text.Json;

namespace HudiPet;

internal sealed partial class PetCatalog
{
    private string MotionPreferencePath(LocalPet pet)
    {
        if (!pet.BuiltIn && !Guid.TryParseExact(pet.Id, "N", out _))
            throw new InvalidDataException("Питомец не найден.");
        return Path.Combine(root, pet.BuiltIn ? Hudi.Id : pet.Id, "motion.json");
    }

    internal bool UserActionsOnly(LocalPet pet)
    {
        try
        {
            using var input = File.OpenRead(MotionPreferencePath(pet));
            using var document = JsonDocument.Parse(input);
            return document.RootElement.TryGetProperty("userActionsOnly", out var value)
                && value.ValueKind == JsonValueKind.True;
        }
        catch (Exception error) when (error is IOException or UnauthorizedAccessException or JsonException or InvalidOperationException)
        {
            return false;
        }
    }

    internal void SetUserActionsOnly(LocalPet pet, bool enabled)
    {
        Locked(() =>
        {
            if (!List().Any(entry => entry.Id == pet.Id))
                throw new InvalidDataException("Питомец не найден.");
            string path = MotionPreferencePath(pet);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            string temporary = path + ".pending";
            File.WriteAllText(temporary, JsonSerializer.Serialize(new { userActionsOnly = enabled }));
            File.Move(temporary, path, true);
            return true;
        });
    }
}
