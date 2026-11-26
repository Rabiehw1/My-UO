using ClassicUO.Network.Encryption;
using ClassicUO.Utility.Logging;
using System;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using System.Collections.Concurrent;
using System.Data;
using System.IO;
using System.Buffers;
using ClassicUO.Utility;
using SDL3;

namespace ClassicUO.Network
{
    sealed class AsyncSocketWrapper(AsyncNetClient client) : IDisposable
    {
        private TcpClient _socket;
        private NetworkStream _stream;
        private readonly CancellationTokenSource _shutdown = new();
        private Task _receiveTask;
        private int _closed;
        public bool IsConnected => Volatile.Read(ref _closed) == 0 && _socket?.Connected == true;
        public EndPoint LocalEndPoint => _socket?.Client?.LocalEndPoint;
        public EndPoint RemoteEndPoint => _socket?.Client?.RemoteEndPoint;
        public Task ReceiveCompletion => _receiveTask ?? Task.CompletedTask;

        public event EventHandler OnDisconnected;
        public event EventHandler<SocketError> OnError;

        public async Task<SocketError> ConnectAsync(string ip, int port, CancellationToken cancellationToken)
        {
            if (IsConnected)
                return SocketError.Success;

            using var deadline = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _shutdown.Token);
            deadline.CancelAfter(TimeSpan.FromSeconds(2));

            try
            {
                deadline.Token.ThrowIfCancellationRequested();
                TcpClient socket = _socket = new TcpClient { NoDelay = true };
                await socket.ConnectAsync(ip, port, deadline.Token).ConfigureAwait(false);
                deadline.Token.ThrowIfCancellationRequested();
                _stream = socket.GetStream();
                _receiveTask = ReceiveLoopAsync(_shutdown.Token);
                return SocketError.Success;
            }
            catch (OperationCanceledException)
            {
                _socket?.Dispose();
                return cancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested
                    ? SocketError.OperationAborted
                    : SocketError.TimedOut;
            }
            catch (SocketException socketEx)
            {
                _socket?.Dispose();
                Log.Error($"Error while connecting {socketEx}");
                return socketEx.SocketErrorCode;
            }
            catch (Exception ex)
            {
                _socket?.Dispose();
                Log.Error($"Error while connecting {ex}");
                return SocketError.SocketError;
            }
        }

        public async Task SendAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken = default)
        {
            if (!IsConnected || _stream == null)
                return;

            try
            {
                await _stream.WriteAsync(buffer.AsMemory(offset, count), cancellationToken).ConfigureAwait(false);
                await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested || _shutdown.IsCancellationRequested)
            {
            }
            catch (Exception ex)
            {
                if (Close())
                {
                    Log.Error($"Error while sending {ex}");
                    OnError?.Invoke(this, SocketError.SocketError);
                }
            }
        }

        private async Task ReceiveLoopAsync(CancellationToken cancellationToken)
        {
            byte[] buffer = ArrayPool<byte>.Shared.Rent(4096);

            try
            {
                while (!cancellationToken.IsCancellationRequested && IsConnected)
                {
                    // Read directly instead of gating on Available. A blocking ReadAsync returns 0
                    // when the remote closes the connection (FIN); gating on Available > 0 meant a
                    // graceful/half-open close was never detected, leaving the socket "connected"
                    // forever. ReadAsync also yields naturally, so no busy-wait delay is needed.
                    int bytesRead = await _stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);

                    if (bytesRead == 0)
                    {
                        if (Close())
                            OnDisconnected?.Invoke(this, EventArgs.Empty);

                        break;
                    }

                    if (!cancellationToken.IsCancellationRequested)
                    {
                        byte[] data = new byte[bytesRead];
                        Array.Copy(buffer, data, bytesRead);
                        client.OnDataReceived(data);
                    }
                }
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested)
            {
            }
            catch (IOException ioEx) when (ioEx.InnerException is SocketException socketEx)
            {
                if (Close())
                {
                    Log.Error($"Socket error in receive loop: {socketEx.SocketErrorCode} - {socketEx.Message}");
                    OnError?.Invoke(this, socketEx.SocketErrorCode);
                }
            }
            catch (Exception ex)
            {
                if (Close())
                {
                    Log.Error($"Error in receive loop {ex}");
                    OnError?.Invoke(this, SocketError.SocketError);
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }
        }

        // Either loop may be the first to detect closure. Closing the socket must not join either loop.
        private bool Close()
        {
            if (Interlocked.Exchange(ref _closed, 1) != 0)
                return false;

            _shutdown.Cancel();
            _socket?.Dispose();
            return true;
        }

        public void Dispose() => Close();
    }

    public sealed class AsyncNetClient : IDisposable
    {
        private const int BUFF_SIZE = 0x10000;

        private readonly byte[] _compressedBuffer = new byte[4096];
        private readonly byte[] _uncompressedBuffer = new byte[BUFF_SIZE];
        private readonly Huffman _huffman = new Huffman();
        private bool _isCompressionEnabled;
        private readonly AsyncSocketWrapper _socket;
        private uint? _localIP;

        // Outgoing packets are queued as owned, pooled buffers and drained by SendLoopAsync.
        // A ConcurrentQueue (rather than a ring/lock) lets Send run from any thread while the
        // loop sends, and lets ClearSendQueue run safely from the connection control paths.
        private readonly ConcurrentQueue<OutgoingPacket> _sendQueue = new();

        // Data-available signal for _sendQueue, capped at 1. Send releases it once per enqueue
        // only when it is not already set, so the loop wakes on the first packet of a burst and
        // then drains everything queued. Extra releases are swallowed by the max count.
        private readonly SemaphoreSlim _sendSignal = new(0, 1);

        private readonly ConcurrentQueue<byte[]> _incomingMessages = new();
        private Task _networkTask;
        private readonly CancellationTokenSource _cancellationTokenSource = new();
        private readonly object _connectionLock = new();
        private Task _disconnectTask;
        public static PacketsTable PacketsTable { get; private set; }
#nullable enable
        public static EncryptionHelper? Encryption { get; private set; }
#nullable disable
        public static AsyncNetClient Socket { get; set; } = new AsyncNetClient();
        public bool IsConnected => _socket != null && _socket.IsConnected;
        public NetStatistics Statistics { get; }

        /// <summary>
        ///     Remote endpoint of the active game connection, or <c>null</c> when disconnected.
        ///     Needed to derive the IP for ICMP fallback pings, since the 0x73 packet ping may be
        ///     unanswered by some servers.
        /// </summary>
        public EndPoint RemoteEndPoint => _socket?.RemoteEndPoint;

        public AsyncNetClient()
        {
            Statistics = new NetStatistics(this);

            _socket = new AsyncSocketWrapper(this);

            _socket.OnDisconnected += (o, e) => Disconnected?.Invoke(this, SocketError.Success);
            _socket.OnError += (o, e) => Disconnected?.Invoke(this, e);
            //_socket.OnDataReceived += OnDataReceived;
        }

        public static EncryptionType Load(ClientVersion clientVersion, EncryptionType encryption)
        {
            PacketsTable = new PacketsTable(clientVersion);

            if (encryption != 0)
            {
                Encryption = new EncryptionHelper(clientVersion);
                Log.Trace("Calculating encryption by client version...");
                Log.Trace($"encryption: {Encryption.EncryptionType}");

                if (Encryption.EncryptionType != encryption)
                {
                    Log.Warn($"Encryption found: {Encryption.EncryptionType}");
                    encryption = Encryption.EncryptionType;
                }
            }

            return encryption;
        }

        public uint LocalIP
        {
            get
            {
                if (!_localIP.HasValue)
                {
                    try
                    {
                        byte[] addressBytes = (_socket?.LocalEndPoint as IPEndPoint)?.Address.MapToIPv4().GetAddressBytes();

                        if (addressBytes != null && addressBytes.Length != 0)
                        {
                            _localIP = (uint)(addressBytes[0] | (addressBytes[1] << 8) | (addressBytes[2] << 16) | (addressBytes[3] << 24));
                        }

                        if (!_localIP.HasValue || _localIP == 0)
                        {
                            _localIP = 0x100007f;
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error($"error while retrieving local endpoint address: \n{ex}");
                        _localIP = 0x100007f;
                    }
                }

                return _localIP.Value;
            }
        }

        public event EventHandler Connected;
        public event EventHandler<SocketError> Disconnected;

        public async Task<bool> Connect(string ip, ushort port, CancellationToken cancellationToken = default)
        {
            ClearSendQueue();
            _huffman.Reset();
            Statistics.Reset();

            SocketError result = await _socket.ConnectAsync(ip, port, cancellationToken).ConfigureAwait(false);
            if (_cancellationTokenSource.IsCancellationRequested || cancellationToken.IsCancellationRequested)
                return false;

            if (result != SocketError.Success)
            {
                Disconnected?.Invoke(this, result);
                return false;
            }

            lock (_connectionLock)
            {
                if (_disconnectTask != null || !IsConnected)
                    return false;

                _networkTask = SendLoopAsync(_cancellationTokenSource.Token);
            }

            Connected?.Invoke(this, EventArgs.Empty);
            return true;
        }

        public Task Disconnect()
        {
            lock (_connectionLock)
                return _disconnectTask ??= DisconnectCore();
        }

        private async Task DisconnectCore()
        {
            SDL.SDL_CaptureMouse(false);
            _cancellationTokenSource.Cancel();
            _socket.Dispose();

            try
            {
                await Task.WhenAll(_networkTask ?? Task.CompletedTask, _socket.ReceiveCompletion).ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                Log.Error($"Error closing network connection {ex}");
            }

            ClearIncomingMessages();
            _isCompressionEnabled = false;
            Statistics.Reset();
            _huffman.Reset();
            ClearSendQueue();
        }

        public void EnableCompression()
        {
            _isCompressionEnabled = true;
            _huffman.Reset();
            ClearSendQueue();
        }

        /// <summary>
        ///     Drains the outgoing queue until cancelled or disconnected. Blocks on
        ///     <see cref="_sendSignal"/> instead of polling, so a queued packet is written
        ///     as soon as it is enqueued rather than on the next timer tick.
        /// </summary>
        private async Task SendLoopAsync(CancellationToken cancellationToken)
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                try
                {
                    await _sendSignal.WaitAsync(cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                // Drain every packet queued up to this point before waiting again.
                while (_sendQueue.TryDequeue(out OutgoingPacket packet))
                {
                    try
                    {
                        await _socket.SendAsync(packet.Buffer, 0, packet.Length, cancellationToken);
                    }
                    finally
                    {
                        ArrayPool<byte>.Shared.Return(packet.Buffer);
                    }
                }
            }
        }

        /// <summary>
        ///     Discards every queued outgoing packet, returning its pooled buffer. Safe to call
        ///     while <see cref="SendLoopAsync"/> is running: <see cref="ConcurrentQueue{T}"/> hands
        ///     each item to exactly one reader.
        /// </summary>
        private void ClearSendQueue()
        {
            while (_sendQueue.TryDequeue(out OutgoingPacket packet))
            {
                ArrayPool<byte>.Shared.Return(packet.Buffer);
            }
        }

        public void OnDataReceived(byte[] data)
        {
            try
            {
                Statistics.TotalBytesReceived += (uint)data.Length;

                Span<byte> span = data.AsSpan();
                ProcessEncryption(span);
                Span<byte> decompressed = DecompressBuffer(span);

                if (!decompressed.IsEmpty)
                {
                    byte[] message = decompressed.ToArray();
                    _incomingMessages.Enqueue(message);
                }
            }
            catch (Exception ex)
            {
                Log.Error($"Error processing received data: {ex}");
            }
        }

        public bool TryDequeuePacket(out byte[] packet) => _incomingMessages.TryDequeue(out packet);

        public bool HasPendingPackets => !_incomingMessages.IsEmpty;

        public void ClearIncomingMessages()
        {
            while (_incomingMessages.TryDequeue(out _))
            {
            }
        }

        public void Send(Span<byte> message, bool ignorePlugin = false, bool skipEncryption = false)
        {
            if (!IsConnected || message is [])
            {
                return;
            }

            if (!ignorePlugin && Plugin.Enabled && !Plugin.ProcessSendPacket(ref message))
            {
                return;
            }

            if (message.IsEmpty)
                return;

            PacketLogger.Default?.Log(message, true);

            if (!skipEncryption)
            {
                EncryptionHelper.Instance?.Encrypt(!_isCompressionEnabled, message, message, message.Length);
            }

            // The caller's span is transient (often a reused StackDataWriter buffer), so copy the
            // packet into an owned pooled buffer before it is queued. There is deliberately no fixed
            // 4 KiB chunk here: that cap dated to the old synchronous send path and may have existed
            // for server-side framing reasons. If a server turns out to require it, cap the bytes
            // written per _socket.SendAsync inside SendLoopAsync and re-add the split here.
            byte[] buffer = ArrayPool<byte>.Shared.Rent(message.Length);
            message.CopyTo(buffer);
            _sendQueue.Enqueue(new OutgoingPacket(buffer, message.Length));

            // Wake the send loop if it is idle. The signal is capped at 1, so only the first packet
            // of a burst releases it; the drain loop picks up the rest. The count check keeps the
            // common "already awake" path exception-free, with the catch covering the race.
            if (_sendSignal.CurrentCount == 0)
            {
                try
                {
                    _sendSignal.Release();
                }
                catch (SemaphoreFullException)
                {
                }
            }

            Statistics.TotalBytesSent += (uint)message.Length;
            Statistics.TotalPacketsSent++;
        }

        private void ProcessEncryption(Span<byte> buffer)
        {
            if (!_isCompressionEnabled)
                return;

            EncryptionHelper.Instance?.Decrypt(buffer, buffer, buffer.Length);
        }

        private Span<byte> DecompressBuffer(Span<byte> buffer)
        {
            if (!_isCompressionEnabled)
                return buffer;

            int size = 65536;

            if (!_huffman.Decompress(buffer, _uncompressedBuffer, ref size))
            {
                _ = Disconnect();
                Disconnected?.Invoke(this, SocketError.SocketError);

                return Span<byte>.Empty;
            }

            return _uncompressedBuffer.AsSpan(0, size);
        }

        public void Dispose() => _ = Disconnect();

        /// <summary>
        ///     One queued outgoing packet: a pooled buffer plus the number of valid bytes in it.
        ///     Ownership of <see cref="Buffer"/> passes to <see cref="_sendQueue"/> on enqueue and is
        ///     returned to <see cref="ArrayPool{T}.Shared"/> once sent or discarded.
        /// </summary>
        private readonly struct OutgoingPacket(byte[] buffer, int length)
        {
            public readonly byte[] Buffer = buffer;
            public readonly int Length = length;
        }
    }
}
