using Xunit;

// The PublicId codec is process-wide (UseCodelabSecurity installs it), so hosts start one at a time.
[assembly: CollectionBehavior(DisableTestParallelization = true)]
