using AssettoServer.Server;
using TougePlugin.Models;

namespace TougePlugin.TougeRulesets;

public class BattleStageRuleset : ITougeRuleset
{
    public async Task<RaceResult> RunSessionAsync(TougeSession session)
    {
        // Do the first two races.
        session.SendSessionState(TougeSession.SessionState.FirstTwo);
        RaceResult result = await session.FirstTwoRaceAsync();

        // If the result of the first two races is a tie, race until there is a winner.
        if (result.Outcome == RaceOutcome.Tie)
        {
            session.SendSessionState(TougeSession.SessionState.SuddenDeath);
            result = await session.RunSuddenDeathRacesAsync(result);
        }

        return result;
    }
}

