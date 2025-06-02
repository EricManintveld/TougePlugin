using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Plugin;
using AssettoServer.Shared.Services;
using Microsoft.Extensions.Hosting;
using Serilog;
using System.Reflection;
using TougePlugin.Database;
using TougePlugin.Models;
using TougePlugin.Packets;
using TougePlugin.Serialization;
using YamlDotNet.Core.Tokens;
using YamlDotNet.Serialization;

namespace TougePlugin;

public class Touge : CriticalBackgroundService, IAssettoServerAutostart
{
    private readonly EntryCarManager _entryCarManager;
    private readonly Func<EntryCar, EntryCarTougeSession> _entryCarTougeSessionFactory;
    private readonly Dictionary<int, EntryCarTougeSession> _instances = [];
    private readonly CSPServerScriptProvider _scriptProvider;
    private readonly CSPClientMessageTypeManager _cspClientMessageTypeManager;
    private readonly TougeConfiguration _configuration;
    private readonly IDbConnectionFactory _connectionFactory;
    private readonly ACServerConfiguration _serverConfig;

    private bool _loadSteamAvatars;
    private const long MaxAvatarCacheSizeBytes = 4L * 1024 * 1024; // 50 MB

    private static readonly string startingPositionsFile = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "cfg", "touge_course_setup.yml");
    private static readonly string avatarFolderPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "plugins", "TougePlugin", "wwwroot", "avatars");

    public readonly IDatabase database;
    public readonly Dictionary<string, Course> tougeCourses;

    public Touge(
        TougeConfiguration configuration,
        EntryCarManager entryCarManager,
        Func<EntryCar, EntryCarTougeSession> entryCarTougeSessionFactory,
        IHostApplicationLifetime applicationLifetime,
        CSPServerScriptProvider scriptProvider,
        ACServerConfiguration serverConfiguration,
        CSPClientMessageTypeManager cspClientMessageTypeManager
        ) : base(applicationLifetime)
    {
        _entryCarManager = entryCarManager;
        _entryCarTougeSessionFactory = entryCarTougeSessionFactory;
        _scriptProvider = scriptProvider;
        _cspClientMessageTypeManager = cspClientMessageTypeManager;
        _configuration = configuration;
        _serverConfig = serverConfiguration;

        _loadSteamAvatars = _configuration.SteamAPIKey != null;

        if (!serverConfiguration.Extra.EnableClientMessages)
        {
            throw new ConfigurationException("Touge plugin requires ClientMessages to be enabled.");
        }

        // Provide lua scripts
        ProvideScript("teleport.lua");
        ProvideScript("hud.lua");
        ProvideScript("timing.lua");

        // Set up database connection
        if (_configuration.IsDbLocalMode)
        {
            // SQLite database.
            _connectionFactory = new SqliteConnectionFactory("plugins/TougePlugin/database.db");
        }
        else
        {
            // PostgreSQL database.
            _connectionFactory = new PostgresConnectionFactory(_configuration.PostgresqlConnectionString!);
        }

        try
        {
            _connectionFactory.InitializeDatabase();
        }
        catch (Exception ex)
        {
            throw new Exception("Failed to initialize touge database: " + ex.Message);
        }

        database = new GenericDatabase(_connectionFactory);

        _cspClientMessageTypeManager.RegisterOnlineEvent<InitializationPacket>(OnInitializationPacket);
        _cspClientMessageTypeManager.RegisterOnlineEvent<InvitePacket>(OnInvitePacket);
        _cspClientMessageTypeManager.RegisterOnlineEvent<LobbyStatusPacket>(OnLobbyStatusPacketAsync);
        _cspClientMessageTypeManager.RegisterOnlineEvent<ForfeitPacket>(OnForfeitPacket);
        _cspClientMessageTypeManager.RegisterOnlineEvent<FinishPacket>(OnFinishPacket);

        tougeCourses = GetCourses();

        CheckAvatarsFolder();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        foreach (var entryCar in _entryCarManager.EntryCars)
        {
            _instances.Add(entryCar.SessionId, _entryCarTougeSessionFactory(entryCar));
        }

        _entryCarManager.ClientConnected += OnClientConnected;

        return Task.CompletedTask;
    }

    internal EntryCarTougeSession GetSession(EntryCar entryCar) => _instances[entryCar.SessionId];

    internal Race? GetActiveRace(EntryCar entryCar)
    {
        EntryCarTougeSession session = GetSession(entryCar);
        TougeSession? tougeSession = session.CurrentSession;
        if (tougeSession != null)
        {
            // Now get the active race.
            return tougeSession.ActiveRace;
        }
        return null;
    }

    private void OnClientConnected(ACTcpClient client, EventArgs args)
    {
        // Check if the player is registered in the database
        string playerId = client.Guid.ToString();
        database.CheckPlayerAsync(playerId);

        if (_configuration.SteamAPIKey != null)
        {
            LoadAvatar(client);
        }
    }

    private void ProvideScript(string scriptName)
    {
        string scriptPath = Path.Join(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), "lua", scriptName);

        using var streamReader = new StreamReader(scriptPath);
        var reconnectScript = streamReader.ReadToEnd();

        _scriptProvider.AddScript(reconnectScript, scriptName);
    }

    private async void OnInitializationPacket(ACTcpClient client, InitializationPacket packet)
    {
        string playerId = client.Guid.ToString();
        var(elo, racesCompleted) = await database.GetPlayerStatsAsync(playerId);

        client.SendPacket(new InitializationPacket { Elo = elo, RacesCompleted = racesCompleted, UseTrackFinish = _configuration.UseTrackFinish, DiscreteMode = _configuration.DiscreteMode, loadSteamAvatars = _loadSteamAvatars });
    }

    private void OnInvitePacket(ACTcpClient client, InvitePacket packet)
    {
        if (packet.InviteSenderName == "nearby")
            InviteNearbyCar(client);
        else if (packet.InviteSenderName == "a")
        {
            // Accept invite.
            var currentSession = GetSession(client.EntryCar).CurrentSession;
            if (currentSession != null && currentSession.Challenger != client.EntryCar && !currentSession.IsActive)
            {
                _ = Task.Run(currentSession.StartAsync);
            }
        }
        else
            // Invite by GUID.
            InviteCar(client, packet.InviteRecipientGuid);
    }

    private async void OnLobbyStatusPacketAsync(ACTcpClient client, LobbyStatusPacket packet)
    {
        // Find if there is a player close to the client.
        List<EntryCar>? closestCars = GetSession(client!.EntryCar).FindClosestCars(5);

        var tasks = closestCars.Select(async car =>
        {
            var (elo, _) = await database.GetPlayerStatsAsync(car.Client!.Guid!.ToString());

            return new Dictionary<string, object>
            {
                { "name", car.Client!.Name! },
                { "id", car.Client!.Guid! },
                { "inRace", IsInTougeSession(car) },
                { "elo", elo }
            };
        });

        var playerStatsList = (await Task.WhenAll(tasks)).ToList();

        int nearbyPlayersCount = playerStatsList.Count;
        if (nearbyPlayersCount < 5)
        {
            // Add dummy values to fill up to 5 entries
            for (int i = nearbyPlayersCount; i < 5; i++)
            {
                playerStatsList.Add(new Dictionary<string, object>
                {
                    { "name", "" },
                    { "id", (ulong)0 },
                    { "inRace", false },
                    { "elo", -1 }
                });
            }
        }

        // Send a packet back to the client
        // Close your eyes, this is not going to be pretty :(
        client.SendPacket(new LobbyStatusPacket
        {
            NearbyPlayerName1 = (string)playerStatsList[0]["name"],
            NearbyPlayerId1 = (ulong)playerStatsList[0]["id"],
            NearbyPlayerInRace1 = (bool)playerStatsList[0]["inRace"],
            NearbyPlayerElo1 = (int)playerStatsList[0]["elo"],
            NearbyPlayerName2 = (string)playerStatsList[1]["name"],
            NearbyPlayerId2 = (ulong)playerStatsList[1]["id"],
            NearbyPlayerInRace2 = (bool)playerStatsList[1]["inRace"],
            NearbyPlayerElo2 = (int)playerStatsList[1]["elo"],
            NearbyPlayerName3 = (string)playerStatsList[2]["name"],
            NearbyPlayerId3 = (ulong)playerStatsList[2]["id"],
            NearbyPlayerInRace3 = (bool)playerStatsList[2]["inRace"],
            NearbyPlayerElo3 = (int)playerStatsList[2]["elo"],
            NearbyPlayerName4 = (string)playerStatsList[3]["name"],
            NearbyPlayerId4 = (ulong)playerStatsList[3]["id"],
            NearbyPlayerInRace4 = (bool)playerStatsList[3]["inRace"],
            NearbyPlayerElo4 = (int)playerStatsList[3]["elo"],
            NearbyPlayerName5 = (string)playerStatsList[4]["name"],
            NearbyPlayerId5 = (ulong)playerStatsList[4]["id"],
            NearbyPlayerInRace5 = (bool)playerStatsList[4]["inRace"],
            NearbyPlayerElo5 = (int)playerStatsList[4]["elo"],
        });
    }

    private void OnForfeitPacket(ACTcpClient sender, ForfeitPacket packet)
    {
        Race? activeRace = GetActiveRace(sender.EntryCar);
        activeRace?.ForfeitPlayer(sender);
    }

    private void OnFinishPacket(ACTcpClient sender, FinishPacket packet)
    {
        Race? activeRace = GetActiveRace(sender.EntryCar);
        activeRace?.OnClientLapCompleted(sender, null);
    }

    private bool IsInTougeSession(EntryCar car)
    {
        EntryCarTougeSession session = GetSession(car);
        if (session.CurrentSession != null)
            return true;
        return false;
    }

    public void InviteNearbyCar(ACTcpClient client)
    {
        EntryCar? nearestCar = GetSession(client!.EntryCar).FindNearbyCar();
        if (nearestCar != null)
        {
            _ = GetSession(client!.EntryCar).ChallengeCar(nearestCar);
            SendNotification(client, "Invite sent!");
        }
        else
        {
            SendNotification(client, "No car nearby!");
        }
    }

    public void InviteCar(ACTcpClient client, ulong recipientId)
    {
        // First find EntryCar in EntryCarManager that matches guid.
        EntryCar? recipientCar = null;
        foreach (EntryCar car in _entryCarManager.EntryCars)
        {
            ACTcpClient? carClient = car.Client;
            if (carClient != null) {
                if (carClient.Guid == recipientId)
                {
                    recipientCar = car;
                    break;
                }
            }
        }

        // Either found the recipient or still null.
        if (recipientCar != null)
        {
            // Invite the recipientCar
            _ = GetSession(client!.EntryCar).ChallengeCar(recipientCar);
            SendNotification(client, "Invite sent!");
        }
        else
        {
            SendNotification(client, "There was an issue sending the invite.");
        }
    }

    internal static void SendNotification(ACTcpClient? client, string message, bool isCountdown = false)
    {
        client?.SendPacket(new NotificationPacket { Message = message, IsCountDown = isCountdown });
    }

    private Dictionary<string, Course> GetCourses()
    {
        // Read starting positions from file
        string trackName = _serverConfig.FullTrackName;
        trackName = trackName.Substring(trackName.LastIndexOf('/') + 1);
        
        if (!File.Exists(startingPositionsFile))
        {
            // Create the file
            string sampleYaml = """
            Tracks:
              your_track_name_here:
                Courses:
                  Sample Course Name:
                    FinishLine:
                      - [0.0, 0.0, 0.0]
                      - [0.0, 0.0, 0.0]
                    StartingSlots:
                      - Leader:
                          Position: [0.0, 0.0, 0.0]
                          Heading: 0
                        Follower:
                          Position: [0.0, 0.0, 0.0]
                          Heading: 0
            """;
            File.WriteAllText(startingPositionsFile, sampleYaml);
            throw new Exception($"No touge starting areas defined in {startingPositionsFile}!");
        }

        var yaml = File.ReadAllText(startingPositionsFile);
        var deserializer = new DeserializerBuilder()
            .WithTypeConverter(new Vector3YamlConverter())
            .WithTypeConverter(new Vector2YamlConverter())
            .IgnoreUnmatchedProperties()
            .Build();
        
        var tracksFile = deserializer.Deserialize<TracksFile>(yaml);

        if (!tracksFile.Tracks.TryGetValue(trackName, out _))
        {
            throw new KeyNotFoundException($"Track '{trackName}' not found in 'cfg/touge_course_setup.yml'. Make sure to define course details for this track.");
        }
        Dictionary<string, Course> courses = tracksFile.Tracks[trackName].Courses;
        foreach (var course in courses)
        {
            course.Value.Name = course.Key;
        }

        if (courses.Count == 0)
        {
            // There are no valid starting areas.
            throw new Exception($"Did not find any valid courses in {startingPositionsFile}. Please define some for the track: {trackName}");
        }

        return courses;
    }

    private void LoadAvatar(ACTcpClient client)
    {
        // Check if its already downloaded
        // Maybe also check if its really old or something.
        if (!IsPictureCached(client))
        {
            EnsureCacheSpace();
            // Download new picture from steam.
            _ = SteamAPIClient.GetSteamAvatarAsync(_configuration.SteamAPIKey!, client.Guid.ToString());
        }
    }

    private static bool IsPictureCached(ACTcpClient client)
    {
        string avatarPath = Path.Combine(avatarFolderPath, client.Guid.ToString() + ".jpg");
        if (File.Exists(avatarPath))
        {
            return true;
        }
        return false;
    }

    private void CheckAvatarsFolder()
    {
        if (!Directory.Exists(avatarFolderPath))
        {
            Directory.CreateDirectory(avatarFolderPath);
        }

        try
        {
            
            var root = Path.GetPathRoot(avatarFolderPath);
            if (root != null)
            {
                DriveInfo drive = new DriveInfo(root);
                if (drive.AvailableFreeSpace < MaxAvatarCacheSizeBytes)
                {
                    Log.Warning("Not enough disk space for avatars. Avatar feature will be disabled.");
                    _loadSteamAvatars = false;
                    return;
                }
            }
        }
        catch (Exception e)
        {
            Log.Warning($"Could not determine available disk space: {e.Message}");
            Log.Warning("Avatar feature will be disabled as a precaution.");
            _loadSteamAvatars = false;
            return;
        }

        _loadSteamAvatars = true;
    }

    private static void EnsureCacheSpace()
    {
        DirectoryInfo dirInfo = new(avatarFolderPath);

        if (!dirInfo.Exists)
            return;

        // Get all avatar files, sorted by LastAccessTime (oldest first)
        var files = dirInfo.GetFiles("*.jpg")
            .OrderBy(f => f.LastAccessTimeUtc)
            .ToList();

        long totalSize = files.Sum(f => f.Length);

        while (totalSize > MaxAvatarCacheSizeBytes && files.Count > 0)
        {
            var fileToDelete = files[0];
            try
            {
                totalSize -= fileToDelete.Length;
                fileToDelete.Delete();
                files.RemoveAt(0);
            }
            catch (Exception ex)
            {
                Log.Warning($"[AvatarCache] Failed to delete {fileToDelete.Name}: {ex.Message}");
                files.RemoveAt(0); // Avoid infinite loop on error
            }
        }
    }
}

