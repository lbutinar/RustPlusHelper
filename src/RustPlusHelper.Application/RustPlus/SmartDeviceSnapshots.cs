namespace RustPlusHelper.Application.RustPlus;

/// <summary>Live state for a Smart Switch or Smart Alarm — both share the same direct boolean
/// "value" shape in the verified Rust+ protocol (see docs/protocol-evidence.md).</summary>
public sealed record SmartDeviceStateSnapshot(ulong EntityId, bool Value);

public sealed record StorageItemSnapshot(int ItemId, int Quantity, bool IsBlueprint);

public sealed record StorageMonitorStateSnapshot(
    ulong EntityId,
    int? Capacity,
    bool? HasProtection,
    IReadOnlyList<StorageItemSnapshot> Items);

/// <summary>
/// A live broadcast for any paired entity. Rust+'s broadcast carries no entity type of its own —
/// callers must know the entity's kind (from where it was paired) before deciding whether to read
/// <see cref="Value"/> (Switch/Alarm) or <see cref="Capacity"/>/<see cref="Items"/> (Storage
/// Monitor); never infer the kind from which fields happen to be populated.
/// </summary>
public sealed record EntityStateChangedSnapshot(
    ulong EntityId,
    bool? Value,
    int? Capacity,
    bool? HasProtection,
    IReadOnlyList<StorageItemSnapshot> Items);
