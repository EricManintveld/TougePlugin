using AssettoServer.Network.ClientMessages;

namespace TougePlugin.Packets;

[OnlineEvent(Key = "AS_LockControls")]
public class LockControlsPacket : OnlineEvent<LockControlsPacket>
{
    [OnlineEventField(Name = "lockControls")]
    public bool LockControls;
}
