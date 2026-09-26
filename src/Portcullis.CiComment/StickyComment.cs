namespace Portcullis.CiComment;

/// <summary>
/// The hidden marker that identifies "the portcullis comment" on a PR among all other
/// comments, so every push updates one comment in place instead of posting a new one.
/// </summary>
public static class StickyComment
{
    public const string Marker = "<!-- portcullis:pr-comment -->";

    public static PullRequestComment? FindExisting(IEnumerable<PullRequestComment> comments) =>
        comments.FirstOrDefault(c => c.Body.Contains(Marker, StringComparison.Ordinal));
}
