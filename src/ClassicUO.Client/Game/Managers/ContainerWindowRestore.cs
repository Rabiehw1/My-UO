using System;
using System.Collections.Generic;
using System.Xml;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Network;
using ClassicUO.Utility;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers;

internal sealed class ContainerWindowRestore(World world)
{
    private const int RestoreWindowMs = 15_000;
    private const int ResponseTimeoutMs = 2_000;
    private readonly Dictionary<uint, Entry> _pending = new();
    private ObjectActionQueueItem _queued;
    private uint _requested;
    private long _responseDeadline;
    private long _restoreDeadline;

    private readonly record struct Entry(uint? Parent, Point? Position, uint Backpack, string Path)
    {
        public int Attempts { get; init; }
        public AnchorManager.AnchorGroup AnchorGroup { get; init; }
        public Point AnchorCell { get; init; }
    }

    internal string GetBackpackPath(uint serial)
    {
        uint backpack = world.Player?.Backpack?.Serial ?? 0;
        if (backpack == 0)
            return null;

        var path = new List<uint>();
        for (Item item = world.Items.Get(serial); item is { IsDestroyed: false }; item = world.Items.Get(item.Container))
        {
            if (path.Contains(item.Serial))
                break;

            path.Add(item.Serial);
            if (item.Serial == backpack)
            {
                path.Reverse();
                return string.Join(",", path);
            }

            if (!SerialHelper.IsItem(item.Container))
                break;
        }

        return null;
    }

    internal void Schedule(uint serial, uint? parent, Point position, string path,
        AnchorManager.AnchorGroup anchorGroup = null, Point anchorCell = default)
    {
        if (!SerialHelper.IsItem(serial))
            return;

        uint backpack = 0;
        if (!string.IsNullOrEmpty(path))
        {
            string[] parts = path.Split(',');
            var chain = new uint[parts.Length];
            var seen = new HashSet<uint>();
            for (int i = 0; i < chain.Length; i++)
            {
                if (!uint.TryParse(parts[i], out uint bag) || !SerialHelper.IsItem(bag) || !seen.Add(bag))
                    return;
                chain[i] = bag;
            }

            if (chain[^1] != serial)
                return;

            backpack = chain[0];
            uint? ancestorParent = world.Player?.Serial;
            for (int i = 0; i < chain.Length - 1; i++)
            {
                _pending.TryAdd(chain[i], new Entry(ancestorParent, null, backpack, null));
                ancestorParent = chain[i];
            }
            parent = ancestorParent;
        }

        _pending[serial] = new Entry(parent, position, backpack, path)
        {
            AnchorGroup = anchorGroup,
            AnchorCell = anchorCell
        };
        _restoreDeadline = Environment.TickCount64 + RestoreWindowMs;
    }

    internal void Update()
    {
        if (_pending.Count == 0 || !AsyncNetClient.Socket.IsConnected)
            return;

        long now = Environment.TickCount64;
        if (now >= _restoreDeadline)
        {
            Clear();
            return;
        }

        if (_queued != null)
            return;

        if (_requested != 0)
        {
            if (now < _responseDeadline)
                return;

            if (_pending[_requested].Attempts >= 2)
                _pending.Remove(_requested);
            _requested = 0;
        }

        foreach ((uint serial, Entry entry) in _pending)
        {
            if (!IsReady(serial, entry))
                continue;

            if (GetWindow(serial) != null)
                Opened(serial);
            else
                QueueOpen(serial);
            return;
        }
    }

    private bool IsReady(uint serial, Entry entry)
    {
        if (world.Items.Get(serial) is not { IsDestroyed: false } item
            || entry.Parent is uint parent && (item.Container != parent || _pending.ContainsKey(parent)))
            return false;

        if (entry.Backpack == 0)
            return true; // The earlier gumps.xml format only saved the immediate parent.

        return world.Player?.Backpack?.Serial == entry.Backpack &&
            (serial == entry.Backpack || item.BackpackOrRootContainer == entry.Backpack);
    }

    private void QueueOpen(uint serial)
    {
        _queued = new ObjectActionQueueItem(() =>
        {
            if (!_pending.TryGetValue(serial, out Entry entry) || !IsReady(serial, entry)
                || !AsyncNetClient.Socket.IsConnected)
                return;

            if (GetWindow(serial) != null)
            {
                Opened(serial);
                return;
            }

            _requested = serial;
            _responseDeadline = Environment.TickCount64 + ResponseTimeoutMs;
            _pending[serial] = entry with { Attempts = entry.Attempts + 1 };
            AsyncNetClient.Socket.Send_DoubleClick(serial);
        }, _ => _queued = null)
        {
            TriggersGlobalCooldown = () => _requested == serial
        };
        ObjectActionQueue.Instance.Enqueue(_queued, ActionPriority.UseItem);
    }

    internal bool IsOpeningHiddenParent(uint serial) =>
        _requested == serial && _pending.TryGetValue(serial, out Entry entry) && entry.Position == null;

    internal void Opened(uint serial)
    {
        if (!_pending.Remove(serial, out Entry entry))
            return;

        if (_requested == serial)
            _requested = 0;
        _restoreDeadline = Environment.TickCount64 + RestoreWindowMs;

        if (entry.Position is Point position && GetWindow(serial) is { } window)
        {
            if (world.Items.Get(serial)?.IsCorpse != true)
            {
                window.Location = position;
                window.SetInScreen();
            }

            if (window is GridContainer grid && entry.AnchorGroup is { } group &&
                UIManager.AnchorManager[grid] == null && group.TryRestoreControl(entry.AnchorCell.X, entry.AnchorCell.Y, grid))
            {
                UIManager.AnchorManager[grid] = group;
            }
        }
    }

    // A second disconnect can happen while the shard is still sending the bags.
    // Keep pending backpack windows in the snapshot until their open requests finish.
    internal void SavePending(XmlTextWriter writer, HashSet<uint> openWindows)
    {
        foreach ((uint serial, Entry entry) in _pending)
        {
            if (entry.Position is not Point position || string.IsNullOrEmpty(entry.Path) || openWindows.Contains(serial))
                continue;

            writer.WriteStartElement("gump");
            writer.WriteAttributeString("type", ((int)GumpType.Container).ToString());
            writer.WriteAttributeString("serial", serial.ToString());
            writer.WriteAttributeString("x", position.X.ToString());
            writer.WriteAttributeString("y", position.Y.ToString());
            writer.WriteAttributeString("backpackPath", entry.Path);
            writer.WriteEndElement();
        }
    }

    internal void Clear()
    {
        _queued?.SetCanceled();
        _pending.Clear();
        _requested = 0;
    }

    private static Gump GetWindow(uint serial) =>
        (Gump)UIManager.GetGump<GridContainer>(serial) ?? UIManager.GetGump<ContainerGump>(serial);
}
