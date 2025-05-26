using System.Numerics;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using Serilog;
using TougePlugin.Models;
using TougePlugin.Packets;

namespace TougePlugin;

public class Race
{
    public EntryCar Leader { get; }
    public EntryCar Follower { get; }

    private readonly EntryCarManager _entryCarManager;
    private readonly TougeConfiguration _configuration;
    private readonly Touge _plugin;

    public enum JumpstartResult
    {
        None,            // No jumpstart
        Leader,          // Leader performed a jumpstart
        Follower,        // Follower performed a jumpstart
        Both             // Both performed a jumpstart (if both are outside the threshold)
    }

    private bool LeaderSetLap = false;
    private bool FollowerSetLap = false;
    private readonly TaskCompletionSource<bool> secondLapCompleted = new();
    private readonly TaskCompletionSource<ACTcpClient> _disconnected = new();
    private readonly TaskCompletionSource<bool> _followerFirst = new();
    private readonly TaskCompletionSource<ACTcpClient> _forfeit = new();

    private string LeaderName { get; }
    private string FollowerName { get; }

    private bool IsGo = false;

    public delegate Race Factory(EntryCar leader, EntryCar follower);

    public Race(EntryCar leader, EntryCar follower, EntryCarManager entryCarManager, TougeConfiguration configuration, Touge plugin)
    {
        Leader = leader;
        Follower = follower;

        LeaderName = Leader.Client?.Name!;
        FollowerName = Follower.Client?.Name!;

        _entryCarManager = entryCarManager;
        _configuration = configuration;
        _plugin = plugin;

        if (_configuration.useTrackFinish)
        {
            Leader.Client!.LapCompleted += OnClientLapCompleted;
            Follower.Client!.LapCompleted += OnClientLapCompleted;
        }

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
                return RaceResult.Disconnected(_forfeit.Task.Result.EntryCar);

            var countdownResult = await RunCountdownAsync(course);
            if (countdownResult != null)
            {
                return countdownResult;
            }

            return await RunRaceAsync();
            
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
        var forfeitTask = _forfeit.Task;

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
                if (!_configuration.isRollingStart)
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
                    _ = SendTimedMessageAsync("Ready...");
                else if (signalStage == 1)
                    _ = SendTimedMessageAsync("Set...");
                else if (signalStage == 2)
                {
                    if (_configuration.isRollingStart)
                    {
                        // Check if cars are close enough to each other to give a valid "Go!".
                        if (!IsValidRollingStartPos())
                        {
                            SendMessage("Players are not close enough for a fair rolling start.");
                            await RestartRaceAsync();
                            break;
                        }
                    }
                    _ = SendTimedMessageAsync("Go!");
                    IsGo = true;
                    break;
                }
                if (await WaitWithForfeitAsync(Task.Delay(1000)))
                    return RaceResult.Disconnected(_forfeit.Task.Result.EntryCar);
                signalStage++;
            }
        }

        return null;
    }

    private async Task<RaceResult> RunRaceAsync()
    {
        if (!_configuration.useTrackFinish)
        {
            NotifyLookForFinish(true);
        }
        Task completedRace = await Task.WhenAny(secondLapCompleted.Task, _disconnected.Task, _followerFirst.Task);

        if (completedRace == _disconnected.Task)
        {
            SendMessage("Race cancelled due to disconnection or forfeit.");
            return RaceResult.Disconnected(_disconnected.Task.Result.EntryCar);
        }
        else if (!FollowerSetLap)
        {
            SendMessage($"{FollowerName} did not finish in time. {LeaderName} wins!");
            return RaceResult.Win(Leader);
        }    
        else if (completedRace == _followerFirst.Task)
        {
            SendMessage($"{FollowerName} overtook {LeaderName}. {FollowerName} wins!");
            return RaceResult.Win(Follower);
        }
        else
        {
            SendMessage($"{LeaderName} did not pull away. It's a tie!");
            return RaceResult.Tie();
        }
    }

    private void FinishRace()
    {
        // Clean up
        Leader.Client!.LapCompleted -= OnClientLapCompleted;
        Follower.Client!.LapCompleted -= OnClientLapCompleted;
        Leader.Client.Disconnecting -= OnClientDisconnected;
        Follower.Client.Disconnecting -= OnClientDisconnected;
        NotifyLookForFinish(false);
    }

    private void SendMessage(string message)
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
            secondLapCompleted.TrySetResult(true);

        // If only the leader has set a lap
        else if (LeaderSetLap && !FollowerSetLap)
        {
            // Make this time also configurable as outrun time.
            int outrunTimer = _configuration.outrunTime * 1000;
            _ = Task.Delay(outrunTimer).ContinueWith(_ => secondLapCompleted.TrySetResult(false));
        }

        // Overtake, the follower finished earlier than leader.
        else if (FollowerSetLap && !LeaderSetLap)
            _followerFirst.TrySetResult(true);
    }

    private void OnClientDisconnected(ACTcpClient sender, EventArgs args)
    {
        _disconnected.TrySetResult(sender);
    }

    internal void ForfeitPlayer(ACTcpClient sender)
    {
        if (IsGo)
        {
            // Race has started so simply set the result of the race to disconnected.
            _disconnected.TrySetResult(sender);
        }
        else
        {
            // Race has not started yet so set result for forfeit task.
            _forfeit.TrySetResult(sender);
        }
        if (!_configuration.useTrackFinish)
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

    private async Task SendTimedMessageAsync(string message)
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

        highPingCar.Client?.SendChatMessage(message);
        Touge.SendNotification(highPingCar.Client, message);
        await Task.Delay(highPingCar.Ping - lowPingCar.Ping);
        lowPingCar.Client?.SendChatMessage(message);
        Touge.SendNotification(lowPingCar.Client, message);
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

        const float thresholdSquared = 40f;

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

    private void NotifyLookForFinish(bool lookForFinish)
    {
        Leader.Client!.SendPacket(new FinishPacket
        {
            LookForFinish = lookForFinish,
        });
        Follower.Client!.SendPacket(new FinishPacket
        {
            LookForFinish = lookForFinish,
        });
    }

    private void SendFinishLine(Course course)
    {
        Leader.Client!.SendPacket(new FinishLinePacket { FinishPoint1 = course.FinishLine![0], FinishPoint2 = course.FinishLine[1] });
        Follower.Client!.SendPacket(new FinishLinePacket { FinishPoint1 = course.FinishLine![0], FinishPoint2 = course.FinishLine[1] });
    }

    private async Task<Course?> SetUpRaceAsync()
    {
        Course course = await GetCourseAsync();
        if (!_configuration.useTrackFinish)
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
        var completed = await Task.WhenAny(task, _forfeit.Task);
        if (completed == _forfeit.Task)
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
