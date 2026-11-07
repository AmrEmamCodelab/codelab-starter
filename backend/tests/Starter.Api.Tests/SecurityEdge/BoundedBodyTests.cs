using Starter.Infrastructure.Security;

namespace Starter.Api.Tests.SecurityEdge;

public sealed class BoundedBodyTests
{
    private const int Limit = 10;

    [Theory]
    [InlineData(0)]
    [InlineData(5)]
    [InlineData(Limit)]
    public async Task A_body_within_the_limit_is_read_whole(int length)
    {
        var bytes = Enumerable.Range(0, length).Select(index => (byte)index).ToArray();

        var read = await BoundedBody.ReadAsync(new TrickleStream(bytes), Limit, CancellationToken.None);

        Assert.Equal(bytes, read!.Value.ToArray());
    }

    [Fact]
    public async Task A_body_over_the_limit_is_null_and_read_no_further_than_one_byte_past_it()
    {
        var stream = new TrickleStream(new byte[Limit * 5]);

        var read = await BoundedBody.ReadAsync(stream, Limit, CancellationToken.None);

        Assert.Null(read);
        Assert.Equal(Limit + 1, stream.Position);
    }

    /// <summary>A stream that hands out at most three bytes per read, as a network body does.</summary>
    private sealed class TrickleStream(byte[] bytes) : MemoryStream(bytes)
    {
        private const int MaxChunk = 3;

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default) =>
            base.ReadAsync(buffer[..Math.Min(buffer.Length, MaxChunk)], cancellationToken);
    }
}
