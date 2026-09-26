namespace Portcullis.CiComment;

/// <summary>
/// One existing comment on a pull request, as returned by the GitHub issue-comments API
/// (a pull request is an issue for commenting purposes). Only the fields the sticky-comment
/// lookup needs.
/// </summary>
public sealed record PullRequestComment(long Id, string Body);
