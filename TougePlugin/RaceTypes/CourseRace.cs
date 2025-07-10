using TougePlugin.Models;
using TougePlugin.Packets;

namespace TougePlugin.RaceTypes;

public class CourseRace : IRaceType
{
    public async Task<RaceResult> RunRaceAsync(Race race)
    {
        try
        {
            if (race.Configuration.UseTrackFinish)
            {
                race.Leader.Client!.LapCompleted += race.OnClientLapCompleted;
                race.Follower.Client!.LapCompleted += race.OnClientLapCompleted;
            }
            else
            {
                NotifyLookForFinish(true, race);
            }

            Task completedRace = await Task.WhenAny(race.SecondLapCompleted.Task, race.Disconnected.Task, race.FollowerFirst.Task);

            if (completedRace == race.Disconnected.Task)
            {
                race.SendMessage("Race cancelled due to disconnection or forfeit.");
                return RaceResult.Disconnected(race.Disconnected.Task.Result.EntryCar);
            }
            else if (!race.FollowerSetLap)
            {
                race.SendMessage($"{race.FollowerName} did not finish in time. {race.LeaderName} wins!");
                return RaceResult.Win(race.Leader);
            }
            else if (completedRace == race.FollowerFirst.Task)
            {
                race.SendMessage($"{race.FollowerName} overtook {race.LeaderName}. {race.FollowerName} wins!");
                return RaceResult.Win(race.Follower);
            }
            else
            {
                race.SendMessage($"{race.LeaderName} did not pull away. It's a tie!");
                return RaceResult.Tie();
            }
        }
        finally
        {
            NotifyLookForFinish(false, race);
            race.Leader.Client!.LapCompleted -= race.OnClientLapCompleted;
            race.Follower.Client!.LapCompleted -= race.OnClientLapCompleted;
        }
    }

    private static void NotifyLookForFinish(bool lookForFinish, Race race)
    {
        race.Leader.Client!.SendPacket(new FinishPacket
        {
            LookForFinish = lookForFinish,
        });
        race.Follower.Client!.SendPacket(new FinishPacket
        {
            LookForFinish = lookForFinish,
        });
    }


}
