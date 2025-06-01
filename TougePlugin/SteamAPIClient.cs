using Serilog;
using System.Text.Json;

namespace TougePlugin;

public class SteamAPIClient()
{
    private static readonly HttpClient _httpClient = new();
    private static readonly string avatarFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "TougePlugin", "wwwroot", "avatars");

    public static async Task GetSteamAvatarAsync(string ApiKey, string SteamId64)
    {
        string url = $"https://api.steampowered.com/ISteamUser/GetPlayerSummaries/v2/?key={ApiKey}&steamids={SteamId64}";
        try
        {
            var response = await _httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();

            string json = await response.Content.ReadAsStringAsync();
            using JsonDocument doc = JsonDocument.Parse(json);

            var root = doc.RootElement;
            var players = root.GetProperty("response").GetProperty("players");

            if (players.GetArrayLength() > 0)
            {
                var player = players[0];
                string? avatarUrl = player.GetProperty("avatarfull").GetString();

                byte[] avatarBytes = await _httpClient.GetByteArrayAsync(avatarUrl);
                string filePath = Path.Combine(avatarFolderPath, $"{SteamId64}.jpg");
                await File.WriteAllBytesAsync(filePath, avatarBytes);
            }
        }
        catch (Exception ex)
        {
            Log.Warning($"Error fetching avatar: {ex.Message}");
        }
    } 
}
