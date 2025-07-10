namespace TougePlugin.TougeRulesets;

using AssettoServer.Server;
using TougePlugin.Models;

public class CatAndMouseRuleset : ITougeRuleset
{
    public async Task<RaceResult> RunSessionAsync(TougeSession session)
    {
        // Set the hud
        session.SendSessionState(TougeSession.SessionState.CatAndMouse);

        int challengerPoints = 0;
        int challengedPoints = 0;

        int round = 0;

        while (challengerPoints < 2 && challengedPoints < 2)
        {
            bool even = round % 2 == 0;
            EntryCar leader = even ? session.Challenger : session.Challenged;
            EntryCar follower = even ? session.Challenged : session.Challenger;

            RaceResult result = await session.RunRaceAsync(leader, follower);

            if (result.Outcome == RaceOutcome.Disconnected)
            {
                return RaceResult.Disconnected(result.ResultCar!);
            }

            if (result.Outcome == RaceOutcome.Win)
            {
                session.ApplyRaceResultToStandings(result, round);
                await Task.Delay(TougeSession.coolDownTime);

                if (result.ResultCar == session.Challenger)
                    challengerPoints++;
                else
                    challengedPoints++;
            }

            round++;
        }

        EntryCar winner = challengerPoints == 2 ? session.Challenger : session.Challenged;
        return RaceResult.Win(winner);
    }
}
