using TougePlugin.Models;

namespace TougePlugin.TougeRulesets;

public interface ITougeRuleset
{
    Task<RaceResult> RunSessionAsync(TougeSession session);
}
