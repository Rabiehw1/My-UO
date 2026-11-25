// SPDX-License-Identifier: BSD-2-Clause

using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Game.UI.MyraWindows;
using ClassicUO.Network;

namespace ClassicUO.Game.Managers
{
    public class WMapEntity
    {
        private static Dictionary<uint, string> _mobileNameCache = new();

        public WMapEntity(uint serial, WaypointsType waypointType = WaypointsType.None)
        {
            Serial = serial;
            WaypointType = waypointType;

            Mobile mob = Client.Game.UO.World.Mobiles.Get(serial);

            if (mob != null && waypointType == WaypointsType.None)
                GetName();
        }

        public bool IsGuild;
        public uint LastUpdate;
        public string Name;
        public readonly uint Serial;
        public int X, Y, HP, Map;
        public WaypointsType WaypointType;

        public string GetName()
        {
            Entity e = Client.Game.UO.World.Get(Serial);

            if (e != null && !e.IsDestroyed && !string.IsNullOrEmpty(e.Name) && Name != e.Name)
            {
                Name = e.Name;
                _mobileNameCache[Serial] = Name;
                _ = FriendliesSQLManager.Instance.AddAsync(Serial, Name);
            }

            return string.IsNullOrEmpty(Name) && !_mobileNameCache.TryGetValue(Serial, out Name) ? "<out of range>" : Name;
        }
    }

    public sealed class WorldMapEntityManager
    {
        private bool _ackReceived;
        private uint _lastUpdate, _lastPacketSend, _lastPacketRecv;
        private readonly List<WMapEntity> _toRemove = new List<WMapEntity>();
        public WMapEntity _corpse;
        private readonly World _world;

        public WorldMapEntityManager(World world) { _world = world; }

        private bool UsesNewMovement => (_world.ClientFeatures.Flags & CharacterListFlags.CLF_NEW_MOVEMENT_SYSTEM) != 0;
        private static bool IsEncrypted => AsyncNetClient.Encryption is { EncryptionType: not 0 };

        internal bool CanUseLegacyTracking => !UsesNewMovement && !IsEncrypted;

        private static bool InGameMapTrackingEnabled => ProfileManager.CurrentProfile?.WorldMapShowParty == true
            && UIManager.GetGump<WorldMapGump>() is { IsDisposed: false, IsDetached: false };

        public bool Enabled => InGameMapTrackingEnabled || (ProfileManager.CurrentProfile?.WorldMapShowParty == true
            && DetachedWorldMapWindow.IsOpenFor(_world));

        internal bool SupportsServerQueries => CanQueryServer(_ackReceived,
            ProfileManager.ServerSettings?.UseLegacyWorldMapTracking == true, UsesNewMovement, IsEncrypted);

        internal static bool CanQueryServer(bool acknowledged, bool legacyOptIn, bool usesNewMovement, bool encrypted) =>
            !encrypted && (acknowledged || legacyOptIn && !usesNewMovement);

        public readonly Dictionary<uint, WMapEntity> Entities = new Dictionary<uint, WMapEntity>();

        public void SetACKReceived() => _ackReceived = true;

        public void AddOrUpdate
        (
            uint serial,
            int x,
            int y,
            int hp,
            int map,
            bool isguild,
            string name = null,
            bool from_packet = false,
            WaypointsType type = WaypointsType.None
        )
        {
            if (type == WaypointsType.None)
            {
                if (from_packet)
                {
                    _lastPacketRecv = Time.Ticks + 10000;
                }
                else if (_lastPacketRecv < Time.Ticks)
                {
                    return;
                }

                if (!Enabled)
                {
                    return;
                }
            }

            if (string.IsNullOrEmpty(name))
            {
                Entity ent = _world.Get(serial);

                if (ent != null && !string.IsNullOrEmpty(ent.Name))
                {
                    name = ent.Name;
                }
            }

            if (!Entities.TryGetValue(serial, out WMapEntity entity) || entity == null)
            {
                entity = new WMapEntity(serial, type)
                {
                    X = x, Y = y, HP = hp, Map = map,
                    LastUpdate = Time.Ticks + 1000,
                    IsGuild = isguild,
                    Name = name
                };

                Entities[serial] = entity;
            }
            else
            {
                entity.X = x;
                entity.Y = y;
                entity.HP = hp;
                entity.Map = map;
                entity.IsGuild = isguild;
                entity.LastUpdate = Time.Ticks + 1000;
                entity.WaypointType = type;

                if (string.IsNullOrEmpty(entity.Name) && !string.IsNullOrEmpty(name))
                {
                    entity.Name = name;
                }
            }

            if (string.IsNullOrEmpty(entity.Name))
            {
                FriendliesSQLManager.Instance.GetNameAsync(entity.Serial).ContinueWith((dbName) =>
                {
                    if (string.IsNullOrEmpty(dbName.Result)) return;

                    MainThreadQueue.EnqueueAction(() => entity.Name = dbName.Result);
                });
            }
        }

        public void Remove(uint serial)
        {
            if (Entities.ContainsKey(serial))
            {
                Entities.Remove(serial);
            }
        }

        public void RemoveUnupdatedWEntity()
        {
            if (_corpse != null && _corpse.LastUpdate < Time.Ticks - 1000)
            {
                _corpse = null;
            }
            if (_lastUpdate > Time.Ticks)
            {
                return;
            }

            _lastUpdate = Time.Ticks + 1000;

            long ticks = Time.Ticks - 1000;

            foreach (WMapEntity entity in Entities.Values)
            {
                if (entity.WaypointType != WaypointsType.None)
                {
                    continue;
                }

                if (entity.LastUpdate < ticks)
                {
                    _toRemove.Add(entity);
                }
            }

            if (_toRemove.Count != 0)
            {
                foreach (WMapEntity entity in _toRemove)
                {
                    Entities.Remove(entity.Serial);
                }

                _toRemove.Clear();
            }
        }

        public WMapEntity GetEntity(uint serial)
        {
            Entities.TryGetValue(serial, out WMapEntity entity);

            return entity;
        }

        public void RequestServerPartyGuildInfo()
        {
            if (!_world.InGame || !SupportsServerQueries || !InGameMapTrackingEnabled || _lastPacketSend >= Time.Ticks)
            {
                return;
            }

            _lastPacketSend = Time.Ticks + 250;
            AsyncNetClient.Socket.Send_QueryGuildPosition();

            if (_world.Party != null && _world.Party.Leader != 0)
            {
                foreach (PartyMember member in _world.Party.Members)
                {
                    if (member == null || !SerialHelper.IsValid(member.Serial))
                        continue;

                    Mobile mobile = _world.Mobiles.Get(member.Serial);
                    if (mobile == null || mobile.Distance > _world.ClientViewRange)
                    {
                        AsyncNetClient.Socket.Send_QueryPartyPosition();
                        break;
                    }
                }
            }
        }

        public void Clear()
        {
            Entities.Clear();
            _ackReceived = false;
            _lastPacketSend = _lastPacketRecv = 0;
        }
    }
}
