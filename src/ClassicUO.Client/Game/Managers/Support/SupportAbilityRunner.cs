#nullable enable

using System;
using System.Collections.Generic;
using ClassicUO.Configuration;
using ClassicUO.Game.Data;
using ClassicUO.Game.GameObjects;
using ClassicUO.Game.Managers.ShardSpells;
using ClassicUO.Game.UI.Gumps;
using ClassicUO.Utility.Logging;

namespace ClassicUO.Game.Managers.Support;

/// <summary>
/// Performs support abilities and remembers when each was last used. For item-and-gump abilities (a songbook) it
/// waits for the gump the item opens and presses the configured button, like an Orion gump hook.
/// Owned by <see cref="SupportAgentManager" />; game thread only.
/// </summary>
internal sealed class SupportAbilityRunner
{
    #region Private members

    /// <summary>Extra time, beyond the cast time, for a spell's target cursor to arrive.</summary>
    private const int TARGET_CURSOR_GRACE_MS = 2000;

    private const ushort DEFAULT_BANDAGE_GRAPHIC = 0x0E21;

    private readonly World _world;
    private readonly Dictionary<string, long> _lastUsed = new();
    private readonly PendingGumpButton _pendingGump;

    #endregion

    #region Ctor

    /// <param name="world">The game world.</param>
    /// <param name="reportNoGump">Shows a message when an item's gump doesn't open, given the ability's name.</param>
    public SupportAbilityRunner(World world, Action<string> reportNoGump)
    {
        _world = world;
        _pendingGump = new PendingGumpButton(world, reportNoGump);
    }

    #endregion

    #region Public methods

    /// <summary>Whether <paramref name="ability" />'s own cooldown has passed.</summary>
    /// <param name="ability">Ability to check.</param>
    /// <param name="now">Current <see cref="Time.Ticks" />.</param>
    /// <returns>True when it may be used again.</returns>
    public bool IsOffCooldown(SupportAbility ability, long now) =>
        !_lastUsed.TryGetValue(ability.Id, out long lastUsed) || now - lastUsed >= ability.CooldownMs;

    /// <summary>Time left before <paramref name="ability" /> may be used again.</summary>
    /// <param name="ability">Ability to check.</param>
    /// <param name="now">Current <see cref="Time.Ticks" />.</param>
    /// <returns>Milliseconds left; 0 when ready.</returns>
    public long RemainingCooldownMs(SupportAbility ability, long now) =>
        _lastUsed.TryGetValue(ability.Id, out long lastUsed) ? Math.Max(0, lastUsed + ability.CooldownMs - now) : 0;

    /// <summary>
    /// Whether the helper has what <paramref name="ability" /> needs (the item, a bandage, a target), so a turn is
    /// never spent on something that can't work.
    /// </summary>
    /// <param name="ability">Ability to check.</param>
    /// <returns>True if the ability can be attempted now.</returns>
    public bool HasRequirements(SupportAbility ability)
    {
        PlayerMobile? player = _world.Player;

        if (player == null)
            return false;

        return ability.Action switch
        {
            SupportActionType.CastSpell => ability.SpellId > 0,
            // Item abilities use the exact item picked in the tab. Its serial is sent as is: the shard accepts it even
            // when the client hasn't been shown the container holding it.
            SupportActionType.UseItemAndGumpButton => ability.ItemSerial != 0,
            SupportActionType.UseItem => ability.ItemSerial != 0 || TryFindItemByGraphicAndHue(ability, player),
            SupportActionType.CastShardSpell => ShardSpellCaster.Instance.GetBookSerial(ability.ShardBook) != 0
                                                && !ShardSpellCaster.Instance.IsCasting,
            SupportActionType.Bandage => player.FindBandage(ProfileBandageGraphic()) != null,
            SupportActionType.Say => !string.IsNullOrWhiteSpace(ability.Text),
            _ => false
        };
    }

    /// <summary>Performs <paramref name="ability" /> on <paramref name="targetSerial" />.</summary>
    /// <param name="ability">Ability to perform.</param>
    /// <param name="targetSerial">Mobile to aim at; 0 for abilities without a target.</param>
    /// <param name="now">Current <see cref="Time.Ticks" />.</param>
    /// <returns>True if something was sent to the shard.</returns>
    public bool TryUse(SupportAbility ability, uint targetSerial, long now)
    {
        bool sent = ability.Action switch
        {
            SupportActionType.CastSpell => CastSpell(ability, targetSerial),
            SupportActionType.UseItemAndGumpButton => UseItemAndPressButton(ability, now),
            SupportActionType.UseItem => UseItem(ability, targetSerial),
            SupportActionType.CastShardSpell => CastShardSpell(ability, targetSerial),
            SupportActionType.Bandage => targetSerial != 0 && GameActions.UseBandageOnTarget(_world, targetSerial, TARGET_CURSOR_GRACE_MS),
            SupportActionType.Say => Say(ability),
            _ => false
        };

        if (sent)
            _lastUsed[ability.Id] = now;

        return sent;
    }

    /// <summary>Presses the pending gump button once the item's gump appears. Call every frame.</summary>
    /// <param name="now">Current <see cref="Time.Ticks" />.</param>
    public void Update(long now) => _pendingGump.Update(now);

    /// <summary>Forgets cooldowns and any pending gump, e.g. when the agent is turned off.</summary>
    public void Reset()
    {
        _lastUsed.Clear();
        _pendingGump.Clear();
    }

    #endregion

    #region Private methods

    private bool CastSpell(SupportAbility ability, uint targetSerial)
    {
        // Set before casting: the cursor arrives after the cast time and must go to the intended target, whatever
        // cursor type the shard uses.
        if (targetSerial != 0)
        {
            TargetManager.SetAutoTarget(
                targetSerial,
                TargetType.Beneficial,
                matchAnyTargetType: true,
                ability.CastTimeMs + TARGET_CURSOR_GRACE_MS
            );
        }

        GameActions.CastSpell(ability.SpellId);
        return true;
    }

    private bool UseItemAndPressButton(SupportAbility ability, long now)
    {
        PlayerMobile? player = _world.Player;

        if (player == null || ability.ItemSerial == 0)
            return false;

        _pendingGump.Arm(ability.GumpButton, ability.Name, now);

        GameActions.DoubleClick(_world, ability.ItemSerial, ignoreQueue: true);
        return true;
    }

    private bool UseItem(SupportAbility ability, uint targetSerial)
    {
        if (ability.ItemSerial == 0)
            return false;

        // Items like a blessing staff open a target cursor; answer it with the intended target.
        if (targetSerial != 0)
            SetAutoTarget(ability, targetSerial);

        GameActions.DoubleClick(_world, ability.ItemSerial, ignoreQueue: true);
        return true;
    }

    private bool CastShardSpell(SupportAbility ability, uint targetSerial)
    {
        if (targetSerial != 0)
            SetAutoTarget(ability, targetSerial);

        return ShardSpellCaster.Instance.TryCast(ability.ShardBook, ability.SpellId);
    }

    private static void SetAutoTarget(SupportAbility ability, uint targetSerial) =>
        TargetManager.SetAutoTarget(
            targetSerial,
            TargetType.Beneficial,
            matchAnyTargetType: true,
            ability.CastTimeMs + TARGET_CURSOR_GRACE_MS
        );

    /// <summary>
    /// For an item given by graphic and hue but not picked yet (e.g. Nagash's staff), looks in the helper's hands
    /// and backpack. A match is remembered by serial, so it is used directly from then on.
    /// </summary>
    private static bool TryFindItemByGraphicAndHue(SupportAbility ability, PlayerMobile player)
    {
        if (ability.ItemGraphic == 0)
            return false;

        Item? item = player.FindItem(ability.ItemGraphic, ability.ItemHue) ?? player.Backpack?.FindItem(ability.ItemGraphic, ability.ItemHue);

        if (item == null)
            return false;

        ability.ItemSerial = item.Serial;
        return true;
    }

    private static bool Say(SupportAbility ability)
    {
        if (string.IsNullOrWhiteSpace(ability.Text))
            return false;

        GameActions.Say(ability.Text);
        return true;
    }

    private static ushort ProfileBandageGraphic() =>
        ProfileManager.CurrentProfile?.BandageAgentGraphic ?? DEFAULT_BANDAGE_GRAPHIC;


    #endregion
}
