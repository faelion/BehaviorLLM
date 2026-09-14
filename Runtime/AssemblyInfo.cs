using System.Runtime.CompilerServices;

// The transport-switch predicate is internal: it is one narrow rule about one response shape, not
// something a consumer should call. The Runtime test assembly is let in rather than widening the
// package's public API for the sake of testing it.
[assembly: InternalsVisibleTo("BehaviorLLM.Tests.Runtime")]
