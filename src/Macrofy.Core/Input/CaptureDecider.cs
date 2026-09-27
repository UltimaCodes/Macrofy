namespace Macrofy.Core.Input;

// Pairs the hook's "block this key?" questions with the Raw Input events that say which
// keyboard each key came from. Raw Input for a key is queued before the app's key message,
// so by the time the hook asks, the matching raw event is already recorded. Pure logic (the
// caller supplies timestamps and the foreground process) so it can be unit tested.
//
// Not thread-safe; the backend calls it under its own lock.
public sealed class CaptureDecider
{
    public const long TtlMs = 250;
    private const int MaxPending = 128;

    private readonly List<Pending> _pending = new();
    private readonly HashSet<int> _heldCaptured = new();

    // A captured key press that no app ever asked about before it expired: the foreground
    // app never received it through the hook, so it wasn't blocked there. Arg = that app's pid.
    public event Action<uint>? Missed;

    public void Reset()
    {
        _pending.Clear();
        _heldCaptured.Clear();
    }

    public void Record(int vk, bool isDown, bool isRepeat, bool captured, uint foregroundPid, long now)
    {
        Prune(now);
        if (_pending.Count >= MaxPending)
            _pending.RemoveAt(0);
        _pending.Add(new Pending(Canonical(vk), isDown, isRepeat, captured, foregroundPid, now));
    }

    public bool Decide(int vk, bool isDown, bool isRepeat, long now)
    {
        Prune(now);
        int canon = Canonical(vk);

        int idx = _pending.FindIndex(p => p.CanonVk == canon && p.IsDown == isDown);
        if (idx >= 0)
        {
            var match = _pending[idx];
            _pending.RemoveAt(idx);
            if (match.Captured)
            {
                if (isDown) _heldCaptured.Add(canon);
                else _heldCaptured.Remove(canon);
                return true;
            }
            if (!isDown) _heldCaptured.Remove(canon);
            return false;
        }

        // No raw event to go on. Windows folds auto-repeats into one queued message when an
        // app reads slowly, so a repeat of a key we know is held on the captured keyboard
        // can arrive with no fresh raw event; block it. Anything else passes (never block
        // on doubt).
        if (isDown && isRepeat && _heldCaptured.Contains(canon))
            return true;
        if (!isDown) _heldCaptured.Remove(canon);
        return false;
    }

    private void Prune(long now)
    {
        for (int i = _pending.Count - 1; i >= 0; i--)
        {
            var p = _pending[i];
            if (now - p.Stamp <= TtlMs)
                continue;
            _pending.RemoveAt(i);
            // Only a fresh press counts: repeats and releases are legitimately unmatched when
            // Windows folds repeats, or when the press itself already went unanswered.
            // ForegroundPid 0 means the caller knows no query is coming (e.g. Print Screen).
            if (p.Captured && p.IsDown && !p.IsRepeat && p.ForegroundPid != 0)
                Missed?.Invoke(p.ForegroundPid);
        }
    }

    // Collapse left/right modifiers: the hook reports the generic VK.
    public static int Canonical(int vk) => vk switch
    {
        0xA0 or 0xA1 => 0x10,
        0xA2 or 0xA3 => 0x11,
        0xA4 or 0xA5 => 0x12,
        _ => vk,
    };

    private readonly record struct Pending(int CanonVk, bool IsDown, bool IsRepeat, bool Captured, uint ForegroundPid, long Stamp);
}
