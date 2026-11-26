#nullable enable

namespace ClassicUO.Game.Managers.Support;

internal sealed class RangerPetOrder
{
    private const int CursorWaitMs = 1750;
    private const int RetryDelayMs = 750;
    private const int MaximumAttempts = 2;
    private long _deadline;
    private long _retryAt;
    private int _attempts;
    private bool _submitted;

    internal uint Pet { get; private set; }
    internal uint Enemy { get; private set; }
    internal bool IsWaitingForCursor(long now) => now < _deadline;

    internal bool TryBegin(uint pet, uint enemy, long now)
    {
        if (IsWaitingForCursor(now))
            return false;

        if (Pet != pet || Enemy != enemy)
        {
            Reset();
            Pet = pet;
            Enemy = enemy;
        }

        if (_submitted || _attempts >= MaximumAttempts || now < _retryAt)
            return false;

        _attempts++;
        _deadline = now + CursorWaitMs;
        _retryAt = _deadline + RetryDelayMs;
        return true;
    }

    internal void Complete()
    {
        _deadline = 0;
        _submitted = true;
    }

    internal void Reset()
    {
        _deadline = _retryAt = 0;
        _attempts = 0;
        _submitted = false;
        Pet = Enemy = 0;
    }
}
