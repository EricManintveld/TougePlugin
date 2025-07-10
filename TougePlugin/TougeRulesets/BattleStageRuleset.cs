using AssettoServer.Server;
using TougePlugin.Models;

namespace TougePlugin.TougeRulesets;

public class BattleStageRuleset : ITougeRuleset
{
    public async Task<RaceResult> RunSessionAsync(TougeSession session)
    {
        // Do the first two races.
        session.SendSessionState(TougeSession.SessionState.FirstTwo);
        RaceResult result = await FirstTwoRaceAsync(session);

        // If the result of the first two races is a tie, race until there is a winner.
        if (result.Outcome == RaceOutcome.Tie)
        {
            session.SendSessionState(TougeSession.SessionState.SuddenDeath);
            result = await RunSuddenDeathRacesAsync(result, session);
        }

        return result;
    }

    private static async Task<RaceResult> FirstTwoRaceAsync(TougeSession session)
    {

        // Run race 1.
        RaceResult result1 = await session.RunRaceAsync(session.Challenger, session.Challenged);

        // If both players are still connected. Run race 2.
        if (result1.Outcome != RaceOutcome.Disconnected)
        {
            session.ApplyRaceResultToStandings(result1, 0);
            await Task.Delay(TougeSession.coolDownTime);
            // If players are still connected, always start second race.
            RaceResult result2 = await session.RunRaceAsync(session.Challenged, session.Challenger);

            if (result2.Outcome != RaceOutcome.Disconnected)
            {
                session.ApplyRaceResultToStandings(result2, 1);
                await Task.Delay(TougeSession.coolDownTime);

                // Both races are finished. Check what to return.
                if (session.IsTie(result1, result2))
                {
                    return RaceResult.Tie();
                }
                else
                {
                    // Its either 0-1 or 0-2.
                    EntryCar winner = GetWinner(result1, result2);
                    return RaceResult.Win(winner);
                }
            }
            else
            {
                // Someone disconnected or forfeited.
                // Check if they won the first race or not.
                if (result2.ResultCar != null && result1.Outcome == RaceOutcome.Win && result1.ResultCar != result2.ResultCar)
                {
                    // The player who disconnected was not leading the standings.
                    // So the other player (who won race 1, is the overall winner)
                    return RaceResult.Win(result1.ResultCar!);
                }
                return RaceResult.Disconnected(result2.ResultCar!);
            }
        }
        return RaceResult.Disconnected(result1.ResultCar!);
    }

    private static EntryCar GetWinner(RaceResult result1, RaceResult result2)
    {
        if (result1.Outcome == RaceOutcome.Win)
            return result1.ResultCar!;
        else
            return result2.ResultCar!;
    }

    private static async Task<RaceResult> RunSuddenDeathRacesAsync(RaceResult firstTwoResult, TougeSession session)
    {
        RaceResult result = firstTwoResult;
        bool isChallengerLeading = true; // Challenger as leader at first.
        bool isFirstSuddenDeathRace = true;
        while (result.Outcome == RaceOutcome.Tie)
        {
            if (!isFirstSuddenDeathRace)
            {
                // Skip cooldown on the first iteration, because of cooldown after first two races.
                await Task.Delay(TougeSession.coolDownTime);
            }

            // Swap the posistion of leader and follower.
            EntryCar leader = isChallengerLeading ? session.Challenger : session.Challenged;
            EntryCar follower = isChallengerLeading ? session.Challenged : session.Challenger;

            result = await session.RunRaceAsync(leader, follower);

            isChallengerLeading = !isChallengerLeading;
            isFirstSuddenDeathRace = false;
        }
        return result;
    }
}

