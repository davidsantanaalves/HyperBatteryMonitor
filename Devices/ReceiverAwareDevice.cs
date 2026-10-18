using System.Diagnostics;
using HyperXBatteryTray.Hid;

namespace HyperXBatteryTray.Devices;

/// <summary>
/// Resolves a model's dedicated receivers and (where supported) TIO child by
/// verifying a battery request on eligible HID collections. One physical
/// endpoint is active at a time. Previously successful collections are tried
/// first, but are never trusted without a fresh response after reconnection.
/// </summary>
internal sealed class ReceiverAwareDevice : IHyperXDevice
{
    private const ushort SharedTioReceiverPid = 0x01BF;
    private readonly string _modelName;
    private readonly IReadOnlyList<HyperXDeviceRegistration> _registrations;
    private readonly object _lifetimeLock = new();
    private readonly SemaphoreSlim _batteryQueryGate = new(1, 1);
    private IReadOnlyList<ReceiverCandidate> _available = Array.Empty<ReceiverCandidate>();
    private IHyperXDevice? _active;
    private int _activeIndex = -1;
    private volatile bool _verified;
    private bool _disposed;
    private int _generation;

    private sealed record ReceiverCandidate(
        HyperXDeviceRegistration Registration,
        HidConnection.EndpointCandidate Endpoint,
        bool IsDedicated);

    public ReceiverAwareDevice(string modelName, IReadOnlyList<HyperXDeviceRegistration> candidates)
    {
        if (candidates.Count == 0)
            throw new ArgumentException("At least one receiver registration is required.", nameof(candidates));
        _modelName = modelName;
        _registrations = candidates;
    }

    public string Name => _modelName;
    public ushort VendorId => Volatile.Read(ref _active)?.VendorId ?? _registrations[0].Definition.VendorId;
    public ushort ProductId => Volatile.Read(ref _active)?.ProductId ?? _registrations[0].Definition.ProductId;
    public bool IsConnected => Volatile.Read(ref _active)?.IsConnected == true;
    public int Battery => Volatile.Read(ref _active)?.Battery ?? -1;
    public bool IsCharging => Volatile.Read(ref _active)?.IsCharging == true;
    public bool IsMicrophoneMuted => Volatile.Read(ref _active)?.IsMicrophoneMuted == true;
    public bool IsMicrophoneMuteStateKnown => Volatile.Read(ref _active)?.IsMicrophoneMuteStateKnown == true;
    public bool SupportsMicrophoneMuteMonitoring =>
        Volatile.Read(ref _active)?.SupportsMicrophoneMuteMonitoring ??
        _registrations.Any(r => r.Definition.SupportsMicrophoneMuteMonitoring);

    public event EventHandler<int>? BatteryChanged;
    public event EventHandler<bool>? ChargingChanged;
    public event EventHandler<bool>? MicrophoneMuteChanged;

    public bool Connect()
    {
        lock (_lifetimeLock)
        {
            if (_disposed) return false;
            if (_active?.IsConnected == true) return true;
            ResetSession();
            _available = GetCandidates();

            for (int i = 0; i < _available.Count; ++i)
            {
                if (Activate(i)) return true;
            }
            return false;
        }
    }

    private IReadOnlyList<ReceiverCandidate> GetCandidates()
    {
        List<ReceiverCandidate> candidates = new();
        foreach (HyperXDeviceRegistration registration in _registrations)
        {
            HyperXDeviceDefinition definition = registration.Definition;
            foreach (HidConnection.EndpointCandidate endpoint in
                HidConnection.EnumerateControlEndpoints(definition))
            {
                bool isDedicated = definition.ProductId != SharedTioReceiverPid;
                candidates.Add(new ReceiverCandidate(registration, endpoint, isDedicated));
            }
        }

        // Dedicated endpoints always precede TIO. A verified collection on
        // this machine is a priority hint; it still needs a valid battery reply.
        return candidates.OrderByDescending(c => c.IsDedicated)
            .ThenByDescending(c =>
                string.Equals(
                    HidEndpointPreferenceStore.Get(_modelName,
                        c.Registration.Definition.VendorId,
                        c.Registration.Definition.ProductId),
                    c.Endpoint.Collection, StringComparison.OrdinalIgnoreCase) &&
                !string.IsNullOrEmpty(c.Endpoint.Collection))
            .ToArray();
    }

    private bool Activate(int index)
    {
        ReleaseActive();
        ReceiverCandidate candidate = _available[index];
        IHyperXDevice? device = null;
        try
        {
            device = candidate.Registration.Factory();
            // A driver that does not support explicit endpoints retains its
            // legacy exact-match behavior; such registrations have a single path.
            if (device is ISelectableHidEndpoint selectable)
                selectable.SelectHidEndpoint(candidate.Endpoint.Path);
            if (!device.Connect())
            {
                device.Dispose();
                return false;
            }
            _activeIndex = index;
            Volatile.Write(ref _active, device);
            return true;
        }
        catch (Exception ex)
        {
            Debug.WriteLine($"HID connection failed for '{_modelName}': {ex.Message}");
            device?.Dispose();
            return false;
        }
    }

    /// <summary>
    /// Probe endpoints sequentially using their existing parser. Reject a
    /// multi-receiver ambiguity if more than one dedicated candidate reports
    /// a valid battery. A TIO dongle is tested only if no dedicated receiver
    /// succeeded, avoiding an unrelated TIO taking precedence.
    /// </summary>
    public async Task<int?> QueryBatteryAsync(CancellationToken cancellationToken = default)
    {
        await _batteryQueryGate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            int generation;
            lock (_lifetimeLock) generation = _generation;
            int? firstVerifiedDedicated = null;

            while (!cancellationToken.IsCancellationRequested)
            {
                IHyperXDevice? active;
                int index;
                bool wasVerified;
                lock (_lifetimeLock)
                {
                    if (_disposed || generation != _generation) return null;
                    active = _active;
                    index = _activeIndex;
                    wasVerified = _verified;
                }

                if (active == null || index < 0) break;
                int? result;
                try
                {
                    result = await active.QueryBatteryAsync(cancellationToken).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Battery validation failed for '{_modelName}': {ex.Message}");
                    result = null;
                }

                cancellationToken.ThrowIfCancellationRequested();
                lock (_lifetimeLock)
                {
                    if (_disposed || generation != _generation ||
                        !ReferenceEquals(active, _active)) return null;
                }

                if (wasVerified) return result is >= 0 and <= 100 ? result : null;

                bool valid = result is >= 0 and <= 100;
                ReceiverCandidate candidate = _available[index];
                bool anotherDedicated = _available.Skip(index + 1).Any(c => c.IsDedicated);

                if (valid && candidate.IsDedicated && anotherDedicated)
                {
                    // One responsive dedicated receiver is not enough to
                    // select among multiple physical dedicated candidates.
                    if (firstVerifiedDedicated.HasValue)
                    {
                        Debug.WriteLine($"Ambiguous responsive receivers for '{_modelName}'.");
                        lock (_lifetimeLock) ReleaseActive();
                        return null;
                    }
                    firstVerifiedDedicated = index;
                }
                else if (valid)
                {
                    if (firstVerifiedDedicated.HasValue &&
                        firstVerifiedDedicated.Value != index && candidate.IsDedicated)
                    {
                        Debug.WriteLine($"Ambiguous responsive receivers for '{_modelName}'.");
                        lock (_lifetimeLock) ReleaseActive();
                        return null;
                    }
                    if (!firstVerifiedDedicated.HasValue)
                    {
                        lock (_lifetimeLock)
                        {
                            if (generation != _generation) return null;
                            Confirm(index);
                        }
                        return result;
                    }
                }

                // If a dedicated receiver has responded, TIO may belong to an
                // unrelated headset; stop after the other dedicated candidates.
                if (firstVerifiedDedicated.HasValue && !anotherDedicated)
                    break;

                bool moved = false;
                lock (_lifetimeLock)
                {
                    if (generation != _generation) return null;
                    for (int next = index + 1; next < _available.Count; ++next)
                    {
                        if (Activate(next)) { moved = true; break; }
                    }
                }
                if (!moved) break;
            }

            if (firstVerifiedDedicated.HasValue && !cancellationToken.IsCancellationRequested)
            {
                IHyperXDevice? winner;
                int winnerIndex = firstVerifiedDedicated.Value;
                lock (_lifetimeLock)
                {
                    if (generation != _generation || !Activate(winnerIndex)) return null;
                    winner = _active;
                }
                if (winner == null) return null;
                int? rechecked = await winner.QueryBatteryAsync(cancellationToken)
                    .ConfigureAwait(false);
                cancellationToken.ThrowIfCancellationRequested();
                lock (_lifetimeLock)
                {
                    if (generation != _generation || !ReferenceEquals(winner, _active)) return null;
                    if (rechecked is >= 0 and <= 100)
                    {
                        Confirm(winnerIndex);
                        return rechecked;
                    }
                }
            }
            return null;
        }
        finally
        {
            _batteryQueryGate.Release();
        }
    }

    private void Confirm(int index)
    {
        if (_verified || _active == null) return;
        _verified = true;
        _active.BatteryChanged += Active_BatteryChanged;
        _active.ChargingChanged += Active_ChargingChanged;
        _active.MicrophoneMuteChanged += Active_MicrophoneMuteChanged;
        ReceiverCandidate c = _available[index];
        if (c.IsDedicated)
            HidEndpointPreferenceStore.Remember(_modelName,
                c.Registration.Definition.VendorId,
                c.Registration.Definition.ProductId, c.Endpoint.Collection);
    }

    public Task<bool?> QueryChargeStatusAsync(CancellationToken cancellationToken = default) =>
        _verified ? Volatile.Read(ref _active)?.QueryChargeStatusAsync(cancellationToken)
            ?? Task.FromResult<bool?>(null) : Task.FromResult<bool?>(null);

    public Task<bool?> QueryMicrophoneMuteStatusAsync(CancellationToken cancellationToken = default) =>
        _verified ? Volatile.Read(ref _active)?.QueryMicrophoneMuteStatusAsync(cancellationToken)
            ?? Task.FromResult<bool?>(null) : Task.FromResult<bool?>(null);

    private void Active_BatteryChanged(object? sender, int battery)
    {
        if (ReferenceEquals(sender, Volatile.Read(ref _active)))
            BatteryChanged?.Invoke(this, battery);
    }
    private void Active_ChargingChanged(object? sender, bool charging)
    {
        if (ReferenceEquals(sender, Volatile.Read(ref _active)))
            ChargingChanged?.Invoke(this, charging);
    }
    private void Active_MicrophoneMuteChanged(object? sender, bool muted)
    {
        if (ReferenceEquals(sender, Volatile.Read(ref _active)))
            MicrophoneMuteChanged?.Invoke(this, muted);
    }

    private void ReleaseActive()
    {
        IHyperXDevice? old = Interlocked.Exchange(ref _active, null);
        _activeIndex = -1;
        _verified = false;
        if (old == null) return;
        old.BatteryChanged -= Active_BatteryChanged;
        old.ChargingChanged -= Active_ChargingChanged;
        old.MicrophoneMuteChanged -= Active_MicrophoneMuteChanged;
        old.Dispose();
    }

    private void ResetSession()
    {
        ++_generation;
        ReleaseActive();
        _available = Array.Empty<ReceiverCandidate>();
    }

    public void Disconnect()
    {
        lock (_lifetimeLock) ResetSession();
    }

    public void Dispose()
    {
        lock (_lifetimeLock)
        {
            if (_disposed) return;
            _disposed = true;
            ResetSession();
        }
        // Never dispose the query gate while a QueryBatteryAsync continuation
        // might still need to release it.
    }
}
