using System.Text.Json;

namespace Portcullis.CiComment;

/// <summary>
/// What GitHubPullRequestCommentClient needs to reach one specific PR, resolved from the
/// environment GitHub Actions sets on every `pull_request`-triggered run (GITHUB_TOKEN,
/// GITHUB_REPOSITORY, GITHUB_API_URL, GITHUB_EVENT_PATH) plus an optional explicit --pr
/// override. Returns null — rather than throwing — when the context is incomplete, so
/// Program.cs can fall back to local rendering only (the "mock file" mode M2B allows).
/// </summary>
public sealed record GitHubPublishContext(string ApiBaseUrl, string Token, string Owner, string Repo, int PullRequestNumber)
{
    public static GitHubPublishContext? FromEnvironment(int? explicitPrNumber)
    {
        var token = Environment.GetEnvironmentVariable("GITHUB_TOKEN");
        var repository = Environment.GetEnvironmentVariable("GITHUB_REPOSITORY");

        if (string.IsNullOrEmpty(token) || string.IsNullOrEmpty(repository))
        {
            return null;
        }

        var parts = repository.Split('/', 2);
        if (parts.Length != 2)
        {
            return null;
        }

        var prNumber = explicitPrNumber ?? ReadPullRequestNumberFromEventPayload();
        if (prNumber is null)
        {
            return null;
        }

        var apiUrl = Environment.GetEnvironmentVariable("GITHUB_API_URL");
        var baseUrl = string.IsNullOrEmpty(apiUrl) ? "https://api.github.com/" : apiUrl.TrimEnd('/') + "/";

        return new GitHubPublishContext(baseUrl, token, parts[0], parts[1], prNumber.Value);
    }

    private static int? ReadPullRequestNumberFromEventPayload()
    {
        var eventPath = Environment.GetEnvironmentVariable("GITHUB_EVENT_PATH");
        if (string.IsNullOrEmpty(eventPath) || !File.Exists(eventPath))
        {
            return null;
        }

        try
        {
            using var stream = File.OpenRead(eventPath);
            using var document = JsonDocument.Parse(stream);

            if (document.RootElement.TryGetProperty("pull_request", out var pr) &&
                pr.TryGetProperty("number", out var number) &&
                number.TryGetInt32(out var value))
            {
                return value;
            }
        }
        catch (JsonException)
        {
            return null;
        }

        return null;
    }
}
