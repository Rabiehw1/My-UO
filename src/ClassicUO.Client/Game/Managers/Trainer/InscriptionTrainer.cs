#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using ClassicUO.Configuration;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.UI.Controls;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Utility;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;

namespace ClassicUO.Game.Managers.Trainer;

internal static class InscriptionTrainer
{
    private enum Step { Idle, Scroll, Book, Menu }
    private static Step _step;
    private static uint _scroll, _book, _player;
    private static Profile? _profile;
    private static World? _world;
    private static Gump? _menu;
    private static int _button;
    private static ShardSpellBookInfo? _bookInfo;
    private static int _skill;
    private static long _deadline, _nextUse;
    private static bool _recovering, _warned;

    internal static bool NeedsRecovery(bool recovering, int mana, int maximum, bool meditating) =>
        maximum > 0 && (meditating || (recovering ? mana < maximum : mana * 100L < maximum * 20L));

    private static bool Carried(World world, uint serial) =>
        world.Items.Get(serial) is { IsDestroyed: false } item &&
        (item.Container == world.Player.Serial || item.BackpackOrRootContainer == world.Player.Backpack?.Serial);

    internal static bool Begin(World world, uint scroll)
    {
        var player = world.Player;
        if (player.IsDead || player.ManaMax == 0 || Time.Ticks < _nextUse)
            return false;
        var profile = ProfileManager.CurrentProfile;
        int skill = player.Skills?.FirstOrDefault(s => s?.Index == 23)?.ValueFixed ?? 0;
        uint book = ChooseBook(world, skill);
        if (scroll == book || !Carried(world, scroll) || !Carried(world, book))
        {
            if (!_warned) GameActions.Print(world, TazLang.Get("trainer_inscription_items"), Constants.HUE_WARN);
            _warned = true;
            return false;
        }
        _warned = false;
        bool meditating = player.IsBuffIconExists(BuffIconType.ActiveMeditation) || TrainerManager.Instance.MeditationActive;
        _recovering = NeedsRecovery(_recovering, player.Mana, player.ManaMax, meditating);
        if (_recovering)
        {
            if (meditating)
                return false;
            GameActions.UseSkill(46); // Meditation
            _nextUse = Time.Ticks + 3000;
            return true;
        }
        _player = player.Serial;
        _profile = profile;
        _scroll = scroll;
        _book = book;
        _skill = skill;
        _step = Step.Scroll;
        _deadline = Time.Ticks + 2000;
        GameActions.UseSkill(23);
        return true;
    }

    private static bool Current(World world) =>
        world.Player is { IsDead: false } player && player.Serial == _player &&
        _profile != null && ReferenceEquals(ProfileManager.CurrentProfile, _profile) &&
        _profile.Trainer.Selected.Contains("inscription") &&
        _profile.Trainer.Targets.TryGetValue("inscription", out var pick) && pick.Serial == _scroll;

    internal static bool Waiting(World world)
    {
        if (_step == Step.Idle) return false;
        if (Current(world))
        {
            if (_step == Step.Menu)
            {
                if (Time.Ticks < _deadline)
                {
                    if (_menu == null) return true;
                    if (!_menu.IsDisposed) _menu.OnButtonClick(_button);
                }
                else
                    GameActions.Print(world, TazLang.Get("trainer_inscription_menu"), Constants.HUE_WARN);
            }
            else if (Time.Ticks < _deadline) return true;
        }
        ClearMenu();
        _step = Step.Idle;
        _nextUse = Time.Ticks + 10500;
        return false;
    }

    internal static bool TryCursor(World world, CursorTarget cursor, TargetType type)
    {
        if (_step is not (Step.Scroll or Step.Book) || cursor != CursorTarget.Object ||
            type != TargetType.Neutral || !Waiting(world))
            return false;
        bool first = _step == Step.Scroll;
        uint target = first ? _scroll : _book;
        if (!Carried(world, target))
        {
            _step = Step.Idle;
            _nextUse = Time.Ticks + 10500;
            world.TargetManager.CancelTarget();
            return true;
        }
        if (first)
        {
            _step = Step.Book;
            _deadline = Time.Ticks + 2000;
        }
        else
        {
            _step = Step.Menu;
            _world = world;
            _deadline = Time.Ticks + 3000;
            world.ServerGumpReceived += ObserveMenu;
        }
        world.TargetManager.Target(target);
        return true;
    }

    // Zulu Hotel Omega Info Vault, Inscription: use the "From Book" difficulty column.
    internal static int Difficulty(ShardSpellBook? book, int index)
    {
        if (book == null)
        {
            if (index is < 0 or >= 64) return -1;
            if (index == 31) return 60; // Recall is the fourth-circle exception.
            return (index / 8) switch { 0 => 20, 1 => 30, 2 => 40, 3 => 50, 4 => 60, 5 => 75, 6 => 90, _ => 100 };
        }
        var spell = ShardSpellCatalog.GetSpell(book.Value, index);
        if (spell == null) return -1;
        return book switch
        {
            ShardSpellBook.Necro => index switch { < 4 => 110, < 8 => 120, < 12 => 150, _ => 160 },
            ShardSpellBook.Earth => 120 + index / 4 * 10,
            ShardSpellBook.Holy when spell.Name != "Wrath of God" => 130 + index / 4 * 10,
            _ => -1
        };
    }

    private static uint ChooseBook(World world, int skill)
    {
        var caster = ShardSpellCaster.Instance;
        uint best = 0;
        int highest = -1;
        _bookInfo = null;
        foreach (var info in ShardSpellCatalog.Books)
        {
            uint serial = caster.GetBookSerial(info.Book);
            if (serial == 0 || !Carried(world, serial)) continue;
            for (int i = 0; i < info.Spells.Count; i++)
            {
                int difficulty = Difficulty(info.Book, i);
                if (difficulty <= highest || difficulty * 10 > skill) continue;
                highest = difficulty;
                best = serial;
                _bookInfo = info;
            }
        }
        if (best != 0 || skill < 200) return best;
        foreach (var item in world.Items.Values)
            if (item.Graphic is 0x0EFA or 0x0EFB && Carried(world, item.Serial) &&
                !caster.Settings.BookSerials.ContainsValue(item.Serial))
                return item.Serial;
        return 0;
    }

    private static void ObserveMenu(Gump gump, string[] text)
    {
        var buttons = gump.FindControls<Button>().Where(b => b.ButtonAction == ButtonAction.Activate).ToArray();
        int highest = -1;
        for (int i = 0; i < (_bookInfo?.Spells.Count ?? 64); i++)
        {
            int difficulty = Difficulty(_bookInfo?.Book, i);
            if (difficulty <= highest || difficulty * 10 > _skill) continue;
            int button;
            if (_bookInfo is { } info)
            {
                var spell = info.Spells[i];
                if (!ShardSpellCaster.MatchesBookGump(_book, gump.LocalSerial, info, spell, text)) continue;
                button = ShardSpellCaster.Instance.GetButton(info.Book, i);
                if (!buttons.Any(b => b.ButtonID == button)) continue;
            }
            else
            {
                // The regular Magery book has spell IDs but no shard reply-button mapping.
                // Read the reply button beside the named spell on the incoming dialog.
                string name = SpellsMagery.GetSpell(i + 1).Name;
                if (gump.LocalSerial != _book && gump.LocalSerial != _player) continue;
                int? matched = FindLabeledButton(gump, buttons, name);
                if (matched == null) continue;
                button = matched.Value;
            }
            _menu = gump;
            _button = button;
            highest = difficulty;
        }
    }

    private static int? FindLabeledButton(Gump gump, Button[] buttons, string name)
    {
        foreach (var label in gump.Children.OfType<Control>())
        {
            string? raw = label switch { Label l => l.Text, HtmlControl h => h.Text, _ => null };
            if (raw == null || !WebUtility.HtmlDecode(RegexHelper.GetRegex("<[^>]*>").Replace(raw, "")).Trim()
                .Equals(name, StringComparison.OrdinalIgnoreCase)) continue;
            var adjacent = buttons.Where(b => b.Page == label.Page && b.X <= label.X &&
                label.X - (b.X + b.Width) is >= -4 and <= 24 && Math.Abs(b.Y - label.Y) <= 8).ToArray();
            if (adjacent.Length == 1) return adjacent[0].ButtonID;
        }
        return null;
    }

    private static void ClearMenu()
    {
        if (_world != null) _world.ServerGumpReceived -= ObserveMenu;
        _menu = null;
        _world = null;
    }

    internal static void Reset()
    {
        ClearMenu();
        _menu = null;
        _profile = null;
        _step = Step.Idle;
        _recovering = _warned = false;
        _nextUse = 0;
    }
}
