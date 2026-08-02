namespace HearthCalendar.Server.Domain;

public sealed class NoOpAiReviewProvider : IAiReviewProvider
{
    public static NoOpAiReviewProvider Instance { get; } = new();

    private NoOpAiReviewProvider()
    {
    }

    public ValueTask<AiReviewSuggestion?> ReviewAsync(
        AiReviewRequest request,
        CancellationToken cancellationToken)
    {
        return ValueTask.FromResult<AiReviewSuggestion?>(null);
    }
}
