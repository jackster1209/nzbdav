using NzbWebDAV.Clients.Usenet.Concurrency;
using NzbWebDAV.Clients.Usenet.Connections;
using UsenetSharp.Concurrency;

namespace NzbWebDAV.Tests.Clients.Usenet;

public class ConcurrencyTests
{
    [Fact]
    public void ProviderCircuitBreaker_TripsAfterThreeFailuresAndResetsOnSuccess()
    {
        var breaker = new ProviderCircuitBreaker("test");

        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.False(breaker.IsTripped);

        breaker.RecordFailure();
        Assert.True(breaker.IsTripped);

        breaker.RecordSuccess();
        Assert.False(breaker.IsTripped);
        Assert.Equal(0, breaker.TrippedUntilMs);
        Assert.Equal(TimeSpan.FromSeconds(60), breaker.CurrentCooldown);
    }

    [Fact]
    public void ProviderCircuitBreaker_BurstOfFailuresProducesExactlyOneTrip()
    {
        var breaker = new ProviderCircuitBreaker("burst");

        for (var i = 0; i < 20; i++)
            breaker.RecordFailure();

        Assert.True(breaker.IsTripped);
        // First trip uses the initial 60s cooldown; ladder advances once for the next trip.
        Assert.Equal(TimeSpan.FromSeconds(120), breaker.CurrentCooldown);
    }

    [Fact]
    public void ProviderCircuitBreaker_FailuresWhileTrippedDoNotExtendWindow()
    {
        var breaker = new ProviderCircuitBreaker("latched");

        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.True(breaker.IsTripped);

        var trippedUntil = breaker.TrippedUntilMs;
        Assert.True(trippedUntil > 0);

        for (var i = 0; i < 10; i++)
            breaker.RecordFailure();

        Assert.Equal(trippedUntil, breaker.TrippedUntilMs);
        Assert.Equal(TimeSpan.FromSeconds(120), breaker.CurrentCooldown);
    }

    [Fact]
    public void ProviderCircuitBreaker_SuccessResetsCooldownLadder()
    {
        var breaker = new ProviderCircuitBreaker("ladder");

        // Trip once so the next-trip cooldown doubles to 120s.
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.Equal(TimeSpan.FromSeconds(120), breaker.CurrentCooldown);

        breaker.RecordSuccess();
        Assert.Equal(TimeSpan.FromSeconds(60), breaker.CurrentCooldown);

        // After reset, need a full threshold of fresh failures to trip again.
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.False(breaker.IsTripped);
        breaker.RecordFailure();
        Assert.True(breaker.IsTripped);
        Assert.Equal(TimeSpan.FromSeconds(120), breaker.CurrentCooldown);
    }

    [Fact]
    public void ProviderCircuitBreaker_HalfOpenAdmitsExactlyOneProbe()
    {
        var breaker = new ProviderCircuitBreaker("half-open");
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.True(breaker.IsTripped);

        breaker.ExpireCooldownForTests();

        Assert.False(breaker.IsTripped); // first caller wins the probe
        Assert.True(breaker.IsTripped);  // everyone else stays blocked
        Assert.True(breaker.IsTripped);
    }

    [Fact]
    public void ProviderCircuitBreaker_ProbeFailureReTripsWithDoubledCooldown()
    {
        var breaker = new ProviderCircuitBreaker("probe-fail");
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.Equal(TimeSpan.FromSeconds(120), breaker.CurrentCooldown);

        breaker.ExpireCooldownForTests();
        Assert.False(breaker.IsTripped); // take probe

        breaker.RecordFailure();
        Assert.True(breaker.IsTripped);
        Assert.True(breaker.TrippedUntilMs > Environment.TickCount64);
        // Re-trip used the 120s cooldown, then ladder advanced to 240s.
        Assert.Equal(TimeSpan.FromSeconds(240), breaker.CurrentCooldown);
    }

    [Fact]
    public void ProviderCircuitBreaker_ProbeSuccessFullyCloses()
    {
        var breaker = new ProviderCircuitBreaker("probe-ok");
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        Assert.Equal(TimeSpan.FromSeconds(120), breaker.CurrentCooldown);

        breaker.ExpireCooldownForTests();
        Assert.False(breaker.IsTripped); // take probe

        breaker.RecordSuccess();
        Assert.False(breaker.IsTripped);
        Assert.Equal(0, breaker.TrippedUntilMs);
        Assert.Equal(TimeSpan.FromSeconds(60), breaker.CurrentCooldown);

        // Further callers are fully open again (not stuck behind a half-open slot).
        Assert.False(breaker.IsTripped);
        Assert.False(breaker.IsTripped);
    }

    [Fact]
    public void ProviderCircuitBreaker_AbandonedProbeCanBeRetaken()
    {
        var breaker = new ProviderCircuitBreaker("probe-abandon")
        {
            ProbeAbandonTimeout = TimeSpan.FromMilliseconds(50),
        };
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.RecordFailure();
        breaker.ExpireCooldownForTests();

        Assert.False(breaker.IsTripped); // first probe, then abandoned
        Assert.True(breaker.IsTripped);

        Thread.Sleep(80);
        Assert.False(breaker.IsTripped); // reclaimed
        Assert.True(breaker.IsTripped);
    }

    [Fact]
    public async Task PrioritizedSemaphore_BlocksUntilPermitIsReleased()
    {
        using var semaphore = new PrioritizedSemaphore(initialAllowed: 1, maxAllowed: 1);
        await semaphore.WaitAsync(SemaphorePriority.High);
        var waiter = semaphore.WaitAsync(SemaphorePriority.Low);

        Assert.False(waiter.IsCompleted);
        semaphore.Release();
        await waiter.WaitAsync(TimeSpan.FromSeconds(1));
        semaphore.Release();
    }

    [Fact]
    public async Task PrioritizedSemaphore_RemovesCanceledWaiter()
    {
        using var semaphore = new PrioritizedSemaphore(initialAllowed: 0, maxAllowed: 1);
        using var cts = new CancellationTokenSource();
        var waiter = semaphore.WaitAsync(SemaphorePriority.High, cts.Token);

        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiter);
        semaphore.Release();
    }

    [Fact]
    public async Task PrioritizedSemaphore_DisposeFaultsQueuedWaiters()
    {
        var semaphore = new PrioritizedSemaphore(initialAllowed: 0, maxAllowed: 1);
        var waiter = semaphore.WaitAsync(SemaphorePriority.High);

        semaphore.Dispose();

        await Assert.ThrowsAsync<ObjectDisposedException>(() => waiter);
    }
}
