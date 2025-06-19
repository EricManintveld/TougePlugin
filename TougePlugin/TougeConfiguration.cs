using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using TougePlugin.Models;
using YamlDotNet.Serialization;

namespace TougePlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class TougeConfiguration : IValidateConfiguration<TougeConfigurationValidator>
{
    [YamlMember(Description = "Car performance ratings keyed by car model name. Can range from 1 - 1000.")]
    public Dictionary<string, int> CarPerformanceRatings { get; init; } = new Dictionary<string, int>
    {
        { "ks_mazda_miata", 125 },
        { "ks_toyota_ae86", 131 }
    };
    
    [YamlMember(Description = "Maximum elo gain. Must be a positive value.")]
    public int MaxEloGain { get; init; } = 32;
    
    [YamlMember(Description = "Number of races for which is player is marked as provisional for the elo system.")]
    public int ProvisionalRaces = 20;

    [YamlMember(Description = "Maximum elo gain, when player is marked as provisional")]
    public int MaxEloGainProvisional = 50;

    [YamlMember(Description = "Rolling start enabled.")]
    public bool IsRollingStart = false;

    [YamlMember(Description = "Outrun timer in seconds for course races. Chase car has to finish within this amount of time after the lead car crosses the finish line.")]
    public float CourseOutrunTime = 1.5f;

    [YamlMember(Description = "Local database mode enabled. If disabled please provide database connection information.")]
    public bool IsDbLocalMode = true;

    [YamlMember(Description = "Connection string to PostgreSQL database. Can be left empty if isDbLocalMode = true.")]
    public string? PostgresqlConnectionString;

    [YamlMember(Description = "Use the track's built in finish line as course end point.")]
    public bool UseTrackFinish = true;

    [YamlMember(Description = "The ruleset used for the touge sessions. Options: BattleStage or CatAndMouse.")]
    public RulesetType RuleSetType = RulesetType.BattleStage;

    [YamlMember(Description = "The type or race. Options: Course or Outrun.")]
    public RaceType RaceType = RaceType.Course;

    [YamlMember(Description = "Discrete mode for the hud. Only shows the hud when necassary, hidden otherwise.")]
    public bool DiscreteMode = false;

    [YamlMember(Description = "Steam API key. Make sure NOT to share with anyone!")]
    public string? SteamAPIKey;

    [YamlMember(Description = "Time limit for outrun races. If the leader is in the lead for this amount of time, they also win. Time is in seconds.")]
    public int OutrunLeadTimeout = 120;

    [YamlMember(Description = "Distance the leader has to outrun the chaser to win in outrun races. Distance in meters.")]
    public int OutrunLeadDistance = 750;

    [YamlMember(Description = "Whether players can challenge others to outrun races.")]
    public bool EnableOutrunRace = false;

    [YamlMember(Description = "Whether or not courser race (races with a defined finish line) are enabled.")]
    public bool EnableCourseRace = true;
}
