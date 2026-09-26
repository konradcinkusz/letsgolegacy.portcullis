using System.Runtime.CompilerServices;

// The mutation pass's hand-written mutants (tests/Portcullis.Engine.Tests/Rules/Mutants/)
// reuse the migration rules' internal symbol helpers, so each mutant differs from its rule
// by the one mechanism it breaks rather than by a reimplementation of everything else.
[assembly: InternalsVisibleTo("Portcullis.Engine.Tests")]
