using AssettoServer.Network.ClientMessages;

namespace TougePlugin.Packets;

[OnlineEvent(Key = "AS_Initialization")]
public class InitializationPacket : OnlineEvent<InitializationPacket>
{
    [OnlineEventField(Name = "elo")]
    public int Elo = 1000;

    [OnlineEventField(Name = "racesCompleted")]
    public int RacesCompleted = 0;

    [OnlineEventField(Name = "useTrackFinish")]
    public bool UseTrackFinish;

    [OnlineEventField(Name = "discreteMode")]
    public bool DiscreteMode = false;

    [OnlineEventField(Name = "loadSteamAvatars")]
    public bool LoadSteamAvatars = false;

    [OnlineEventField(Size = 1024)]
    public string CourseNames;
}
