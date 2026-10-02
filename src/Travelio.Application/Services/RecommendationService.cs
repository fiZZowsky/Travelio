using Travelio.Domain;

namespace Travelio.Application.Services;

public sealed class RecommendationService(IDestinationCatalog catalog) : IRecommendationService
{
    public IReadOnlyList<DestinationRecommendation> Recommend(TravelerPreferences preferences)
    {
        if (preferences.DailyBudgetPln <= 0 || preferences.Month is < 1 or > 12 || preferences.Days is < 1 or > 30)
            throw new DomainException("Sprawdź budżet, miesiąc i długość podróży.");

        return catalog.All.Select(destination =>
        {
            var reasons = new List<string>();
            var score = 20;
            if (destination.Styles.Contains(preferences.Style)) { score += 45; reasons.Add("Pasuje do Twoich zainteresowań"); }
            if (destination.BestMonths.Contains(preferences.Month)) { score += 25; reasons.Add("Polecany sezon według przewodnika"); }
            if (string.IsNullOrEmpty(preferences.Region) || preferences.Region == destination.Region) score += 10;
            else score -= 20;
            return new DestinationRecommendation(destination, Math.Clamp(score, 0, 100), reasons.ToArray());
        }).OrderByDescending(x => x.Match).ThenBy(x => x.Destination.Name).ToArray();
    }
}

