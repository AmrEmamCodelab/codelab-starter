using Starter.Application.Records;
using Starter.Domain.Records;

namespace Starter.Modules.Records.Application;

/// <summary>The kernel <see cref="IRecordVisibility"/>: the <see cref="RecordLoader"/> gate, so a record-type module sees exactly what the record endpoints show.</summary>
public sealed class RecordVisibility(RecordLoader loader) : IRecordVisibility
{
    /// <inheritdoc />
    public async Task<bool> CanViewAsync(RecordRef record, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(record);
        return (await loader.LoadAsync(record, cancellationToken)).IsSuccess;
    }
}
