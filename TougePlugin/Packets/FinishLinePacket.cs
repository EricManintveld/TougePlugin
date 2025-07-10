using System.Numerics;
using AssettoServer.Network.ClientMessages;

namespace TougePlugin.Packets;

[OnlineEvent(Key = "AS_FinishLine")]
public class FinishLinePacket : OnlineEvent<FinishLinePacket>
{
    [OnlineEventField(Name = "finishPoint1")]
    public Vector2 FinishPoint1;
    [OnlineEventField(Name = "finishPoint2")]
    public Vector2 FinishPoint2;
}
