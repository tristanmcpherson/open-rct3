// DAT Game Time Data
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

namespace OpenRCT3.Serialization;

/// <summary>Exact saved simulation and day/night clock state from one DAT GameTime entry.</summary>
internal sealed class DatGameTimeData {
  public ulong EntryId { get; }
  public int DayNightMode { get; }
  public float DayNightTime { get; }
  public float DayNightTimeAsRendered { get; }
  public float Time { get; }
  public float ZeroTime { get; }

  public DatGameTimeData(
    ulong entryId,
    int dayNightMode,
    float dayNightTime,
    float dayNightTimeAsRendered,
    float time,
    float zeroTime
  ) {
    if (!float.IsFinite(dayNightTime))
      throw new ArgumentOutOfRangeException(nameof(dayNightTime));
    if (!float.IsFinite(dayNightTimeAsRendered))
      throw new ArgumentOutOfRangeException(nameof(dayNightTimeAsRendered));
    if (!float.IsFinite(time)) throw new ArgumentOutOfRangeException(nameof(time));
    if (!float.IsFinite(zeroTime)) throw new ArgumentOutOfRangeException(nameof(zeroTime));

    EntryId = entryId;
    DayNightMode = dayNightMode;
    DayNightTime = dayNightTime;
    DayNightTimeAsRendered = dayNightTimeAsRendered;
    Time = time;
    ZeroTime = zeroTime;
  }
}
