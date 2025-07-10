using AssettoServer.Network.ClientMessages;

namespace TougePlugin.Packets;

[OnlineEvent(Key = "AS_Finish")]
public class FinishPacket : OnlineEvent<FinishPacket>
{
    [OnlineEventField(Name = "lookForFinish")]
    public bool LookForFinish = true;
}
