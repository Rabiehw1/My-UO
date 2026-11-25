#nullable enable
using System;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.Hotkeys;
using ClassicUO.Game.Managers.Structs;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Network;
using Microsoft.Xna.Framework;

namespace ClassicUO.Game.Managers;

internal sealed class WeaponSwitchManager
{
    private const int DisarmTimeout = 600;
    private const int MoveDelay = 200;
    private const int MoveTimeout = 3000;
    private const int NoteDuration = 3500;

    private sealed record SwitchRequest(uint Target, bool Alternate, string Label, string LoadoutName, string TargetName, WeaponSet Set, uint Bag);

    private WeaponSwitchSettings? _settings;
    private SwitchRequest? _request;
    private TextObject? _loadoutNote;
    private (uint Target, bool Alternate)? _lastSwitch;
    private uint _moving;
    private bool _equipping;
    private long _disarmUntil;
    private bool _waitingForServerEquip;
    private long _nextAction;
    private long _deadline;

    public WeaponSwitchSettings Settings => _settings ??= WeaponSwitchSettings.Load();

    public void Start(World world)
    {
        if (_request != null || world.Player is not { IsDead: false, Backpack: Item backpack } player
            || Client.Game.UO.GameCursor.ItemHold.Enabled)
            return;

        uint target = world.TargetManager.LastAttack;
        Mobile? mobile = world.Mobiles.Get(target) is { IsDestroyed: false, IsDead: false } livingTarget ? livingTarget : null;
        var monster = mobile != null ? Settings.FindMonster(mobile.Graphic, mobile.Hue) : null;
        var assignment = monster != null ? Settings.GetAssignment(monster) : new(Settings.DefaultLoadout, null);
        bool alternate = assignment.Alternate.HasValue && _lastSwitch == (target, false);
        WeaponLoadout loadout = alternate ? assignment.Alternate.GetValueOrDefault() : assignment.Primary;
        WeaponSet set = Settings.GetSet(loadout);
        string loadoutName = Settings.GetLoadoutLabel(loadout);
        string label = $"{monster?.Name ?? "Default"} - {loadoutName}";
        uint bag = Settings.WeaponBag == 0 ? backpack.Serial : Settings.WeaponBag;

        if (!TryGetEquipment(world, player, set.Weapon, out Layer weaponLayer))
        {
            GameActions.Print($"{label}. Set a hand weapon serial and keep it in your backpack.", Constants.HUE_WARN);
            return;
        }

        if (set.Shield != 0 && (!TryGetEquipment(world, player, set.Shield, out Layer shieldLayer) || shieldLayer == weaponLayer))
        {
            GameActions.Print($"{label}. The shield must be available and use the other hand layer.", Constants.HUE_WARN);
            return;
        }

        if (!IsAccessibleBag(world, backpack, bag))
        {
            GameActions.Print("Weapon Switch requires a weapon bag inside your backpack.", Constants.HUE_WARN);
            return;
        }

        string targetName = mobile == null ? TazLang.Get("weaponswitch_note_no_target")
            : !string.IsNullOrWhiteSpace(mobile.Name) ? mobile.Name
            : monster?.Name ?? TazLang.Get("weaponswitch_note_unknown_target");

        var request = new SwitchRequest(target, alternate, label, loadoutName, targetName, set, bag);
        if (IsEquipped(player, set))
        {
            Complete(player, request);
            return;
        }

        _request = request;
        long now = Time.Ticks;
        _disarmUntil = 0;
        _waitingForServerEquip = false;
        _nextAction = now;

        uint right = player.FindItemByLayer(Layer.OneHanded)?.Serial ?? 0;
        uint left = player.FindItemByLayer(Layer.TwoHanded)?.Serial ?? 0;
        if (IsExtra(right, set) || IsExtra(left, set))
        {
            _disarmUntil = now + DisarmTimeout;
            GameActions.Say(".disarm");
        }

        _deadline = now + DisarmTimeout + Math.Max(0, GlobalActionCooldown.CooldownDuration) + MoveTimeout;
    }

    public void Update(World world)
    {
        if (_request is not { } request)
            return;

        if (world.Player is not { IsDead: false, Backpack: Item backpack } player
            || HotKeys.GloballyDisabled || HotKeys.Get(HotKeyRegistrar.WeaponSwitchId)?.Enabled != true)
        {
            _request = null;
            _moving = 0;
            return;
        }

        long now = Time.Ticks;
        if (now < _nextAction)
            return;

        if (now >= _deadline || !IsAccessibleBag(world, backpack, request.Bag))
        {
            Fail(request);
            return;
        }

        uint right = player.FindItemByLayer(Layer.OneHanded)?.Serial ?? 0;
        uint left = player.FindItemByLayer(Layer.TwoHanded)?.Serial ?? 0;
        // .disarm may clear both hands in separate updates. Wait for both or fall back to moving what's left.
        if (now < _disarmUntil && (right != 0 || left != 0))
            return;

        _disarmUntil = 0;
        if (_moving != 0)
        {
            bool inHand = _moving == right || _moving == left;
            if (inHand != _equipping)
                return;

            _moving = 0;
        }

        if (IsEquipped(player, request.Set))
        {
            Complete(player, request);
            return;
        }

        if (_waitingForServerEquip || GlobalActionCooldown.IsOnCooldown || ObjectActionQueue.Instance.HasPendingMove
            || Client.Game.UO.GameCursor.ItemHold.Enabled || Client.Game.UO.GameCursor.ItemHold.Dropped)
            return;

        uint extra = IsExtra(right, request.Set) ? right : IsExtra(left, request.Set) ? left : 0;
        if (extra != 0)
        {
            Send(new MoveRequest(extra, request.Bag), extra, false);
            return;
        }

        if (ProfileManager.ServerSettings?.UseKrEquipSwap == true)
        {
            Span<uint> missing = stackalloc uint[2];
            int count = 0;
            uint shieldSerial = request.Set.Shield;
            uint weaponSerial = request.Set.Weapon;
            if (shieldSerial != 0 && shieldSerial != right && shieldSerial != left)
                missing[count++] = shieldSerial;
            if (weaponSerial != right && weaponSerial != left)
                missing[count++] = weaponSerial;

            for (int i = 0; i < count; i++)
            {
                if (!TryGetEquipment(world, player, missing[i], out _))
                {
                    Fail(request);
                    return;
                }
            }

            AsyncNetClient.Socket.Send_EquipMacroKR(missing[..count]);
            _waitingForServerEquip = true;
            _deadline = now + MoveTimeout;
            GlobalActionCooldown.BeginCooldown();
            return;
        }

        uint shield = request.Set.Shield;
        uint serial = shield != 0 && shield != left && shield != right ? shield : request.Set.Weapon;
        if (!TryGetEquipment(world, player, serial, out Layer layer)
            || MoveRequest.EquipItem(serial, layer) is not { } equip)
        {
            Fail(request);
            return;
        }

        Send(equip, serial, true);

        void Send(MoveRequest move, uint movedSerial, bool equipping)
        {
            _moving = movedSerial;
            _equipping = equipping;
            _nextAction = now + MoveDelay;
            _deadline = now + Math.Max(MoveDelay, GlobalActionCooldown.CooldownDuration) + MoveTimeout;
            move.Execute();
            GlobalActionCooldown.BeginCooldown();
        }
    }

    private void Complete(PlayerMobile player, SwitchRequest request)
    {
        _lastSwitch = (request.Target, request.Alternate);
        _request = null;
        GameActions.Print(request.Label);

        string loadoutLine = TazLang.Get("weaponswitch_note_loadout", [request.LoadoutName]);
        string targetLine = TazLang.Get("weaponswitch_note_target", [request.TargetName]);
        string text = $"/c[#8FE8FF]{EscapeNoteLine(loadoutLine)}\n/c[#D4B9FF]{EscapeNoteLine(targetLine)}";
        Profile profile = ProfileManager.CurrentProfile;
        var options = TextBox.RTLOptions.DefaultCenterStroked(profile.OverheadChatWidth);
        options.ConvertHtmlColors = false;

        _loadoutNote?.Destroy();
        _loadoutNote = TextObject.Create(player.World);
        _loadoutNote.TextBox = TextBox.GetOne(text, profile.OverheadChatFont, profile.OverheadChatFontSize, Color.White, options);
        _loadoutNote.Time = Time.Ticks + NoteDuration;
        player.AddMessage(_loadoutNote);
    }

    // FontStashSharp uses slashes for formatting commands. Names are shown as literal text.
    private static string EscapeNoteLine(string text) => text.ReplaceLineEndings(" ").Replace("/", "//");

    private void Fail(SwitchRequest request)
    {
        _request = null;
        _moving = 0;
        GameActions.Print($"{request.Label}. Equip incomplete.", Constants.HUE_WARN);
    }

    private static bool TryGetEquipment(World world, PlayerMobile player, uint serial, out Layer layer)
    {
        Item? item = world.Items.Get(serial);
        layer = item is { IsDestroyed: false } ? (Layer)item.ItemData.Layer : Layer.Invalid;
        return item != null && layer is Layer.OneHanded or Layer.TwoHanded
            && (item.BackpackOrRootContainer == player.Backpack?.Serial
                || player.FindItemByLayer(layer)?.Serial == serial);
    }

    private static bool IsAccessibleBag(World world, Item backpack, uint serial) => serial == backpack.Serial
        || (world.Items.Get(serial) is { IsDestroyed: false, ItemData.IsContainer: true } bag
            && bag.BackpackOrRootContainer == backpack.Serial);

    private static bool IsEquipped(PlayerMobile player, WeaponSet set)
    {
        uint right = player.FindItemByLayer(Layer.OneHanded)?.Serial ?? 0;
        uint left = player.FindItemByLayer(Layer.TwoHanded)?.Serial ?? 0;
        return (right == set.Weapon || left == set.Weapon)
            && (set.Shield == 0 || right == set.Shield || left == set.Shield)
            && !IsExtra(right, set) && !IsExtra(left, set);
    }

    private static bool IsExtra(uint serial, WeaponSet set) => serial != 0 && serial != set.Weapon && serial != set.Shield;
}
