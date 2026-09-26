namespace Portcullis.CiComment.Tests;

public class StickyCommentPublisherTests
{
    private sealed class FakeCommentClient : IPullRequestCommentClient
    {
        private long _nextId = 100;

        public List<PullRequestComment> Comments { get; } = [];

        public int CreateCalls { get; private set; }

        public int UpdateCalls { get; private set; }

        public Task<IReadOnlyList<PullRequestComment>> ListCommentsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<PullRequestComment>>(Comments.ToList());

        public Task CreateCommentAsync(string body, CancellationToken cancellationToken = default)
        {
            CreateCalls++;
            Comments.Add(new PullRequestComment(_nextId++, body));
            return Task.CompletedTask;
        }

        public Task UpdateCommentAsync(long commentId, string body, CancellationToken cancellationToken = default)
        {
            UpdateCalls++;
            var index = Comments.FindIndex(c => c.Id == commentId);
            if (index >= 0)
            {
                Comments[index] = new PullRequestComment(commentId, body);
            }

            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task PublishAsync_WithNoExistingComment_CreatesOne()
    {
        var client = new FakeCommentClient();

        await StickyCommentPublisher.PublishAsync(client, $"{StickyComment.Marker}\nfirst run");

        Assert.Equal(1, client.CreateCalls);
        Assert.Equal(0, client.UpdateCalls);
        Assert.Single(client.Comments);
    }

    [Fact]
    public async Task PublishAsync_WithAnExistingPortcullisComment_UpdatesItInPlaceRatherThanPostingANewOne()
    {
        var client = new FakeCommentClient();
        client.Comments.Add(new PullRequestComment(1, "a human's review comment"));
        client.Comments.Add(new PullRequestComment(2, $"{StickyComment.Marker}\nfirst run"));

        await StickyCommentPublisher.PublishAsync(client, $"{StickyComment.Marker}\nsecond run");

        Assert.Equal(0, client.CreateCalls);
        Assert.Equal(1, client.UpdateCalls);
        Assert.Equal(2, client.Comments.Count);
        Assert.Contains(client.Comments, c => c.Id == 2 && c.Body.Contains("second run"));
        Assert.Contains(client.Comments, c => c.Id == 1 && c.Body.Contains("human"));
    }

    [Fact]
    public async Task PublishAsync_NeverTouchesCommentsWithoutTheMarker()
    {
        var client = new FakeCommentClient();
        client.Comments.Add(new PullRequestComment(1, "unrelated comment"));

        await StickyCommentPublisher.PublishAsync(client, $"{StickyComment.Marker}\nrun");

        Assert.Equal(1, client.CreateCalls);
        Assert.Equal("unrelated comment", client.Comments.Single(c => c.Id == 1).Body);
    }
}
