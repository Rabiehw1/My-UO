#nullable enable

namespace ClassicUO.Game.Managers.Support;

internal sealed class RangerPetOrder
{
    private const int CursorWaitMs = 1750;
    private long _deadline;

    internal uint Pet { get; private set; }
    internal uint Enemy { get; private set; }
    internal bool IsWaitingForCursor(long now) => now < _deadline;

    internal bool TryBegin(uint pet, uint enemy, long now)
    {
        if (IsWaitingForCursor(now) || (Pet == pet && Enemy == enemy))
            return false;

        Pet = pet;
        Enemy = enemy;
        _deadline = now + CursorWaitMs;
        return true;
    }

    internal bool TakeCursor(long now)
    {
        bool waiting = IsWaitingForCursor(now);
        _deadline = 0;
        return waiting;
    }

    internal void Cancel() => _deadline = 0;

    internal void Reset()
    {
        Cancel();
        Pet = Enemy = 0;
    }
}
