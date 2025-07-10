using AssettoServer.Network.ClientMessages;

namespace TougePlugin.Packets;

[OnlineEvent(Key = "AS_Invite")]
public class InvitePacket : OnlineEvent<InvitePacket>
{
    [OnlineEventField(Name = "inviteSenderName", Size = 32)]
    public string InviteSenderName;
    
    [OnlineEventField(Name = "inviteRecipientGuid")]
    public ulong InviteRecipientGuid = 0;
    
    [OnlineEventField(Name = "inviteSenderElo")]
    public int InviteSenderElo;
    
    [OnlineEventField(Name = "inviteSenderId", Size = 32)]
    public string InviteSenderId;
    
    [OnlineEventField(Name = "courseName", Size = 32)]
    public string CourseName;

    [OnlineEventField(Name = "isCourse")]
    public bool IsCourse;
}
