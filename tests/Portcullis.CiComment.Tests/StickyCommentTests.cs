namespace Portcullis.CiComment.Tests;

public class StickyCommentTests
{
    [Fact]
    public void FindExisting_WithNoComments_ReturnsNull()
    {
        var found = StickyComment.FindExisting([]);

        Assert.Null(found);
    }

    [Fact]
    public void FindExisting_IgnoresCommentsWithoutTheMarker()
    {
        PullRequestComment[] comments =
        [
            new(1, "just a regular review comment"),
            new(2, "looks good to me"),
        ];

        var found = StickyComment.FindExisting(comments);

        Assert.Null(found);
    }

    [Fact]
    public void FindExisting_FindsTheCommentCarryingTheMarker()
    {
        PullRequestComment[] comments =
        [
            new(1, "just a regular review comment"),
            new(2, $"{StickyComment.Marker}\n## Portcullis\n\nsomething"),
            new(3, "another human comment"),
        ];

        var found = StickyComment.FindExisting(comments);

        Assert.NotNull(found);
        Assert.Equal(2, found.Id);
    }
}
