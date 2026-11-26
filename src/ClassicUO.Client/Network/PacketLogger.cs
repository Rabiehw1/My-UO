using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using ClassicUO.Network.PacketHandlers;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Network;

/// <summary>Formats and writes packet dumps off the game and socket threads.</summary>
internal sealed class PacketLogger
{
    public static PacketLogger Default { get; } = new(flushOnProcessExit: true);

    // An empty list logs all packets with credentials redacted. Explicit IDs are set at startup.
    public readonly List<byte> LogPacketId = [];

    private const int QueueCapacity = 256;
    private readonly object _fileLock = new();
    private LogFile _logFile;
    private readonly Channel<PacketLogEntry> _entries = Channel.CreateBounded<PacketLogEntry>(new BoundedChannelOptions(QueueCapacity)
    {
        SingleReader = true,
        FullMode = BoundedChannelFullMode.Wait
    });
    private readonly Lazy<Task> _writer;
    private int _dropped;
    private volatile bool _enabled;

    private readonly Lazy<Dictionary<uint, string>> _packetNames = new(() =>
    {
        var names = new Dictionary<uint, string>();
        foreach ((uint id, PacketHandler _, string name) in PacketHandlerRegistry.GetHandlers())
            names.TryAdd(id, name);
        return names;
    });

    internal PacketLogger(bool flushOnProcessExit = false)
    {
        _writer = new(() => Task.Run(WriteEntries));
        if (flushOnProcessExit)
            AppDomain.CurrentDomain.ProcessExit += (_, _) => FlushOnExit();
    }

    public bool Enabled { get => _enabled; set => _enabled = value; }

    public bool ShouldLog(byte id) => Enabled && (LogPacketId.Count == 0 || LogPacketId.Contains(id));

    public void CreateFile(string directory = null)
    {
        lock (_fileLock)
        {
            try
            {
                _logFile?.Dispose();
                directory ??= FileSystemHelper.CreateFolderIfNotExists(CUOEnviroment.ExecutablePath, "Logs", "Network");
                _logFile = new LogFile(directory, "packets.log");
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                _logFile = null;
                Utility.Logging.Log.Warn($"Could not open packet log: {ex.Message}");
            }
        }
    }

    public void Log(ReadOnlySpan<byte> message, bool toServer, double? processingTimeMs = null)
    {
        if (message.IsEmpty || !ShouldLog(message[0]))
            return;

        byte id = message[0];
        bool hideCredentials = id is 0x80 or 0x91 && !LogPacketId.Contains(id);
        if (_entries.Reader.Count >= QueueCapacity || !_entries.Writer.TryWrite(new PacketLogEntry(id, message.Length,
            hideCredentials ? null : message.ToArray(), toServer, processingTimeMs, DateTime.UtcNow, Environment.CurrentManagedThreadId)))
        {
            Interlocked.Increment(ref _dropped);
            return;
        }

        _ = _writer.Value;
    }

    internal Task CompleteAsync()
    {
        _entries.Writer.TryComplete();
        return _writer.Value;
    }

    private void FlushOnExit()
    {
        if (!_writer.IsValueCreated && !Enabled)
            return;

        try
        {
            CompleteAsync().Wait(TimeSpan.FromSeconds(2));
        }
        catch (AggregateException)
        {
            // Diagnostic output must not interfere with process shutdown.
        }
    }

    private async Task WriteEntries()
    {
        try
        {
            await foreach (PacketLogEntry entry in _entries.Reader.ReadAllAsync().ConfigureAwait(false))
            {
                ReportSkippedEntries();
                WriteLine(FormatEntry(entry));
            }

            ReportSkippedEntries();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ObjectDisposedException)
        {
            Enabled = false;
            _entries.Writer.TryComplete();
            while (_entries.Reader.TryRead(out _)) { }
            Utility.Logging.Log.Warn($"Packet logging stopped: {ex.Message}");
        }
        finally
        {
            lock (_fileLock)
            {
                _logFile?.Dispose();
                _logFile = null;
            }
        }
    }

    private void ReportSkippedEntries()
    {
        int dropped = Interlocked.Exchange(ref _dropped, 0);
        if (dropped > 0)
            WriteLine($"Skipped {dropped} packet log entries while the logging queue was full.");
    }

    private void WriteLine(string text)
    {
        lock (_fileLock)
        {
            if (_logFile != null)
                _logFile.Write(text);
            else
                Console.WriteLine(text);
        }
    }

    private string FormatEntry(PacketLogEntry entry)
    {
        const int AddressWidth = sizeof(ulong) + 2;

        Span<char> span = stackalloc char[256];
        using var output = new ValueStringBuilder(span);
        output.Append(FormatHeader(entry));
        output.Append(' ', AddressWidth);

        if (entry.Bytes == null)
        {
            output.Append("[ACCOUNT CREDENTIALS HIDDEN]\n");
        }
        else
        {
            ReadOnlySpan<byte> message = entry.Bytes;
            output.Append("0  1  2  3  4  5  6  7   8  9  A  B  C  D  E  F\n");
            output.Append(' ', AddressWidth);
            output.Append("-- -- -- -- -- -- -- --  -- -- -- -- -- -- -- --\n");

            for (int offset = 0; offset < message.Length; offset += 16)
            {
                output.Append($"{offset:X8}");

                for (int column = 0; column < 16; column++)
                {
                    if (column % 8 == 0)
                        output.Append(' ');

                    output.Append(offset + column < message.Length ? $" {message[offset + column]:X2}" : "   ");
                }

                output.Append("  ");

                foreach (byte character in message.Slice(offset, Math.Min(16, message.Length - offset)))
                    output.Append(character is >= 0x20 and < 0x80 ? (char)character : '.');

                output.Append('\n');
            }
        }

        output.Append("\n\n");
        return output.ToString();
    }

    private string FormatHeader(PacketLogEntry entry)
    {
        string timestamp = entry.Timestamp.ToString("dd/MM/yyyy HH:mm:ss.fff", CultureInfo.InvariantCulture);
        string direction = entry.ToServer ? "Client -> Server" : "Server -> Client";
        string processingTime = entry.ToServer || !entry.ProcessingTimeMs.HasValue
            ? "" : $"Processing time: {entry.ProcessingTimeMs.Value.ToString("F3", CultureInfo.InvariantCulture)}ms\n";

        // The registry provides inbound names only. Some outbound IDs share an unrelated inbound ID.
        string packetId = $"0x{entry.Id:X2}";
        string name = !entry.ToServer && _packetNames.Value.TryGetValue(entry.Id, out string handler)
            ? $"{handler} ({packetId})" : packetId;

        return $"Thread: {entry.ThreadId}\nTime (UTC): {timestamp}\nDirection: {direction}\nID: {name}\nLength: {entry.Length}\n{processingTime}\n";
    }

    private sealed record PacketLogEntry(byte Id, int Length, byte[] Bytes, bool ToServer, double? ProcessingTimeMs,
        DateTime Timestamp, int ThreadId);
}
