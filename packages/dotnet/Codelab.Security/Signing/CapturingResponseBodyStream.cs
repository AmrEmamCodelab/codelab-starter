namespace Codelab.Security.Signing;

/// <summary>
/// Wraps the response body for the response signer. On the first write it decides: a response to a verified request
/// (a <see cref="SignedRequestBinding"/> exists) is buffered so it can be hashed before the headers go out; any other
/// response streams straight through and is never buffered, and so does the response of an
/// <see cref="UnsignedResponseAttribute"/> endpoint. While it buffers it is seekable, so <c>HttpResponse.Clear()</c>
/// (the exception handler calls it) drops what the endpoint wrote before it failed, as it would on an unbuffered body
/// that has not started.
/// </summary>
internal sealed class CapturingResponseBodyStream(HttpContext context, Stream inner) : Stream
{
    private MemoryStream? _buffer;
    private bool _decided;
    private bool _uncovered;

    public bool IsCovering => !_uncovered;

    public int CapturedLength => _buffer is null ? 0 : (int)_buffer.Length;

    public override bool CanRead => false;

    public override bool CanSeek => _buffer is not null;

    public override bool CanWrite => true;

    public override long Length => Buffered.Length;

    public override long Position
    {
        get => Buffered.Position;
        set => Buffered.Position = value;
    }

    private MemoryStream Buffered => _buffer ?? throw new NotSupportedException("The response body is not buffered.");

    /// <summary>True when the request's endpoint opted out of body coverage with <see cref="UnsignedResponseAttribute"/>.</summary>
    public static bool IsUnsignedResponse(HttpContext context) =>
        context.GetEndpoint()?.Metadata.GetMetadata<UnsignedResponseAttribute>() is not null;

    public string HashCapturedBody() => SignatureCanonicalForm.HashBody(CapturedBytes);

    public ValueTask CopyCapturedToAsync(Stream destination, CancellationToken cancellationToken) =>
        _buffer is null || _buffer.Length == 0
            ? ValueTask.CompletedTask
            : destination.WriteAsync(_buffer.GetBuffer().AsMemory(0, (int)_buffer.Length), cancellationToken);

    public override void Write(byte[] buffer, int offset, int count) => Write(new ReadOnlySpan<byte>(buffer, offset, count));

    public override void Write(ReadOnlySpan<byte> buffer)
    {
        Decide();
        if (_buffer is null)
        {
            inner.Write(buffer);
        }
        else
        {
            _buffer.Write(buffer);
        }
    }

    public override Task WriteAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken) =>
        WriteAsync(new ReadOnlyMemory<byte>(buffer, offset, count), cancellationToken).AsTask();

    public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Decide();
        if (_buffer is null)
        {
            return inner.WriteAsync(buffer, cancellationToken);
        }

        _buffer.Write(buffer.Span);
        return ValueTask.CompletedTask;
    }

    public override void Flush()
    {
        Decide();
        if (_buffer is null)
        {
            inner.Flush();
        }
    }

    public override Task FlushAsync(CancellationToken cancellationToken)
    {
        Decide();
        return _buffer is null ? inner.FlushAsync(cancellationToken) : Task.CompletedTask;
    }

    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    public override long Seek(long offset, SeekOrigin origin) => Buffered.Seek(offset, origin);

    public override void SetLength(long value) => Buffered.SetLength(value);

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _buffer?.Dispose();
        }

        base.Dispose(disposing);
    }

    private ReadOnlySpan<byte> CapturedBytes => _buffer is null ? default : _buffer.GetBuffer().AsSpan(0, (int)_buffer.Length);

    private void Decide()
    {
        if (_decided)
        {
            return;
        }

        _decided = true;
        if (SignedRequestBinding.Find(context) is null || IsUnsignedResponse(context))
        {
            _uncovered = true;
        }
        else
        {
            _buffer = new MemoryStream();
        }
    }
}
