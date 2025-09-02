using System.IO;
using System.Text.Json;

namespace Client;

public static class ClientPrefs
{
    private static string Dir => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "cocodex", "Client");
    private static string FilePath => Path.Combine(Dir, "clientsettings.json");

    public static void Save(ClientSettings settings)
    {
        Directory.CreateDirectory(Dir);
        var json = JsonSerializer.Serialize(settings, new JsonSerializerOptions(JsonSerializerDefaults.Web));
        File.WriteAllText(FilePath, json);
    }

    public static ClientSettings? Load()
    {
        if (!File.Exists(FilePath)) return null;
        var json = File.ReadAllText(FilePath);
        return JsonSerializer.Deserialize<ClientSettings>(json, new JsonSerializerOptions(JsonSerializerDefaults.Web));
    }
}

