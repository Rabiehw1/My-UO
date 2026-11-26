using System;
using System.IO;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using ClassicUO.Game.Scenes;
using ClassicUO.Network;
using Xunit;

namespace ClassicUO.UnitTests.Network;

public class ConnectionRecoveryTests
{
    [Theory]
    [InlineData(LoginSteps.Connecting, true)]
    [InlineData(LoginSteps.VerifyingAccount, true)]
    [InlineData(LoginSteps.LoginInToServer, true)]
    [InlineData(LoginSteps.EnteringBritania, true)]
    [InlineData(LoginSteps.CharacterCreationDone, true)]
    [InlineData(LoginSteps.ServerSelection, false)]
    [InlineData(LoginSteps.CharacterSelection, false)]
    public void WatchdogCoversEveryServerWaitWithoutTimingOutPlayerMenus(LoginSteps step, bool expected)
    {
        Assert.Equal(expected, LoginHandshake.GetHandshakeTimeout(step) > 0);
    }

    [Fact]
    public void DelayedPingReplyUsesItsProbeTimestampAndLeavesLaterProbeAvailable()
    {
        var statistics = new NetStatistics(null);
        byte first = statistics.TrackPacketPing(100);
        byte second = statistics.TrackPacketPing(1100);

        statistics.ReceivePacketPing(first, 1350);
        Assert.Equal(1250u, statistics.LastPing);

        statistics.ReceivePacketPing(first, 1400);
        Assert.Equal(1250u, statistics.LastPing);

        statistics.ReceivePacketPing(second, 1500);
        Assert.Equal(400u, statistics.LastPing);
    }

    [Fact]
    public async Task ReceiveLoopFinishesPromptlyWhenPeerCloses()
    {
        using var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        using var socket = new AsyncSocketWrapper(null);
        int disconnects = 0;
        socket.OnDisconnected += (_, _) => Interlocked.Increment(ref disconnects);

        int port = ((IPEndPoint)listener.LocalEndpoint).Port;
        Assert.Equal(SocketError.Success, await socket.ConnectAsync("127.0.0.1", port, CancellationToken.None));
        using TcpClient peer = await listener.AcceptTcpClientAsync();
        peer.Close();

        await socket.ReceiveCompletion.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(1, disconnects);
        Assert.False(socket.IsConnected);
    }

    [Fact]
    public async Task PacketLogCopiesReusedBuffersAndRedactsCredentials()
    {
        DirectoryInfo folder = Directory.CreateTempSubdirectory();
        try
        {
            var logger = new PacketLogger { Enabled = true };
            logger.CreateFile(folder.FullName);

            byte[] packet = [0x73, 0x41];
            logger.Log(packet, toServer: true);
            packet[1] = 0x42;
            logger.Log(new byte[] { 0x80, 0x61, 0x62, 0x63 }, toServer: true);
            await logger.CompleteAsync();

            string output = File.ReadAllText(Assert.Single(folder.GetFiles()).FullName);
            Assert.Contains("73 41", output);
            Assert.DoesNotContain("73 42", output);
            Assert.Contains("[ACCOUNT CREDENTIALS HIDDEN]", output);
            Assert.DoesNotContain("80 61 62 63", output);
        }
        finally
        {
            folder.Delete(recursive: true);
        }
    }
}
