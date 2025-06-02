using System.Numerics;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using Serilog;
using TougePlugin.Models;

namespace TougePlugin.RaceTypes;

public class OutrunRace : IRaceType
{
    private EntryCar? OutrunLeader;
    private EntryCar? OutrunChaser;

    private long LastOvertakeTime { get; set; }
    private Vector3 LastLeaderPosition { get; set; }

    public async Task<RaceResult> RunRaceAsync(Race race)
    {
        OutrunLeader = race.Leader;
        OutrunChaser = race.Follower;

        LastLeaderPosition = OutrunLeader.Status.Position;
        LastOvertakeTime = race.SessionManager.ServerTimeMilliseconds;

        while (true)
        {
            // Check for disconnects
            if (race.Leader.Client == null)
            {
                Log.Debug("Leader disconnected.");
                return RaceResult.Disconnected(race.Leader);
            }
            else if (race.Follower.Client == null)
            {
                Log.Debug("Follower disconnect.");
                return RaceResult.Disconnected(race.Follower);
            }

            UpdateLeader(race);

            Vector3 leaderPosition = OutrunLeader.Status.Position;
            if (Vector3.DistanceSquared(LastLeaderPosition, leaderPosition) > 40000)
            {
                // Leader teleported.
                Log.Debug("Leader teleported, chaser wins.");
                return RaceResult.Win(OutrunChaser);
            }
            LastLeaderPosition = leaderPosition;

            int outRunDistanceSquared = race.Configuration.OutrunLeadDistance * race.Configuration.OutrunLeadDistance;
            if (Vector3.DistanceSquared(OutrunLeader.Status.Position, OutrunChaser.Status.Position) > outRunDistanceSquared)
            {
                // Leader has outrun the chaser.
                Log.Debug("Leader has outrun the chaser.");
                return RaceResult.Win(OutrunLeader);
            }

            // Make this time configurable
            if (race.SessionManager.ServerTimeMilliseconds - LastOvertakeTime > race.Configuration.OutrunLeadTimeout * 1000)
            {
                // The leader has been in the lead for two minutes.
                Log.Debug("Leader has kept the lead for long enough to win.");
                return RaceResult.Win(OutrunLeader);
            }

            if (race.Forfeit.Task.IsCompleted)
            {
                // Player forfeited the session.
                ACTcpClient forfeiter = await race.Forfeit.Task;
                race.SendMessage("Session ended due to forfeit.");
                return RaceResult.Disconnected(forfeiter.EntryCar);
            }

            await Task.Delay(250);
        }
    }

    private void UpdateLeader(Race race)
    {
        float leaderAngle = (float)(Math.Atan2(race.Follower.Status.Position.X - race.Leader.Status.Position.X, race.Follower.Status.Position.Z - race.Leader.Status.Position.Z) * 180 / Math.PI);
        if (leaderAngle < 0)
            leaderAngle += 360;
        float leaderRot = race.Leader.Status.GetRotationAngle();

        leaderAngle += leaderRot;
        leaderAngle %= 360;

        float followerAngle = (float)(Math.Atan2(race.Leader.Status.Position.X - race.Follower.Status.Position.X, race.Leader.Status.Position.Z - race.Follower.Status.Position.Z) * 180 / Math.PI);
        if (followerAngle < 0)
            followerAngle += 360;
        float followerRot = race.Follower.Status.GetRotationAngle();

        followerAngle += followerRot;
        followerAngle %= 360;

        float leaderSpeed = (float)Math.Max(0.07716061728, race.Leader.Status.Velocity.LengthSquared());
        float followerSpeed = (float)Math.Max(0.07716061728, race.Follower.Status.Velocity.LengthSquared());

        float distanceSquared = Vector3.DistanceSquared(race.Leader.Status.Position, race.Follower.Status.Position);

        EntryCar oldLeader = OutrunLeader!;

        if((leaderAngle > 90 && leaderAngle < 275) && OutrunLeader != race.Leader && leaderSpeed > followerSpeed && distanceSquared < 2500)
        {
            OutrunLeader = race.Leader;
            OutrunChaser = race.Follower;
        }
        else if ((followerAngle > 90 && followerAngle < 275) && OutrunLeader != race.Follower && followerSpeed > leaderSpeed && distanceSquared < 2500)
        {
            OutrunLeader = race.Follower;
            OutrunChaser = race.Leader;
        }

        if(oldLeader != OutrunLeader)
        {
            race.SendMessage($"{OutrunLeader!.Client?.Name} has overtaken {oldLeader.Client?.Name}!");

            LastOvertakeTime = race.SessionManager.ServerTimeMilliseconds;
            LastLeaderPosition = OutrunLeader!.Status.Position;
        }
    }

}
