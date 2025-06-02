using AssettoServer.Server;
using TougePlugin.Packets;
using Serilog;
using TougePlugin.Models;
using TougePlugin.TougeRulesets;
using TougePlugin.RaceTypes;

namespace TougePlugin;

public class TougeSession
{
    public EntryCar Challenger { get; }
    public EntryCar Challenged { get; }

    private int winCounter = 0;
    private readonly int[] challengerStandings = [(int)RaceResultCounter.Tbd, (int)RaceResultCounter.Tbd, (int)RaceResultCounter.Tbd];
    private readonly int[] challengedStandings = [(int)RaceResultCounter.Tbd, (int)RaceResultCounter.Tbd, (int)RaceResultCounter.Tbd];

    public const int coolDownTime = 6000; // Cooldown timer for inbetween races.

    private enum RaceResultCounter
    {
        Tbd = 0,
        Win = 1,
        Loss = 2,
        Tie = 3,
    }

    public enum SessionState
    {
        Off = 0,
        FirstTwo = 1,
        SuddenDeath = 2,
        Finished = 3,
        CatAndMouse = 4,
        NoUpdate = 5,
    }

    public bool IsActive { get; private set; }
    public Race? ActiveRace = null;

    private readonly EntryCarManager _entryCarManager;
    private readonly Touge _plugin;
    public readonly Race.Factory _raceFactory;
    public readonly Func<RaceType, IRaceType> _raceTypeFactory;
    private readonly TougeConfiguration _configuration;
    private readonly ITougeRuleset _ruleset;

    public delegate TougeSession Factory(EntryCar challenger, EntryCar challenged, ITougeRuleset ruleset);

    public TougeSession(EntryCar challenger, EntryCar challenged, EntryCarManager entryCarManager, Touge plugin, Race.Factory raceFactory, TougeConfiguration configuration, ITougeRuleset ruleset, Func<RaceType, IRaceType> raceTypeFactory)
    {
        Challenger = challenger;
        Challenged = challenged;
        _entryCarManager = entryCarManager;
        _plugin = plugin;
        _raceFactory = raceFactory;
        _configuration = configuration;
        _ruleset = ruleset;
        _raceTypeFactory = raceTypeFactory;
    }

    public Task StartAsync()
    {
        if (!IsActive)
        {
            IsActive = true;
            _ = Task.Run(TougeSessionAsync);
        }

        return Task.CompletedTask;
    }

    private async Task TougeSessionAsync()
    {
        try
        {
            RaceResult result = await _ruleset.RunSessionAsync(this);

            if (result.Outcome != RaceOutcome.Disconnected)
            {
                UpdateStandings(result.ResultCar!, 2, SessionState.Finished);
                UpdateEloAsync(result.ResultCar!);

                EntryCar loser = result.ResultCar! == Challenged ? Challenger : Challenged;
                string loserName = loser.Client?.Name!;
                string winnerName = result.ResultCar!.Client?.Name!;
                _entryCarManager.BroadcastChat($"{winnerName} beat {loserName}");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error while running touge session.");
        }
        finally
        {
            await FinishTougeSessionAsync();
        }
    }

    public async Task<RaceResult> RunRaceAsync(EntryCar leader, EntryCar follower)
    {
        IRaceType raceType = _raceTypeFactory(_configuration.RaceType);
        Course course = _plugin.tougeCourses["Yaesu Route"]; // Grab something for testing
        Race race = _raceFactory(leader, follower, raceType, course);
        ActiveRace = race;
        RaceResult result = await race.RaceAsync();
        ActiveRace = null;

        return result;
    }

    public void ApplyRaceResultToStandings(RaceResult result, int raceIndex)
    {
        if (result.Outcome == RaceOutcome.Win)
        {
            UpdateStandings(result.ResultCar!, raceIndex);
            winCounter++;
        }
        else
        {
            // Tie case.
            UpdateStandings(null, raceIndex);
        }
    }

    public bool IsTie(RaceResult r1, RaceResult r2)
    {
        bool bothAreWins = r1.Outcome == RaceOutcome.Win && r2.Outcome == RaceOutcome.Win;
        bool differentWinners = r1.ResultCar != r2.ResultCar;
        return winCounter == 0 || (bothAreWins && differentWinners);
    }

    private async Task FinishTougeSessionAsync()
    {
        _plugin.GetSession(Challenger).CurrentSession = null;
        _plugin.GetSession(Challenged).CurrentSession = null;

        await Task.Delay(coolDownTime); // Small cooldown to shortly keep scoreboard up after session has ended.

        // Turn off and reset hud
        Array.Fill(challengerStandings, (int)RaceResultCounter.Tbd);
        Array.Fill(challengedStandings, (int)RaceResultCounter.Tbd);
        SendSessionState(SessionState.Off);
    }

    private async void UpdateEloAsync(EntryCar? winner)
    {
        if (winner == null) return;
        var loser = (winner == Challenger) ? Challenged : Challenger;

        string winnerId = winner.Client!.Guid.ToString();
        string loserId = loser.Client!.Guid.ToString();

        int winnerCarRating = GetCarRating(winner.Model);
        int loserCarRating = GetCarRating(loser.Model);

        var (winnerElo, winnerRacesCompleted) = await _plugin.database.GetPlayerStatsAsync(winnerId);
        var (loserElo, loserRacesCompleted) = await _plugin.database.GetPlayerStatsAsync(loserId);

        int newWinnerElo = CalculateElo(winnerElo, loserElo, winnerCarRating, loserCarRating, true, winnerRacesCompleted);
        int newLoserElo = CalculateElo(loserElo, winnerElo, loserCarRating, winnerCarRating, false, loserRacesCompleted);

        // Update elo in the database.
        await _plugin.database.UpdatePlayerEloAsync(winnerId, newWinnerElo);
        await _plugin.database.UpdatePlayerEloAsync(loserId, newLoserElo);

        // Send new elo the clients.
        winner.Client!.SendPacket(new EloPacket { Elo = newWinnerElo });
        loser.Client!.SendPacket(new EloPacket { Elo = newLoserElo });
    }

    private int CalculateElo(int playerElo, int opponentElo, int playerCarRating, int opponentCarRating, bool hasWon, int racesCompleted)
    {
        // Calculate car performance difference factor
        double carAdvantage = (playerCarRating - opponentCarRating) / 100;

        // Adjust effective ratings based on car performance.
        double effectivePlayerElo = playerElo - carAdvantage * 100;

        // Calculate expected outcome (standard ELO formula)
        double expectedResult = 1.0 / (1.0 + Math.Pow(10.0, (opponentElo - effectivePlayerElo) / 400.0));

        int maxGain = _configuration.MaxEloGain;
        if (racesCompleted < _configuration.ProvisionalRaces)
        {
            maxGain = _configuration.MaxEloGainProvisional;
        }

        // Calculate base ELO change
        int result = hasWon ? 1 : 0;
        double eloChange = maxGain * (result - expectedResult);

        // Apply car performance adjustment to ELO change
        // If player has better car (positive car_advantage), reduce gains and increase losses
        // If player has worse car (negative car_advantage), increase gains and reduce losses
        double carFactor = 1 - (carAdvantage * 0.5);

        // Ensure car_factor is within reasonable bounds (0.5 to 1.5)
        carFactor = Math.Max(0.5, Math.Min(1.5, carFactor));

        // Apply car factor to elo change.
        int adjustedEloChange = (int)Math.Round(eloChange * carFactor);

        int newElo = playerElo + adjustedEloChange;

        // Ensure Elo rating never goes below 0
        return Math.Max(0, newElo);
    }

    private int GetCarRating(string carModel)
    {
        // Check if the rating is in the cfg file
        int performance = 500; // Default value
        if (_configuration.CarPerformanceRatings.TryGetValue(carModel, out int carPerformance))
        {
            performance = carPerformance;
        }
        return performance;
    }

    private void UpdateStandings(EntryCar? winner, int scoreboardIndex, SessionState hudState = SessionState.NoUpdate)
    {
        // Update the standings arrays
        // Maybe just store the winner in a list? Not 2. And figure out by Client how the score should be sent.
        if (winner == null)
        {
            // Tie
            challengerStandings[scoreboardIndex] = (int)RaceResultCounter.Tie;
            challengedStandings[scoreboardIndex] = (int)RaceResultCounter.Tie;
        }
        else if (winner == Challenger)
        {
            challengerStandings[scoreboardIndex] = (int)RaceResultCounter.Win;
            challengedStandings[scoreboardIndex] = (int)RaceResultCounter.Loss;
        }
        else
        {
            challengerStandings[scoreboardIndex] = (int)RaceResultCounter.Loss;
            challengedStandings[scoreboardIndex] = (int)RaceResultCounter.Win;
        }

        // Now update client side.
        SendSessionState(hudState);
    }

    public void SendSessionState(SessionState hudState)
    {
        Challenger.Client!.SendPacket(new SessionStatePacket { Result1 = challengerStandings[0], Result2 = challengerStandings[1], Result3 = challengerStandings[2], SessionState = (int)hudState });
        Challenged.Client!.SendPacket(new SessionStatePacket { Result1 = challengedStandings[0], Result2 = challengedStandings[1], Result3 = challengedStandings[2], SessionState = (int)hudState });
    }
}
