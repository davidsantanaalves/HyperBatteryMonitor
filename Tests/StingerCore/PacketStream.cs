using System.Threading.Channels;

internal sealed class PacketStream : Stream
{
    private readonly Channel<object> _packets = Channel.CreateUnbounded<object>();
    public bool IgnoreCancellationAndDisposal { get; init; }
    public bool ThrowOnDispose { get; init; }
    public bool Disposed { get; private set; }
    public Action? BeforeDispose { get; init; }
    public Action? OnCancellation { get; init; }
    public int DisposeCount;
    public TaskCompletionSource Reading { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public void Packet(params byte[] bytes) => _packets.Writer.TryWrite(bytes);
    public void Fail(Exception error) => _packets.Writer.TryWrite(error);
    public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
    {
        Reading.TrySetResult();
        using var registration = OnCancellation == null ? default : cancellationToken.Register(OnCancellation);
        object next = await _packets.Reader.ReadAsync(IgnoreCancellationAndDisposal ? CancellationToken.None : cancellationToken);
        if (next is Exception error) throw error;
        byte[] packet = (byte[])next;
        packet.CopyTo(buffer);
        return packet.Length;
    }
    protected override void Dispose(bool disposing)
    {
        Interlocked.Increment(ref DisposeCount);
        BeforeDispose?.Invoke();
        Disposed = true;
        if (!IgnoreCancellationAndDisposal) _packets.Writer.TryWrite(new ObjectDisposedException(nameof(PacketStream)));
        if (ThrowOnDispose) throw new IOException("Scripted disposal failure");
        base.Dispose(disposing);
    }
    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;
    public override long Length => throw new NotSupportedException();
    public override long Position { get => throw new NotSupportedException(); set => throw new NotSupportedException(); }
    public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    public override void Flush() => throw new NotSupportedException();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
}
