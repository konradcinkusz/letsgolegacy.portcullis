using System.Net.Http;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Portcullis.CiComment;

/// <summary>
/// Thin adapter over the GitHub REST "issue comments" API (pull requests are issues for
/// commenting purposes) for one fixed repository + PR. This is the only part of
/// Portcullis.CiComment that talks to a network — deliberately kept minimal so the logic
/// that decides *what* to send (StickyCommentPublisher, CommentFormatter) stays unit
/// testable without it.
/// </summary>
public sealed class GitHubPullRequestCommentClient : IPullRequestCommentClient, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _http;
    private readonly string _owner;
    private readonly string _repo;
    private readonly int _issueNumber;

    public GitHubPullRequestCommentClient(string apiBaseUrl, string token, string owner, string repo, int issueNumber)
    {
        _owner = owner;
        _repo = repo;
        _issueNumber = issueNumber;

        _http = new HttpClient { BaseAddress = new Uri(apiBaseUrl) };
        _http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        _http.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("portcullis-ci-comment", "1.0"));
        _http.DefaultRequestHeaders.Add("X-GitHub-Api-Version", "2022-11-28");
    }

    public async Task<IReadOnlyList<PullRequestComment>> ListCommentsAsync(CancellationToken cancellationToken = default)
    {
        var response = await _http.GetAsync(
            $"repos/{_owner}/{_repo}/issues/{_issueNumber}/comments?per_page=100",
            cancellationToken);
        response.EnsureSuccessStatusCode();

        var raw = await response.Content.ReadFromJsonAsync<List<GitHubCommentDto>>(JsonOptions, cancellationToken)
            ?? new List<GitHubCommentDto>();
        return raw.Select(c => new PullRequestComment(c.Id, c.Body ?? string.Empty)).ToList();
    }

    public async Task CreateCommentAsync(string body, CancellationToken cancellationToken = default)
    {
        var response = await _http.PostAsJsonAsync(
            $"repos/{_owner}/{_repo}/issues/{_issueNumber}/comments",
            new GitHubCommentRequest(body),
            JsonOptions,
            cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public async Task UpdateCommentAsync(long commentId, string body, CancellationToken cancellationToken = default)
    {
        var request = new HttpRequestMessage(HttpMethod.Patch, $"repos/{_owner}/{_repo}/issues/comments/{commentId}")
        {
            Content = JsonContent.Create(new GitHubCommentRequest(body), options: JsonOptions),
        };

        var response = await _http.SendAsync(request, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    public void Dispose() => _http.Dispose();

    private sealed record GitHubCommentRequest(string Body);

    private sealed class GitHubCommentDto
    {
        [JsonPropertyName("id")]
        public long Id { get; set; }

        [JsonPropertyName("body")]
        public string? Body { get; set; }
    }
}
