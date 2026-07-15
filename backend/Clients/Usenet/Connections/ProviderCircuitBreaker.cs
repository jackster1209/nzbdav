using Serilog;

namespace NzbWebDAV.Clients.Usenet.Connections;

/// <summary>
/// Tracks recent BODY/ARTICLE outcomes for an NNTP provider and temporarily
/// disables it when a failure threshold is reached, preventing a single
/// misbehaving provider from blocking the entire download pipeline.
/// <para>
/// Failures accumulate in a short sliding window between successes. A success
/// clears the window and fully resets the cooldown ladder (same recovery
/// semantics as the former consecutive-failure breaker). After tripping, the
/// provider enters a cooldown during which it is skipped and additional
/// failures are ignored (latched). When the cooldown expires, exactly one
/// half-open probe is admitted; other callers keep seeing the provider as
/// tripped until that probe records success or failure. A failed probe
/// re-trips with the doubled cooldown. An abandoned probe (no outcome within
/// <see cref="ProbeAbandonTimeout"/>) can be retaken.
/// </para>
/// </summary>
public class ProviderCircuitBreaker
{
    private const int WindowSeconds = 30;
    private const int MinFailuresToTrip = 3;
    private const double TripFailureRate = 0.5;

    private static readonly TimeSpan InitialCooldown = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan MaxCooldown = TimeSpan.FromMinutes(5);
    private static readonly TimeSpan DefaultProbeAbandonTimeout = TimeSpan.FromSeconds(60);

    private readonly string _providerName;
    private readonly object _lock = new();
    private readonly Queue<(long AtMs, bool Failed)> _window = new();

    private long _trippedUntilMs;
    private TimeSpan _currentCooldown = InitialCooldown;
    private int _halfOpenProbeInFlight; // 0/1
    private long _probeStartedMs;

    public ProviderCircuitBreaker(string providerName)
    {
        _providerName = providerName;
    }

    /// <summary>How long an unanswered half-open probe may hold the slot. For tests.</summary>
    internal TimeSpan ProbeAbandonTimeout { get; set; } = DefaultProbeAbandonTimeout;

    public bool IsTripped
    {
        get
        {
            var trippedUntil = Volatile.Read(ref _trippedUntilMs);
            if (trippedUntil == 0) return false;
            if (Environment.TickCount64 < trippedUntil) return true;

            // Cooldown expired → half-open: exactly one caller wins the probe slot.
            TryReclaimAbandonedProbe();
            if (Interlocked.CompareExchange(ref _halfOpenProbeInFlight, 1, 0) == 0)
            {
                Volatile.Write(ref _probeStartedMs, Environment.TickCount64);
                return false; // this caller is the probe
            }

            return true; // another probe is already in flight
        }
    }

    /// <summary>TickCount64 deadline while latched open; 0 when not tripped. For tests.</summary>
    internal long TrippedUntilMs => Volatile.Read(ref _trippedUntilMs);

    /// <summary>Cooldown that will apply on the next trip. For tests.</summary>
    internal TimeSpan CurrentCooldown
    {
        get
        {
            lock (_lock) return _currentCooldown;
        }
    }

    /// <summary>Force the open cooldown into the past so half-open tests can proceed.</summary>
    internal void ExpireCooldownForTests()
    {
        lock (_lock)
        {
            if (_trippedUntilMs > 0)
                _trippedUntilMs = Environment.TickCount64 - 1;
        }
    }

    public void RecordSuccess()
    {
        lock (_lock)
        {
            if (_window.Count > 0 || _trippedUntilMs > 0 || _halfOpenProbeInFlight != 0)
                Log.Information("Provider {Provider} recovered — circuit breaker reset.", _providerName);

            _window.Clear();
            _trippedUntilMs = 0;
            _currentCooldown = InitialCooldown;
            Volatile.Write(ref _halfOpenProbeInFlight, 0);
            Volatile.Write(ref _probeStartedMs, 0);
        }
    }

    public void RecordFailure()
    {
        lock (_lock)
        {
            var now = Environment.TickCount64;

            // Already latched open: ignore in-flight failures from the same burst
            // so they cannot extend the window, double the cooldown, or spam logs.
            if (_trippedUntilMs > 0 && now < _trippedUntilMs)
                return;

            // Half-open probe failed → re-trip immediately with the current
            // (already doubled from the previous trip) cooldown, then advance ladder.
            if (Volatile.Read(ref _halfOpenProbeInFlight) == 1)
            {
                Volatile.Write(ref _halfOpenProbeInFlight, 0);
                Volatile.Write(ref _probeStartedMs, 0);
                Trip(now, "half-open probe failure");
                return;
            }

            // Cooldown expired (or never tripped); clear a stale trip marker so
            // success recovery logging stays accurate.
            if (_trippedUntilMs > 0)
                _trippedUntilMs = 0;

            EvictOldEntries(now);
            _window.Enqueue((now, true));

            var failures = 0;
            foreach (var entry in _window)
                if (entry.Failed) failures++;

            if (failures >= MinFailuresToTrip
                && failures / (double)_window.Count >= TripFailureRate)
            {
                Trip(now, $"{failures} failures in {_window.Count}-sample window");
            }
        }
    }

    private void Trip(long nowMs, string reason)
    {
        _trippedUntilMs = nowMs + (long)_currentCooldown.TotalMilliseconds;
        Log.Warning(
            "Provider {Provider} tripped ({Reason}). Skipping for {Cooldown}s.",
            _providerName, reason, _currentCooldown.TotalSeconds);

        _window.Clear();
        _currentCooldown = TimeSpan.FromMilliseconds(
            Math.Min(_currentCooldown.TotalMilliseconds * 2, MaxCooldown.TotalMilliseconds));
    }

    private void EvictOldEntries(long nowMs)
    {
        var cutoff = nowMs - WindowSeconds * 1000L;
        while (_window.Count > 0 && _window.Peek().AtMs < cutoff)
            _window.Dequeue();
    }

    private void TryReclaimAbandonedProbe()
    {
        if (Volatile.Read(ref _halfOpenProbeInFlight) != 1) return;
        var started = Volatile.Read(ref _probeStartedMs);
        if (started == 0) return;
        if (Environment.TickCount64 - started < (long)ProbeAbandonTimeout.TotalMilliseconds) return;

        // Abandoned probe (cancelled request, etc.): free the slot so another
        // caller can retry. CompareExchange so we don't clear a just-resolved probe.
        if (Interlocked.CompareExchange(ref _halfOpenProbeInFlight, 0, 1) == 1)
            Volatile.Write(ref _probeStartedMs, 0);
    }
}
