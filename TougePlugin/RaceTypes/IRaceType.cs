using TougePlugin.Models;

namespace TougePlugin.RaceTypes;
public interface IRaceType
{
    Task<RaceResult> RunRaceAsync(Race race);
}

