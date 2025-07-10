using AssettoServer.Network.ClientMessages;

namespace TougePlugin.Packets;

[OnlineEvent(Key = "AS_SessionState")]
public class SessionStatePacket : OnlineEvent<SessionStatePacket>
{
    [OnlineEventField(Name = "result1")]
    public int Result1;
    [OnlineEventField(Name = "result2")]
    public int Result2;
    [OnlineEventField(Name = "result3")]
    public int Result3;
    [OnlineEventField(Name = "sessionState")]
    public int SessionState;
}
