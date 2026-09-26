using System.Text.Json;
using Portcullis.CiComment;
using Portcullis.Engine.Model;

if (CliOptions.Parse(args) is not { } options)
{
    Console.Error.WriteLine(
        "usage: portcullis-ci-comment --current <path|-> [--previous <path|->] [--output <path>] [--pr <number>]");
    return 2;
}

var current = await ReadScanResultAsync(options.CurrentPath);
if (current is null)
{
    Console.Error.WriteLine($"error: could not read/parse scan result from '{options.CurrentPath}'");
    return 2;
}

ScanResult? previous = null;
if (options.PreviousPath is not null)
{
    previous = await ReadScanResultAsync(options.PreviousPath);
    if (previous is null)
    {
        Console.Error.WriteLine(
            $"warning: could not read/parse previous scan result from '{options.PreviousPath}' — rendering without a diff.");
    }
}

// Compute+render before touching the network: a malformed cached scan must not take
// the tool down after it has already decided the verdict.
string markdown;
try
{
    markdown = CommentFormatter.Render(current, previous);
}
catch (Exception ex)
{
    Console.Error.WriteLine($"error: could not render the comment: {ex.Message}");
    return 2;
}
Console.WriteLine(markdown);

if (options.OutputPath is not null)
{
    await File.WriteAllTextAsync(options.OutputPath, markdown);
}

var publishContext = GitHubPublishContext.FromEnvironment(options.PullRequestNumber);
if (publishContext is null)
{
    Console.Error.WriteLine(
        "portcullis: no GitHub PR context found (need GITHUB_TOKEN, GITHUB_REPOSITORY, and either --pr or a " +
        "pull_request GITHUB_EVENT_PATH) — skipping comment publish.");
}
else
{
    // A pull_request event from a fork gets a read-only GITHUB_TOKEN no matter what the
    // workflow's `permissions:` block says, so the POST 403s and EnsureSuccessStatusCode
    // throws. Publishing is a courtesy; the gate verdict below is the job. Letting a
    // failed comment kill the process turned "we could not comment" into a stack trace
    // that looks exactly like "the gate blocked" — two very different outcomes rendered
    // identically.
    //
    // The client is constructed INSIDE the try: its constructor builds an HttpClient and
    // parses ApiBaseUrl, so a malformed GITHUB_API_URL threw before the guard could
    // apply. And the filter is a deny-list rather than an allow-list of transport
    // exceptions, because the failure modes here are open-ended — a 403 body that is not
    // JSON reaches StickyComment's parser and throws JsonException, which an
    // HttpRequestException-shaped filter does not catch. Only cancellation is allowed to
    // propagate, since that is the caller asking to stop rather than a publish failure.
    try
    {
        using var client = new GitHubPullRequestCommentClient(
            publishContext.ApiBaseUrl,
            publishContext.Token,
            publishContext.Owner,
            publishContext.Repo,
            publishContext.PullRequestNumber);

        await StickyCommentPublisher.PublishAsync(client, markdown);
        Console.Error.WriteLine(
            $"portcullis: sticky comment published to {publishContext.Owner}/{publishContext.Repo}#{publishContext.PullRequestNumber}");
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
        Console.Error.WriteLine(
            $"portcullis: warning: could not publish the sticky comment ({ex.GetType().Name}: {ex.Message}). " +
            "This is expected for a pull_request event from a fork, where GITHUB_TOKEN is read-only. " +
            "The gate verdict below is unaffected.");
    }
}

// current.Gate is populated by any fresh portcullis scan; a legacy scan JSON predating
// the diff-scoped gate (or a hand-written fixture) deserializes it as null, in which
// case this falls back to the original, unconditional absolute-count behavior.
return (current.Gate?.Blocked ?? current.Summary.ErrorCount > 0) ? 1 : 0;

// The read is inside the try as well as the parse. It used to sit outside, so a missing
// or unreadable file threw FileNotFoundException/DirectoryNotFoundException straight out
// of the program and the "could not read/parse" branch at the call site was unreachable
// for exactly the I/O errors it names. The PR workflow feeds --previous from a restored
// cache, which is absent on a PR's first push, so this is the ordinary path, not an edge
// case.
//
// A well-formed but incomplete document is caught here too: ScanResult is a positional
// record, so "{}" deserializes with null Violations and Summary, and every consumer
// downstream assumes those are present. A cache entry written by an older engine version,
// or truncated by a crashed scan, is exactly that shape.
static async Task<ScanResult?> ReadScanResultAsync(string pathOrDash)
{
    try
    {
        var json = pathOrDash == "-"
            ? await Console.In.ReadToEndAsync()
            : await File.ReadAllTextAsync(pathOrDash);

        var result = JsonSerializer.Deserialize<ScanResult>(
            json,
            new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

        return result is { Violations: not null, Summary: not null } ? result : null;
    }
    catch (Exception ex) when (ex is JsonException or IOException or UnauthorizedAccessException or ArgumentException)
    {
        return null;
    }
}
