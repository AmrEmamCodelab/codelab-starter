namespace Starter.Modules.Users.Infrastructure.Import;

/// <summary>A workbook broke one of the import caps while it was being read (the zip-bomb guard).</summary>
public sealed class SheetLimitExceededException : Exception
{
    /// <summary>Creates the exception.</summary>
    public SheetLimitExceededException()
        : base("The workbook exceeds an import cap.")
    {
    }

    /// <summary>Creates the exception with a log message (never cell contents).</summary>
    public SheetLimitExceededException(string message)
        : base(message)
    {
    }

    /// <summary>Creates the exception with a log message and its cause.</summary>
    public SheetLimitExceededException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

/// <summary>
/// The uncompressed bytes one workbook read may still consume, shared by every part it opens. The declared entry sizes
/// of a zip are not trusted: the budget counts the bytes actually inflated.
/// </summary>
/// <param name="limit">The most bytes all parts together may inflate to.</param>
public sealed class ReadBudget(long limit)
{
    private long _used;

    /// <summary>Counts <paramref name="bytes"/> read.</summary>
    /// <exception cref="SheetLimitExceededException">The total passed the limit.</exception>
    public void Spend(int bytes)
    {
        _used += bytes;
        if (_used > limit)
        {
            throw new SheetLimitExceededException("The workbook inflates past the uncompressed-bytes cap.");
        }
    }
}

/// <summary>A read-only stream over a zip entry that charges every byte to a <see cref="ReadBudget"/>.</summary>
/// <param name="inner">The entry stream (owned: disposed with this stream).</param>
/// <param name="budget">The shared budget.</param>
public sealed class BudgetedStream(Stream inner, ReadBudget budget) : Stream
{
    /// <inheritdoc />
    public override bool CanRead => true;

    /// <inheritdoc />
    public override bool CanSeek => false;

    /// <inheritdoc />
    public override bool CanWrite => false;

    /// <inheritdoc />
    public override long Length => throw new NotSupportedException();

    /// <inheritdoc />
    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    /// <inheritdoc />
    public override int Read(byte[] buffer, int offset, int count)
    {
        var read = inner.Read(buffer, offset, count);
        budget.Spend(read);
        return read;
    }

    /// <inheritdoc />
    public override void Flush()
    {
    }

    /// <inheritdoc />
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void SetLength(long value) => throw new NotSupportedException();

    /// <inheritdoc />
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    /// <inheritdoc />
    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
        }

        base.Dispose(disposing);
    }
}
