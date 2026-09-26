namespace Portcullis.CiComment;

/// <summary>
/// Finds the existing portcullis comment on a PR (via StickyComment's marker) and updates it,
/// or creates it if this is the first push. The one place that decides create-vs-update.
/// </summary>
public static class StickyCommentPublisher
{
    public static async Task PublishAsync(
        IPullRequestCommentClient client,
        string body,
        CancellationToken cancellationToken = default)
    {
        var comments = await client.ListCommentsAsync(cancellationToken);
        var existing = StickyComment.FindExisting(comments);

        if (existing is null)
        {
            await client.CreateCommentAsync(body, cancellationToken);
        }
        else
        {
            await client.UpdateCommentAsync(existing.Id, body, cancellationToken);
        }
    }
}
