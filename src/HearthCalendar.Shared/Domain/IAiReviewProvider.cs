namespace HearthCalendar.Shared.Domain;

public interface IAiReviewProvider
{
    ValueTask<AiReviewSuggestion?> ReviewAsync(
        AiReviewRequest request,
        CancellationToken cancellationToken);
}
