using System;
using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Threading.Tasks;
using ClassicUO.Configuration;
using ClassicUO.Game;
using ClassicUO.Game.Managers;
using ClassicUO.Game.Scenes;
using ClassicUO.IO;
using ClassicUO.Network.Encryption;
using ClassicUO.Utility;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Network
{
    public class LoginHandshake : IDisposable
    {
        /// <summary>
        /// LoginHandshake supports only one connection. Trying to connect multiple times in one client would need a rework.
        /// </summary>
        public static LoginHandshake Instance {
            get
            {
                if (field == null)
                    field = new();

                return field;
            }

            private set;
        }

        // How long (ms) to wait on the server during a handshake stage before giving up.
        // A reconnect that lands while the server is still starting up can complete the TCP
        // connect but never receive a reply, leaving us pinned in VerifyingAccount with no way
        // out. This watchdog forces a fallback so the reconnect loop can retry.
        private const int HandshakeTimeoutMs = 8_000;
        private const int WorldEntryTimeoutMs = 20_000;
        private const int MaxRelayAttempts = 6;
        private int _reconnectTryCounter = 1;
        private long _reconnectTime;
        private long _handshakeTimeout;
        private bool _isDisposed;
        private uint _pingTime;
        private RelayConnection _relayConnection;
        private bool _connectedToRelay;
        private (AsyncNetClient Connection, SocketError Error, long ReceivedAt)? _pendingDisconnect;

        private LoginHandshake() { }

        public LoginSteps CurrentLoginStep { get; private set; } = LoginSteps.Main;
        public ServerListEntry[] Servers { get; private set; }
        public CityInfo[] Cities { get; set; }
        public string[] Characters { get; private set; }
        public byte ServerIndex { get; private set; }

        // When the user steps back from character selection while 'Skip Server Select' is enabled,
        // we need to suppress the auto-skip once so they actually land on the server selection screen
        // instead of being bounced straight back to character selection.
        public bool BypassServerSelectSkipOnce { get; set; }
        public static string Account { get; private set; }
        private static string Password { get; set; }
        public static string IP { get; set; }
        public static ushort Port { get; set; }
        public (int min, int max) LoginDelay { get; private set; }
        public bool ShouldReconnect { get; set; }
        public static bool Reconnect { get; set; }
        public ushort LastServerNum { get; private set; }
        public string LastServerName { get; private set; }
        public byte ErrorPacket { get; private set; } = byte.MaxValue;
        public byte ErrorCode { get; private set; } = byte.MaxValue;
        public string ErrorMessage { get; private set; } = string.Empty;
        public uint CharacterListFlags { get; private set; }

        public event EventHandler<LoginSteps> LoginStepChanged;
        public event EventHandler<SocketError> ConnectionFailed;
        public event EventHandler<string> ErrorOccurred;

        public void Connect(string account, string password, string ip, ushort port)
        {
            if (CurrentLoginStep == LoginSteps.Connecting)
            {
                return;
            }

            Task previousConnectionClosed = StopLoginConnection();
            ClassicUO.Network.PacketHandlers.PacketParser.Instance.ClearBuffers();

            Account = account;
            Password = password;
            IP = ip;
            Port = port;

            Log.TraceDebug($"[HandShake] Start login to: {IP},{Port}");

            if (!Reconnect)
            {
                _reconnectTryCounter = 1;
            }

            SetLoginStep(LoginSteps.Connecting);

            var connection = new AsyncNetClient();
            AsyncNetClient.Socket = connection;
            connection.Connected += OnNetClientConnected;
            connection.Disconnected += OnNetClientDisconnected;
            _ = ConnectAfterClose(previousConnectionClosed, connection, ip, port);
        }

        private static async Task ConnectAfterClose(Task previousConnectionClosed, AsyncNetClient connection, string ip, ushort port)
        {
            await previousConnectionClosed.ConfigureAwait(false);
            await connection.Connect(ip, port).ConfigureAwait(false);
        }

        public void Disconnect()
        {
            Log.TraceDebug("[HandShake] Disconnecting...");
            _ = StopLoginConnection();
        }

        private Task StopLoginConnection()
        {
            _connectedToRelay = false;
            return Task.WhenAll(CancelRelay(), CloseLoginSocket());
        }

        private Task CloseLoginSocket()
        {
            _pendingDisconnect = null;
            AsyncNetClient connection = AsyncNetClient.Socket;
            connection.Connected -= OnNetClientConnected;
            connection.Disconnected -= OnNetClientDisconnected;
            return connection.Disconnect();
        }

        private Task CancelRelay()
        {
            RelayConnection relay = _relayConnection;
            _relayConnection = null;
            return relay == null ? Task.CompletedTask
                : Task.WhenAll(relay.PreviousConnectionClosed, relay.Connection?.Disconnect() ?? Task.CompletedTask);
        }

        private void SetError(byte packet = byte.MaxValue, byte code = byte.MaxValue, string msg = "")
        {
            ErrorPacket = packet;
            ErrorCode = code;
            ErrorMessage = msg;
        }

        public void SelectServer(byte index, string serverName)
        {
            Log.TraceDebug($"[HandShake] Selecting server {serverName}.");
            if (CurrentLoginStep == LoginSteps.ServerSelection)
            {
                for (byte i = 0; i < Servers.Length; i++)
                {
                    if (Servers[i].Index == index)
                    {
                        ServerIndex = i;
                        break;
                    }
                }

                // Persist the previously loaded settings while the old server scope is still active,
                // so switching servers doesn't discard unsaved changes to the previous server/account.
                ProfileManager.SaveServerSettings();
                ProfileManager.SaveAccountSettings();

                World.Instance.ServerName = serverName;
                LastServerNum = (ushort)(1 + ServerIndex);
                LastServerName = Servers[ServerIndex].Name;

                // Server folder is known now, and the account folder nests under it, so both
                // scoped settings can be resolved and loaded at this point.
                ProfileManager.LoadServerSettings();
                ProfileManager.LoadAccountSettings();

                SetLoginStep(LoginSteps.LoginInToServer);

                AsyncNetClient.Socket.Send_SelectServer(index);
            }
        }

        /// <summary>
        /// Call in Update() of login scene
        /// </summary>
        /// <param name="reconnectTime">In ms</param>
        public void HandleReconnect(int reconnectTime)
        {
            // Note: we intentionally don't gate on IsConnected here. On PopUpMessage/Main with
            // Reconnect set we're not in a live session by definition, and Socket.Connected only
            // reflects the last I/O so a timed-out/half-open socket can report a stale 'true' and
            // wedge the retry loop forever. Connect() tears down and replaces the socket anyway,
            // and _reconnectTime enforces the delay between attempts.
            if (Reconnect && (CurrentLoginStep == LoginSteps.PopUpMessage || CurrentLoginStep == LoginSteps.Main))
            {
                if (_reconnectTime >= Time.Ticks)
                    return;

                Log.TraceDebug($"[HandShake] Reconnecting...");
                if (!string.IsNullOrEmpty(Account))
                {
                    Connect(Account, Password, IP, Port);
                }
                else
                {
                    Reconnect = false;
                }

                if (reconnectTime < 1000)
                {
                    reconnectTime = 1000;
                }

                _reconnectTime = (long)Time.Ticks + reconnectTime;
                _reconnectTryCounter++;
            }
        }

        public void ServerListReceived(ref StackDataReader p)
        {
            Log.TraceDebug($"[HandShake] Got server list.");
            byte flags = p.ReadUInt8();
            ushort count = p.ReadUInt16BE();
            DisposeAllServerEntries();
            Servers = new ServerListEntry[count];

            for (ushort i = 0; i < count; i++)
            {
                Servers[i] = ServerListEntry.Create(ref p);
            }

            SetLoginStep(LoginSteps.ServerSelection);

            if (BypassServerSelectSkipOnce)
            {
                // User explicitly navigated back to server selection, don't auto-skip this time.
                BypassServerSelectSkipOnce = false;
            }
            else if (ProfileManager.GlobalSettings.SkipServerSelection && Servers.Length == 1 && CurrentLoginStep == LoginSteps.ServerSelection) //Double check server selection, the previous call may initiate auto login and already select one
            {
                SelectServer((byte)Servers[0].Index, Servers[0].Name);
                return;
            }
        }

        public event NotifierEventHandler ReceiveCharacterListNotifier;
        public void ReceiveCharacterList(ref StackDataReader p)
        {
            Log.TraceDebug($"[HandShake] Got character list.");
            ParseCharacterList(ref p);
            ParseCities(ref p);
            CharacterListFlags = p.ReadUInt32BE();

            SetLoginStep(LoginSteps.CharacterSelection);
            ReceiveCharacterListNotifier?.Invoke();
        }

        public event NotifierEventHandler UpdateCharacterListNotifier;
        public void UpdateCharacterList(ref StackDataReader p)
        {
            Log.TraceDebug($"[HandShake] Updated character list.");
            ParseCharacterList(ref p);

            if (CurrentLoginStep != LoginSteps.PopUpMessage)
            {
                SetError();
            }

            SetLoginStep(LoginSteps.CharacterSelection);
            UpdateCharacterListNotifier?.Invoke();
        }

        public void HandleErrorCode(ref StackDataReader p)
        {
            byte code = p.ReadUInt8();
            SetError(p[0], code);
            SetLoginStep(LoginSteps.PopUpMessage);
            LoginDelay = default;
        }

        public void HandleLoginDelayPacket(ref StackDataReader p)
        {
            byte delay = p.ReadUInt8();
            LoginDelay = ((delay - 1) * 10, delay * 10);
        }

        public void HandleRelayServerPacket(ref StackDataReader p, bool ignoreRelay)
        {
            if (CurrentLoginStep != LoginSteps.LoginInToServer || _relayConnection != null || _connectedToRelay)
                return;

            Log.TraceDebug($"[HandShake] Got server relay packet.");
            uint ip = p.ReadUInt32LE(); // use LittleEndian here
            ushort port = p.ReadUInt16BE();
            uint seed = p.ReadUInt32BE();

            byte[] ipBytes = new byte[]
            {
                    (byte)(ip & 0xFF),
                    (byte)((ip >> 8) & 0xFF),
                    (byte)((ip >> 16) & 0xFF),
                    (byte)((ip >> 24) & 0xFF)
                    };

            string finalIP = new IPAddress(ipBytes).ToString();

            if (ignoreRelay || ip == 0)
            {
                Log.TraceDebug("Ignoring relay server packet IP address");
                finalIP = IP;
                port = Port;
            }

            _relayConnection = new RelayConnection(finalIP, port, seed, CloseLoginSocket());
            ClassicUO.Network.PacketHandlers.PacketParser.Instance.ClearBuffers();
            ArmHandshakeTimeout(CurrentLoginStep);
        }

        public int GetServerIndexByName(string name)
        {
            if (string.IsNullOrWhiteSpace(name) || Servers == null) return -1;

            for (int i = 0; i < Servers.Length; i++)
                if (Servers[i].Name.Equals(name, StringComparison.InvariantCultureIgnoreCase))
                    return Servers[i].Index;

            return -1;
        }

        public CityInfo GetCity(int index)
        {
            if (Cities != null && index < Cities.Length)
            {
                return Cities[index];
            }

            return null;
        }

        public void SendSelectCharacter(uint index)
        {
            if (Characters != null && index < (uint)Characters.Length)
            {
                AsyncNetClient.Socket.Send_SelectCharacter(index, Characters[index], AsyncNetClient.Socket.LocalIP);
                SetLoginStep(LoginSteps.EnteringBritania);
            }
            else
            {
                Log.Warn($"[HandShake] Invalid character index {index}.");
            }
        }

        public void SendDeleteCharacter(uint index)
        {
            if (CurrentLoginStep == LoginSteps.CharacterSelection)
            {
                AsyncNetClient.Socket.Send_DeleteCharacter((byte)index, AsyncNetClient.Socket.LocalIP);
            }
        }

        public void SendPing()
        {
            if ((CurrentLoginStep == LoginSteps.CharacterCreation || CurrentLoginStep == LoginSteps.CharacterSelection) && Time.Ticks > _pingTime)
            {
                // Note that this will not be an ICMP ping, so it's better that this *not* be affected by -no_server_ping.

                if (AsyncNetClient.Socket.IsConnected)
                {
                    AsyncNetClient.Socket.Statistics.SendPing();
                }

                _pingTime = Time.Ticks + 60000;
            }
        }

        internal void SetLoginStep(LoginSteps step)
        {
            _pingTime = Time.Ticks + 60000;
            ArmHandshakeTimeout(step);

            Log.TraceDebug($"[HandShake] Set login step to {step}.");
            CurrentLoginStep = step;
            LoginStepChanged?.Invoke(this, step);

            // Returning to Main means we've left the account/server context (e.g. stepping back
            // from server or character selection), so persist those scoped settings.
            if (step == LoginSteps.Main)
            {
                ProfileManager.SaveServerSettings();
                ProfileManager.SaveAccountSettings();
            }
        }

        private void ArmHandshakeTimeout(LoginSteps step)
        {
            int timeout = GetHandshakeTimeout(step);
            _handshakeTimeout = timeout > 0 ? (long)Time.Ticks + timeout : 0;
        }

        internal static int GetHandshakeTimeout(LoginSteps step) => step switch
        {
            LoginSteps.Connecting or LoginSteps.VerifyingAccount or LoginSteps.LoginInToServer => HandshakeTimeoutMs,
            LoginSteps.EnteringBritania or LoginSteps.CharacterCreationDone => WorldEntryTimeoutMs,
            _ => 0
        };

        /// <summary>
        /// Call in Update() of the login scene. If a handshake stage stalls (e.g. a reconnect that
        /// connects to a server which is still restarting and never replies), force a disconnect and
        /// fall back to the popup so the reconnect loop can retry instead of getting stuck.
        /// </summary>
        public void CheckHandshakeTimeout()
        {
            if (_pendingDisconnect is { } disconnected && ApplySocketFailure(disconnected))
                return;

            AdvanceRelayConnection();

            if (_handshakeTimeout == 0 || Time.Ticks < _handshakeTimeout)
                return;

            _handshakeTimeout = 0;
            Log.Warn($"[HandShake] Handshake timed out at step {CurrentLoginStep}, aborting connection attempt.");

            Disconnect();
            HandleConnectionFailure(SocketError.TimedOut);
        }

        private void OnNetClientConnected(object sender, EventArgs e) => MainThreadQueue.InvokeOnMainThread(() =>
        {
            if (_isDisposed || sender is not AsyncNetClient connection || !ReferenceEquals(connection, AsyncNetClient.Socket)
                || CurrentLoginStep != LoginSteps.Connecting || !connection.IsConnected)
                return;

            Log.Info("Connected!");
            SetLoginStep(LoginSteps.VerifyingAccount);

            uint address = connection.LocalIP;

            AsyncNetClient.Encryption?.Initialize(true, address);

            if (Client.Game.UO.Version >= ClientVersion.CV_6040)
            {
                uint clientVersion = (uint)Client.Game.UO.Version;

                byte major = (byte)(clientVersion >> 24);
                byte minor = (byte)(clientVersion >> 16);
                byte build = (byte)(clientVersion >> 8);
                byte extra = (byte)clientVersion;

                connection.Send_Seed(address, major, minor, build, extra);
            }
            else
            {
                connection.Send_Seed_Old(address);
            }

            connection.Send_FirstLogin(Account, Password);
        });

        private void OnNetClientDisconnected(object sender, SocketError e) => MainThreadQueue.InvokeOnMainThread(() =>
        {
            if (_isDisposed || sender is not AsyncNetClient connection || !ReferenceEquals(connection, AsyncNetClient.Socket)
                || _relayConnection != null
                || CurrentLoginStep is LoginSteps.Main or LoginSteps.PopUpMessage)
                return;

            // The login server can send the relay packet immediately before closing TCP.
            // Let the frame process received packets before deciding that the close was unexpected.
            _pendingDisconnect = (connection, e, Environment.TickCount64);
        });

        private bool ApplySocketFailure((AsyncNetClient Connection, SocketError Error, long ReceivedAt) disconnected)
        {
            if (!ReferenceEquals(disconnected.Connection, AsyncNetClient.Socket) || _relayConnection != null)
            {
                _pendingDisconnect = null;
                return false;
            }

            if (Environment.TickCount64 - disconnected.ReceivedAt < 1_000
                && (disconnected.Connection.HasPendingPackets || ClassicUO.Network.PacketHandlers.PacketParser.Instance.HasBufferedData))
                return false;

            Log.Warn($"Socket disconnected - {disconnected.Error}");
            Disconnect();
            HandleConnectionFailure(disconnected.Error == SocketError.Success ? SocketError.ConnectionReset : disconnected.Error);
            return true;
        }

        private void HandleConnectionFailure(SocketError e)
        {
            Characters = null;
            DisposeAllServerEntries();

            if (ShouldReconnect)
            {
                Reconnect = true;
                SetError(msg: string.Format(
                                             TazLang.Get("reconnect_please_wait01"),
                                             _reconnectTryCounter,
                                             StringHelper.AddSpaceBeforeCapital(e.ToString())
                                         ));
            }
            else
            {
                SetError(msg: string.Format(
                                                  TazLang.Get("connection_lost0"),
                                                  StringHelper.AddSpaceBeforeCapital(e.ToString())
                                              ));
            }

            SetLoginStep(LoginSteps.PopUpMessage);
            ConnectionFailed?.Invoke(this, e);
        }

        private void AdvanceRelayConnection()
        {
            if (_relayConnection is not { } relay || !relay.PreviousConnectionClosed.IsCompleted)
                return;

            if (relay.Connecting == null)
            {
                if (relay.Attempts >= MaxRelayAttempts)
                {
                    _relayConnection = null;
                    HandleConnectionFailure(SocketError.NotConnected);
                    ErrorOccurred?.Invoke(this, ErrorMessage);
                    return;
                }

                relay.Attempts++;
                Log.TraceDebug($"[HandShake] Connecting to relay server (attempt {relay.Attempts}).");
                EncryptionHelper.Instance?.Initialize(false, relay.Seed);
                relay.Connection = new AsyncNetClient();
                relay.Connection.EnableCompression();
                relay.Connecting = relay.Connection.Connect(relay.Address, relay.Port);
                ArmHandshakeTimeout(CurrentLoginStep);
                return;
            }

            if (!relay.Connecting.IsCompleted)
                return;

            AsyncNetClient connection = relay.Connection;
            if (relay.Connecting.IsCompletedSuccessfully && relay.Connecting.Result && connection.IsConnected)
            {
                _relayConnection = null;
                _connectedToRelay = true;
                AsyncNetClient.Socket = connection;
                connection.Disconnected += OnNetClientDisconnected;
                ArmHandshakeTimeout(CurrentLoginStep);

                Span<byte> seed = stackalloc byte[sizeof(uint)];
                System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(seed, relay.Seed);
                connection.Send(seed, true, true);
                connection.Send_SecondLogin(Account, Password, relay.Seed);
                Log.TraceDebug("[HandShake] Sent second login.");

                if (Settings.GlobalSettings.CustomServer is Settings.CustomServers.Eventine or Settings.CustomServers.LOCAL_SERVER)
                    connection.Send_TazUO();
                return;
            }

            relay.PreviousConnectionClosed = connection.Disconnect();
            relay.Connecting = null;
            relay.Connection = null;
        }

        private sealed class RelayConnection(string address, ushort port, uint seed, Task previousConnectionClosed)
        {
            public string Address { get; } = address;
            public ushort Port { get; } = port;
            public uint Seed { get; } = seed;
            public Task PreviousConnectionClosed { get; set; } = previousConnectionClosed;
            public AsyncNetClient Connection { get; set; }
            public Task<bool> Connecting { get; set; }
            public int Attempts { get; set; }
        }

        private void ParseCharacterList(ref StackDataReader p)
        {
            int count = p.ReadUInt8();
            Characters = new string[count];

            for (ushort i = 0; i < count; i++)
            {
                Characters[i] = p.ReadASCII(30).TrimEnd('\0');
                p.Skip(30);
            }
        }

        private void ParseCities(ref StackDataReader p)
        {
            byte count = p.ReadUInt8();
            Cities = new CityInfo[count];

            bool isNew = Client.Game.UO.Version >= ClientVersion.CV_70130;

            Vector2[] oldtowns =
            {
                new(105, 130), new(245, 90),
                new(165, 200), new(395, 160),
                new(200, 305), new(335, 250),
                new(160, 395), new(100, 250),
                new(270, 130), new(0xFFFF, 0xFFFF)
            };

            for (int i = 0; i < count; i++)
            {
                CityInfo cityInfo;

                if (isNew)
                {
                    byte cityIndex = p.ReadUInt8();
                    string cityName = p.ReadASCII(32);
                    string cityBuilding = p.ReadASCII(32);
                    ushort cityX = (ushort)p.ReadUInt32BE();
                    ushort cityY = (ushort)p.ReadUInt32BE();
                    sbyte cityZ = (sbyte)p.ReadUInt32BE();
                    uint cityMapIndex = p.ReadUInt32BE();
                    uint cityDescription = p.ReadUInt32BE();
                    p.Skip(4);

                    cityInfo = new CityInfo
                    (
                        cityIndex,
                        cityName,
                        cityBuilding,
                        Client.Game.UO.FileManager.Clilocs.GetString((int)cityDescription),
                        cityX,
                        cityY,
                        cityZ,
                        cityMapIndex,
                        isNew
                    );
                }
                else
                {
                    byte cityIndex = p.ReadUInt8();
                    string cityName = p.ReadASCII(31);
                    string cityBuilding = p.ReadASCII(31);

                    cityInfo = new CityInfo
                    (
                        cityIndex,
                        cityName,
                        cityBuilding,
                        string.Empty,
                        (ushort)oldtowns[i % oldtowns.Length].X,
                        (ushort)oldtowns[i % oldtowns.Length].Y,
                        0,
                        0,
                        isNew
                    );
                }

                Cities[i] = cityInfo;
            }
        }

        private void DisposeAllServerEntries()
        {
            if (Servers != null)
            {
                for (int i = 0; i < Servers.Length; i++)
                {
                    if (Servers[i] != null)
                    {
                        Servers[i].Dispose();
                        Servers[i] = null;
                    }
                }

                Servers = null;
            }
        }

        public void Dispose()
        {
            Instance = null;

            if (_isDisposed)
            {
                return;
            }

            _isDisposed = true;

            _ = CancelRelay();

            AsyncNetClient.Socket.Disconnected -= OnNetClientDisconnected;
            AsyncNetClient.Socket.Connected -= OnNetClientConnected;

            DisposeAllServerEntries();
            Characters = null;
            Cities = null;
        }
    }

    public delegate void NotifierEventHandler();

    public class CityInfo
    {
        public CityInfo
        (
            int index,
            string city,
            string building,
            string description,
            ushort x,
            ushort y,
            sbyte z,
            uint map,
            bool isNew
        )
        {
            Index = index;
            City = city;
            Building = building;
            Description = description;
            X = x;
            Y = y;
            Z = z;
            Map = map;
            IsNewCity = isNew;
        }

        public readonly string Building;
        public readonly string City;
        public readonly string Description;
        public readonly int Index;
        public readonly bool IsNewCity;
        public readonly uint Map;
        public readonly ushort X, Y;
        public readonly sbyte Z;
    }
}
