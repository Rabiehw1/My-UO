using ClassicUO.Configuration;

namespace ClassicUO.Game.Managers;

public static class GlobalActionCooldown
{
    private static long _nextActionTime;
    private static long _nextAutoLootTime;
    private static long _cooldownDuration => ProfileManager.CurrentProfile?.MoveMultiObjectDelay ?? 1000;
    public static long CooldownDuration => _cooldownDuration;

    public static bool IsOnCooldown => Time.Ticks < _nextActionTime;
    internal static bool IsOnAutoLootCooldown => Time.Ticks < _nextAutoLootTime;

    public static void BeginCooldown() => BeginCooldown(autoLoot: false);

    internal static void BeginCooldown(bool autoLoot)
    {
        long now = Time.Ticks;
        if (ProfileManager.CurrentProfile?.AutoLootItemMoveDelay is not int lootDelay)
        {
            _nextActionTime = _nextAutoLootTime = now + _cooldownDuration;
        }
        else if (autoLoot)
        {
            _nextAutoLootTime = now + lootDelay;
        }
        else
        {
            _nextActionTime = now + _cooldownDuration;
        }
    }
}
