namespace Portcullis.CiComment;

/// <summary>
/// The minimal surface StickyCommentPublisher needs against a single, already-known pull
/// request. Kept as an interface so the create-vs-update decision (StickyCommentPublisher)
/// is testable without a live GitHub API call — GitHubPullRequestCommentClient is the only
/// production implementation.
/// </summary>
public interface IPullRequestCommentClient
{
    Task<IReadOnlyList<PullRequestComment>> ListCommentsAsync(CancellationToken cancellationToken = default);

    Task CreateCommentAsync(string body, CancellationToken cancellationToken = default);

    Task UpdateCommentAsync(long commentId, string body, CancellationToken cancellationToken = default);
}
