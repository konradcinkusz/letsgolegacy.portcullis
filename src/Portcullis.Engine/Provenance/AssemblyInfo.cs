using System.Runtime.CompilerServices;

// Lets Portcullis.Engine.Tests's git-repository test fixture reuse GitCommandRunner
// directly instead of duplicating its process-invocation logic.
[assembly: InternalsVisibleTo("Portcullis.Engine.Tests")]
