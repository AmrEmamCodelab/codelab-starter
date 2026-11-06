using Starter.Application.Jobs;

namespace Starter.Modules.Jobs.Application.Catalog;

/// <summary>
/// The registered jobs (every <see cref="IJob"/> added with <c>AddStarterJob</c>), ordered by key. Built per DI scope,
/// so each run and each request sees the job instances of its own scope. A catalog that would be ambiguous or
/// unschedulable is refused when it is built.
/// </summary>
public sealed class JobCatalog
{
    private readonly Dictionary<string, IJob> _byKey;

    /// <summary>Builds the catalog.</summary>
    /// <exception cref="InvalidOperationException">A key is malformed or used twice, or a name is blank.</exception>
    public JobCatalog(IEnumerable<IJob> jobs)
    {
        ArgumentNullException.ThrowIfNull(jobs);
        Jobs = [.. jobs.OrderBy(job => job.Key, StringComparer.Ordinal)];
        _byKey = new Dictionary<string, IJob>(StringComparer.Ordinal);
        foreach (var job in Jobs)
        {
            Admit(job);
        }
    }

    /// <summary>Every job, ordered by key.</summary>
    public IReadOnlyList<IJob> Jobs { get; }

    /// <summary>Every key, ordered.</summary>
    public IReadOnlyList<string> Keys => [.. Jobs.Select(job => job.Key)];

    /// <summary>The job with <paramref name="jobKey"/> (exact, case-sensitive), or null.</summary>
    public IJob? Find(string? jobKey) => jobKey is not null && _byKey.TryGetValue(jobKey, out var job) ? job : null;

    private void Admit(IJob job)
    {
        if (!JobKeyFormat.IsValid(job.Key))
        {
            throw new InvalidOperationException($"Job {job.GetType().Name} has a malformed key; keys follow JobKeyFormat.");
        }

        if (string.IsNullOrWhiteSpace(job.NameAr) || string.IsNullOrWhiteSpace(job.NameEn))
        {
            throw new InvalidOperationException($"Job {job.Key} needs an Arabic and an English name.");
        }

        if (job.Cron is { } cron && string.IsNullOrWhiteSpace(cron))
        {
            throw new InvalidOperationException($"Job {job.Key} has a blank schedule; use null for a manual-only job.");
        }

        if (!_byKey.TryAdd(job.Key, job))
        {
            throw new InvalidOperationException(
                $"Job key {job.Key} is registered by {_byKey[job.Key].GetType().Name} and {job.GetType().Name}.");
        }
    }
}
