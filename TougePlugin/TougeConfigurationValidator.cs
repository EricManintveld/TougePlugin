using FluentValidation;
using JetBrains.Annotations;

namespace TougePlugin;

// Use FluentValidation to validate plugin configuration
[UsedImplicitly]
public class TougeConfigurationValidator : AbstractValidator<TougeConfiguration>
{
    public TougeConfigurationValidator()
    {
        // Validate that each value in the CarPerformanceRatings dictionary is between 1 and 1000
        RuleFor(cfg => cfg.CarPerformanceRatings)
            .Must(BeWithinValidRange)
            .WithMessage(x => GetInvalidRangeMessage(x.CarPerformanceRatings));

        RuleFor(cfg => cfg.MaxEloGain)
            .GreaterThan(0)
            .WithMessage("MaxEloGain must be a positive integer");
        
        RuleFor(cfg => cfg.ProvisionalRaces)
        .GreaterThan(0)
        .WithMessage("ProvisionalRaces must be a positive integer");

        RuleFor(cfg => cfg.MaxEloGainProvisional)
            .GreaterThan(0)
            .WithMessage("MaxEloGainProvisional must be a positive integer");

        RuleFor(cfg => cfg.CourseOutrunTime)
            .InclusiveBetween(1f, 60f)
            .WithMessage("OutrunTime must be an integer between 1 and 60 seconds.");

        RuleFor(cfg => cfg.PostgresqlConnectionString)
            .NotEmpty()
            .WithMessage("PostgreSQL connection string must be provided when isDbLocalMode is false.")
            .When(cfg => !cfg.IsDbLocalMode);

        RuleFor(cfg => cfg.OutrunLeadTimeout)
            .GreaterThan(0)
            .WithMessage("OutrunLeadTimeout must be a positive integer (in seconds).");

        RuleFor(cfg => cfg.OutrunLeadDistance)
            .GreaterThan(0)
            .WithMessage("OutrunLeadDistance must be a positive integer (in meters).");

        RuleFor(cfg => cfg.EnableCourseRace)
            .Must((cfg, _) => cfg.EnableCourseRace || cfg.EnableOutrunRace)
            .WithMessage("At least one of EnableCourseRace or EnableOutrunRace must be true.");
    }

    private bool BeWithinValidRange(Dictionary<string, int> ratings)
    {
        return ratings.All(kvp => kvp.Value >= 1 && kvp.Value <= 1000);
    }

    private string GetInvalidRangeMessage(Dictionary<string, int> ratings)
    {
        var invalidEntries = ratings
            .Where(kvp => kvp.Value < 1 || kvp.Value > 1000)
            .Select(kvp => $"Car '{kvp.Key}' has performance rating {kvp.Value}")
            .ToList();

        return $"The following car performance ratings must be between 1 and 1000: {string.Join(", ", invalidEntries)}";
    }
}
