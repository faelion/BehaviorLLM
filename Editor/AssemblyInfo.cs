using System.Runtime.CompilerServices;

// The model-manager helpers are implementation detail of the Editor tooling, not package API, so
// they stay internal. The Editor test assembly is let in rather than widening what consumers see:
// the parts worth testing are pure - reading a parameter count out of a repository name, matching
// a download to the preset that expects it - and those deserve tests without becoming API.
[assembly: InternalsVisibleTo("BehaviorLLM.Tests.Editor")]
