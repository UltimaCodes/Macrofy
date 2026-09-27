using Macrofy.Core.Input;
using Xunit;

namespace Macrofy.Tests;

// The block/pass logic is the riskiest code in the app, so it gets the most tests.
public class CaptureDeciderTests
{
    private static void Record(CaptureDecider d, int vk, bool down, bool captured, uint pid, long now)
        => d.Record(vk, down, isRepeat: false, captured, pid, now);

    [Fact]
    public void Blocks_a_captured_key()
    {
        var d = new CaptureDecider();
        Record(d, 0x41, down: true, captured: true, pid: 0, now: 0);
        Assert.True(d.Decide(0x41, isDown: true, isRepeat: false, now: 1));
    }

    [Fact]
    public void Passes_a_non_captured_key()
    {
        var d = new CaptureDecider();
        Record(d, 0x42, down: true, captured: false, pid: 0, now: 0);
        Assert.False(d.Decide(0x42, isDown: true, isRepeat: false, now: 1));
    }

    [Fact]
    public void Passes_when_no_raw_event_was_recorded()
    {
        var d = new CaptureDecider();
        Assert.False(d.Decide(0x43, isDown: true, isRepeat: false, now: 0));
    }

    [Fact]
    public void Generic_modifier_from_hook_matches_left_right_raw()
    {
        var d = new CaptureDecider();
        // Raw Input reports Left Shift (0xA0); the hook reports the generic Shift (0x10).
        Record(d, 0xA0, down: true, captured: true, pid: 0, now: 0);
        Assert.True(d.Decide(0x10, isDown: true, isRepeat: false, now: 1));
    }

    [Fact]
    public void Blocks_auto_repeat_of_a_held_captured_key_without_a_fresh_raw_event()
    {
        var d = new CaptureDecider();
        Record(d, 0x41, down: true, captured: true, pid: 0, now: 0);
        Assert.True(d.Decide(0x41, isDown: true, isRepeat: false, now: 1)); // marks it held
        // A folded repeat arrives with no matching raw event:
        Assert.True(d.Decide(0x41, isDown: true, isRepeat: true, now: 2));
    }

    [Fact]
    public void Does_not_block_repeat_of_a_key_that_was_not_captured()
    {
        var d = new CaptureDecider();
        Record(d, 0x41, down: true, captured: false, pid: 0, now: 0);
        Assert.False(d.Decide(0x41, isDown: true, isRepeat: false, now: 1));
        Assert.False(d.Decide(0x41, isDown: true, isRepeat: true, now: 2));
    }

    [Fact]
    public void Reports_a_miss_when_a_captured_press_is_never_asked_about()
    {
        var d = new CaptureDecider();
        uint? missed = null;
        d.Missed += pid => missed = pid;

        Record(d, 0x41, down: true, captured: true, pid: 1234, now: 0);
        // No hook query arrives; time passes and another event prunes the stale press.
        d.Decide(0x42, isDown: true, isRepeat: false, now: CaptureDecider.TtlMs + 1);

        Assert.Equal(1234u, missed);
    }

    [Fact]
    public void A_blocked_press_is_not_reported_as_a_miss()
    {
        var d = new CaptureDecider();
        bool missed = false;
        d.Missed += _ => missed = true;

        Record(d, 0x41, down: true, captured: true, pid: 1234, now: 0);
        Assert.True(d.Decide(0x41, isDown: true, isRepeat: false, now: 1)); // answered → consumed
        d.Decide(0x42, isDown: true, isRepeat: false, now: CaptureDecider.TtlMs + 100);

        Assert.False(missed);
    }

    [Fact]
    public void A_press_with_pid_zero_is_never_a_miss()
    {
        var d = new CaptureDecider();
        bool missed = false;
        d.Missed += _ => missed = true;

        // pid 0 = the backend already knows no query is coming (e.g. Print Screen).
        Record(d, 0x2C, down: true, captured: true, pid: 0, now: 0);
        d.Decide(0x00, isDown: true, isRepeat: false, now: CaptureDecider.TtlMs + 100);

        Assert.False(missed);
    }

    [Theory]
    [InlineData(0xA0, 0x10)]
    [InlineData(0xA1, 0x10)]
    [InlineData(0xA2, 0x11)]
    [InlineData(0xA3, 0x11)]
    [InlineData(0xA4, 0x12)]
    [InlineData(0xA5, 0x12)]
    [InlineData(0x41, 0x41)]
    public void Canonical_folds_left_right_modifiers(int vk, int expected)
        => Assert.Equal(expected, CaptureDecider.Canonical(vk));

    [Fact]
    public void Reset_clears_held_and_pending_state()
    {
        var d = new CaptureDecider();
        Record(d, 0x41, down: true, captured: true, pid: 0, now: 0);
        d.Decide(0x41, isDown: true, isRepeat: false, now: 1);
        d.Reset();
        // After reset the held-key memory is gone, so a bare repeat is no longer blocked.
        Assert.False(d.Decide(0x41, isDown: true, isRepeat: true, now: 2));
    }
}
