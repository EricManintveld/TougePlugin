using System.Numerics;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using TougePlugin.RaceTypes;
using Serilog;
using TougePlugin.Models;
using TougePlugin.Packets;

namespace TougePlugin;

public class Race
{
    public EntryCar Leader { get; }
    public EntryCar Follower { get; }

    private readonly EntryCarManager _entryCarManager;
    public readonly TougeConfiguration Configuration;
    private readonly Touge _plugin;
    public readonly SessionManager SessionManager;

    public enum JumpstartResult
    {
        None,            // No jumpstart
        Leader,          // Leader performed a jumpstart
        Follower,        // Follower performed a jumpstart
        Both             // Both performed a jumpstart (if both are outside the threshold)
    }

    private bool LeaderSetLap = false;
    public bool FollowerSetLap = false;
    
    public readonly TaskCompletionSource<ACTcpClient> Forfeit = new();
    public TaskCompletionSource<bool> SecondLapCompleted { get; } = new();
    public TaskCompletionSource<ACTcpClient> Disconnected { get; } = new();
    public TaskCompletionSource<bool> FollowerFirst { get; } = new();

    public string LeaderName { get; }
    public string FollowerName { get; }

    private bool IsGo = false;

    private readonly IRaceType _raceType;

    public delegate Race Factory(EntryCar leader, EntryCar follower, IRaceType raceType);

    public Race(EntryCar leader, EntryCar follower, IRaceType raceType, EntryCarManager entryCarManager, TougeConfiguration configuration, Touge plugin, SessionManager sessionManager)
    {
        Leader = leader;
        Follower = follower;

        LeaderName = Leader.Client?.Name!;
        FollowerName = Follower.Client?.Name!;

        _entryCarManager = entryCarManager;
        Configuration = configuration;
        _plugin = plugin;
        SessionManager = sessionManager;
        _raceType = raceType;

        Leader.Client!.Disconnecting += OnClientDisconnected;
        Follower.Client!.Disconnecting += OnClientDisconnected;
    }

    public async Task<RaceResult> RaceAsync()
    {
        try
        {
            Course? course = await InitializeRaceAsync();
            if (course == null)
            {
                return RaceResult.Disconnected(null);
            }

            SendMessage("Race starting soon...");
            if (await WaitWithForfeitAsync(Task.Delay(3000)))
                return RaceResult.Disconnected(Forfeit.Task.Result.EntryCar);

            var countdownResult = await RunCountdownAsync(course);
            if (countdownResult != null)
            {
                return countdownResult;
            }

            return await _raceType.RunRaceAsync(this);
        }

        catch (Exception e)
        {
            Log.Error(e, "Error while running race.");
            SendMessage("There was an error while runnning the race.");
            return RaceResult.Tie();
        }
        finally
        {
            FinishRace();
        }
    }

    private async Task<Course?> InitializeRaceAsync()
    {
        var setupTask = Task.Run(() => SetUpRaceAsync());
        var forfeitTask = Forfeit.Task;

        var completedSetup = await Task.WhenAny(setupTask, forfeitTask);

        if (completedSetup == forfeitTask)
        {
            SendMessage("Race cancelled due to player forfeit.");
            return null;
        }

        Course? course = await setupTask;

        if (course == null)
        {
            SendMessage("Teleportation failed. Race setup aborted.");
            return null;
        }

        return course;
    }

    private async Task<RaceResult?> RunCountdownAsync(Course course)
    {
        while (!IsGo)
        {
            byte signalStage = 0;
            while (signalStage < 3)
            {
                if (!Configuration.IsRollingStart)
                {
                    JumpstartResult jumpstart = AreInStartingPos(course);
                    if (jumpstart != JumpstartResult.None)
                    {
                        if (jumpstart == JumpstartResult.Both)
                        {
                            SendMessage("Both players made a jumpstart.");
                            await RestartRaceAsync();
                            break;
                        }
                        else if (jumpstart == JumpstartResult.Follower)
                        {
                            SendMessage($"{FollowerName} made a jumpstart. {LeaderName} wins this race.");
                            return RaceResult.Win(Leader);
                        }
                        else
                        {
                            SendMessage($"{LeaderName} made a jumpstart. {FollowerName} wins this race.");
                            return RaceResult.Win(Follower);
                        }
                    }
                }

                if (signalStage == 0)
                    _ = SendTimedMessageAsync("Ready...", true);
                else if (signalStage == 1)
                    _ = SendTimedMessageAsync("Set...", true);
                else if (signalStage == 2)
                {
                    if (Configuration.IsRollingStart)
                    {
                        // Check if cars are close enough to each other to give a valid "Go!".
                        if (!IsValidRollingStartPos())
                        {
                            SendMessage("Players are not close enough for a fair rolling start.");
                            await RestartRaceAsync();
                            break;
                        }
                    }
                    _ = SendTimedMessageAsync("Go!", true);
                    IsGo = true;
                    break;
                }
                if (await WaitWithForfeitAsync(Task.Delay(1000)))
                    return RaceResult.Disconnected(Forfeit.Task.Result.EntryCar);
                signalStage++;
            }
        }

        return null;
    }

    private void FinishRace()
    {
        // General clean up
        Leader.Client!.Disconnecting -= OnClientDisconnected;
        Follower.Client!.Disconnecting -= OnClientDisconnected;
    }

    public void SendMessage(string message)
    {
        Touge.SendNotification(Follower.Client, message);
        Touge.SendNotification(Leader.Client, message);
    }

    private async Task RestartRaceAsync()
    {
        SendMessage("Returning both players to their starting positions.");
        SendMessage("Race restarting soon...");
        await Task.Delay(3000);
        Course course = await GetCourseAsync();
        await TeleportToStartAsync(Leader, Follower, course);
    }

    public void OnClientLapCompleted(ACTcpClient sender, LapCompletedEventArgs? args)
    {
        var car = sender.EntryCar;
        if (car == Leader)
            LeaderSetLap = true;
        else if (car == Follower)
            FollowerSetLap = true;

        // If someone already set a lap, and this is the seconds person to set a lap
        if (LeaderSetLap && FollowerSetLap)
            SecondLapCompleted.TrySetResult(true);

        // If only the leader has set a lap
        else if (LeaderSetLap && !FollowerSetLap)
        {
            // Make this time also configurable as outrun time.
            int outrunTimer = (int)(Configuration.CourseOutrunTime * 1000f);
            _ = Task.Delay(outrunTimer).ContinueWith(_ => SecondLapCompleted.TrySetResult(false));
        }

        // Overtake, the follower finished earlier than leader.
        else if (FollowerSetLap && !LeaderSetLap)
            FollowerFirst.TrySetResult(true);
    }

    private void OnClientDisconnected(ACTcpClient sender, EventArgs args)
    {
        ForfeitPlayer(sender);
    }

    internal void ForfeitPlayer(ACTcpClient sender)
    {
        if (IsGo && _raceType is CourseRace)
        {
            // Course race has started so simply set the result of the race to disconnected.
            Disconnected.TrySetResult(sender);
        }
        else
        {
            // Race has not started yet so set result for forfeit task.
            Forfeit.TrySetResult(sender);
        }
        if (!Configuration.UseTrackFinish)
        {
            sender.SendPacket(new FinishPacket { LookForFinish = false });
        }
        UnlockControls();
    }

    private async Task<bool> TeleportToStartAsync(EntryCar Leader, EntryCar Follower, Course course)
    {
        Leader.Client!.SendPacket(new TeleportPacket
        {
            Position = course.Leader.Position,
            Heading = course.Leader.Heading,
        });
        Follower.Client!.SendPacket(new TeleportPacket
        {
            Position = course.Follower.Position,
            Heading = course.Follower.Heading,
        });

        // Check if both cars have been teleported to their starting locations.
        bool isLeaderTeleported = false;
        bool isFollowerTeleported = false;
        const float thresholdSquared = 50f;

        using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var token = cts.Token;

        try
        {
            while (!isLeaderTeleported || !isFollowerTeleported)
            {
                Vector3 currentLeaderPos = Leader.Status.Position;
                Vector3 currentFollowerPos = Follower.Status.Position;

                float leaderDistanceSquared = Vector3.DistanceSquared(currentLeaderPos, course.Leader.Position);
                float followerDistanceSquared = Vector3.DistanceSquared(currentFollowerPos, course.Follower.Position);

                if (leaderDistanceSquared < thresholdSquared)
                {
                    isLeaderTeleported = true;
                }
                if (followerDistanceSquared < thresholdSquared)
                {
                    isFollowerTeleported = true;
                }

                await Task.Delay(250, token);
            }

            return true;
        }
        catch (OperationCanceledException)
        {
            return false;
        }
        finally
        {
            UnlockControls();
        }
    }

    private async Task SendTimedMessageAsync(string message, bool isCountdown = false)
    {
        bool isChallengerHighPing = Leader.Ping > Follower.Ping;
        EntryCar highPingCar, lowPingCar;

        if (isChallengerHighPing)
        {
            highPingCar = Leader;
            lowPingCar = Follower;
        }
        else
        {
            highPingCar = Follower;
            lowPingCar = Leader;
        }

        Touge.SendNotification(highPingCar.Client, message, isCountdown);
        await Task.Delay(highPingCar.Ping - lowPingCar.Ping);
        Touge.SendNotification(lowPingCar.Client, message, isCountdown);
    }

    // Check if the cars are still in their starting positions.
    private JumpstartResult AreInStartingPos(Course course)
    {
        // Get the current position of each car.
        Vector3 currentLeaderPos = Leader.Status.Position;
        Vector3 currentFollowerPos = Follower.Status.Position;

        // Check if they are the same as the original starting postion.
        // Or at least check if the difference is not larger than a threshold.
        float leaderDistanceSquared = Vector3.DistanceSquared(currentLeaderPos, course.Leader.Position);
        float followerDistanceSquared = Vector3.DistanceSquared(currentFollowerPos, course.Follower.Position);

        const float thresholdSquared = 20f;

        // Check if either car has moved too far (jumpstart detection)
        if (leaderDistanceSquared > thresholdSquared && followerDistanceSquared > thresholdSquared)
        {
            // Both cars moved too far
            return JumpstartResult.Both;
        }
        else if (leaderDistanceSquared > thresholdSquared)
        {
            return JumpstartResult.Leader; // Leader caused the jumpstart
        }
        else if (followerDistanceSquared > thresholdSquared)
        {
            return JumpstartResult.Follower; // Follower caused the jumpstart
        }

        return JumpstartResult.None; // No jumpstart
    }

    private bool IsValidRollingStartPos()
    {
        // Check if players are within a certain distance of each other.
        float distanceBetweenCars = Vector3.DistanceSquared(Follower.Status.Position, Leader.Status.Position);
        if (distanceBetweenCars > 30)
            return false;
        return true;
    }

    private Course? FindClearStartArea()
    {
        // Loop over the list of starting positions in the cfg file
        // If you find a valid/clear starting pos, return that.
        foreach (var course in _plugin.tougeCourses)
        {
            if (IsStartPosClear(course.Leader.Position) && IsStartPosClear(course.Follower.Position))
            {
                return course;
            }
        }
        return null;
    }

    private bool IsStartPosClear(Vector3 startPos)
    {
        // Checks if startPos is clear.
        const float startArea = 50f; // Area around the startpoint that should be cleared.
        foreach (var car in _entryCarManager.EntryCars)
        {
            // Dont look at the players in the race or empty cars
            ACTcpClient? carClient = car.Client;
            if (carClient != null && car != Leader && car != Follower)
            {
                // Check if they are not in the starting area.
                float distanceToStartPosSquared = Vector3.DistanceSquared(car.Status.Position, startPos);
                if (distanceToStartPosSquared < startArea)
                {
                    // The car is in the start area.
                    return false;
                }
            }
        }
        return true;
    }

    private async Task<Course> GetCourseAsync()
    {
        // Get the startingArea here.
        int waitTime = 0;
        Course? startingArea = FindClearStartArea();
        while (startingArea == null)
        {
            // Wait for a short time before checking again to avoid blocking the thread
            await Task.Delay(250);

            // Try to find a starting area again
            startingArea = FindClearStartArea();

            waitTime += 1;
            if (waitTime > 40)
            {
                // Fallback after 10 seconds.
                startingArea = _plugin.tougeCourses[0];
            }
        }
        return startingArea;
    }

    private void SendFinishLine(Course course)
    {
        Leader.Client!.SendPacket(new FinishLinePacket { FinishPoint1 = course.FinishLine![0], FinishPoint2 = course.FinishLine[1] });
        Follower.Client!.SendPacket(new FinishLinePacket { FinishPoint1 = course.FinishLine![0], FinishPoint2 = course.FinishLine[1] });
    }

    private async Task<Course?> SetUpRaceAsync()
    {
        Course course = await GetCourseAsync();
        if (!Configuration.UseTrackFinish)
        {
            SendFinishLine(course);
        }

        // First teleport players to their starting positions.
        bool isTeleported = await TeleportToStartAsync(Leader, Follower, course);

        if (!isTeleported)
        {
            SendMessage("Teleportation failed. Race setup timed out.");
            return null;
        }

        return course;
    }
    private async Task<bool> WaitWithForfeitAsync(Task task)
    {
        var completed = await Task.WhenAny(task, Forfeit.Task);
        if (completed == Forfeit.Task)
        {
            SendMessage("Race cancelled due to player forfeit.");
            return true;
        }

        await task; // Propagate exceptions if needed
        return false;
    }

    private void UnlockControls()
    {
        Leader.Client!.SendPacket(new LockControlsPacket { LockControls = false });
        Follower.Client!.SendPacket(new LockControlsPacket { LockControls = false });
    }
}
