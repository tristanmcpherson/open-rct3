// Splines Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using System.Numerics;
using OpenCobra.OVL;
using OpenCobra.OVL.Files;
using OVL.Tests;

namespace OpenCobra.Tests.OVL;

[TestFixture]
public class SplinesTests {
  [Test]
  public void Decode_OpenSplinePreservesExactNodeAndSegmentOrdering() {
    var spline = new SplineFixture(cyclic: false).Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(spline.Name, Is.EqualTo("synthetic"));
      Assert.That(spline.Cyclic, Is.False);
      Assert.That(spline.TotalLength, Is.EqualTo(5f));
      Assert.That(spline.InverseTotalLength, Is.EqualTo(0.2f));
      Assert.That(spline.MaximumY, Is.EqualTo(20f));
      Assert.That(spline.Nodes, Has.Count.EqualTo(2));
      Assert.That(spline.Nodes[0], Is.EqualTo(new SplineNode(
        new Vector3(1, 2, 3),
        new Vector3(-1, -2, -3),
        new Vector3(4, 5, 6))));
      Assert.That(spline.Nodes[1], Is.EqualTo(new SplineNode(
        new Vector3(7, 8, 9),
        new Vector3(-4, -5, -6),
        new Vector3(10, 11, 12))));
      Assert.That(spline.Segments, Has.Count.EqualTo(1));
      Assert.That(spline.Segments[0].Length, Is.EqualTo(5f));
      Assert.That(spline.Segments[0].TravelData,
        Is.EqualTo(Enumerable.Range(1, 14).Select(Convert.ToByte).ToArray()));
    }
  }

  [Test]
  public void Decode_CyclicSplineUsesOneSegmentPerNode() {
    var spline = new SplineFixture(cyclic: true).Decode();

    using (Assert.EnterMultipleScope()) {
      Assert.That(spline.Cyclic, Is.True);
      Assert.That(spline.Nodes, Has.Count.EqualTo(2));
      Assert.That(spline.Segments.Select(segment => segment.Length),
        Is.EqualTo(new[] { 5f, 6f }));
      Assert.That(spline.Segments[1].TravelData,
        Is.EqualTo(Enumerable.Range(15, 14).Select(Convert.ToByte).ToArray()));
    }
  }

  [Test]
  public void Decode_EnforcesAggregateByteBudget() {
    var fixture = new SplineFixture(cyclic: false);

    Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(new SplineDecodeLimits(31, 100))));
  }

  [Test]
  public void Decode_EnforcesCompleteModelObjectBudgetBeforeAllocation() {
    var fixture = new SplineFixture(cyclic: false);

    Assert.Throws<InvalidDataException>(new Action(() =>
      fixture.Decode(new SplineDecodeLimits(1_000, 6))));
  }

  [Test]
  public void Extract_PreflightsCompleteLoaderIndexObjectCharge() {
    using var ovl = new Ovl("fixture");
    ((List<OvlLoaderEntry>)ovl.LoaderEntriesInOrder).Add(
      new OvlLoaderEntry("spl", 1_000, "fixture.common.ovl", 900));

    Assert.Throws<InvalidDataException>(new Action(() =>
      Splines.Extract(ovl, new SplineDecodeLimits(1_000, 3))));
  }

  [TestCase(MalformedSpline.TruncatedHeader)]
  [TestCase(MalformedSpline.ZeroNodeCount)]
  [TestCase(MalformedSpline.ExcessiveNodeCount)]
  [TestCase(MalformedSpline.UnsupportedCyclicFlag)]
  [TestCase(MalformedSpline.MissingNodeRelocation)]
  [TestCase(MalformedSpline.MismatchedNodeRelocation)]
  [TestCase(MalformedSpline.ZeroNodeTarget)]
  [TestCase(MalformedSpline.TruncatedNodes)]
  [TestCase(MalformedSpline.NonFiniteTotalLength)]
  [TestCase(MalformedSpline.NonFiniteInverseTotalLength)]
  [TestCase(MalformedSpline.NonFiniteMaximumY)]
  [TestCase(MalformedSpline.NonFinitePosition)]
  [TestCase(MalformedSpline.NonFinitePreviousControl)]
  [TestCase(MalformedSpline.NonFiniteNextControl)]
  [TestCase(MalformedSpline.MissingLengthRelocation)]
  [TestCase(MalformedSpline.MismatchedLengthRelocation)]
  [TestCase(MalformedSpline.TruncatedLengths)]
  [TestCase(MalformedSpline.NonFiniteSegmentLength)]
  [TestCase(MalformedSpline.MissingTravelRelocation)]
  [TestCase(MalformedSpline.MismatchedTravelRelocation)]
  [TestCase(MalformedSpline.TruncatedTravelData)]
  [TestCase(MalformedSpline.WrongLoaderTag)]
  public void Decode_RejectsMalformedCountsRelocationsAndGeometry(MalformedSpline malformed) {
    var fixture = new SplineFixture(cyclic: false);
    fixture.MakeMalformed(malformed);

    Assert.Throws<InvalidDataException>(new Action(() => fixture.Decode()));
  }

  [TestCase("Track16")]
  [TestCase("Track59")]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Extract_FromInstalledCoasterArchiveDecodesExactSplines(string archive) {
    var rct3Path = Environment.GetEnvironmentVariable("RCT3_PATH")!;
    Assert.That(rct3Path, Is.Not.Null.And.Not.Empty, "RCT3_PATH is not configured.");
    var path = Path.Combine(
      rct3Path,
      "Tracks",
      "coasters",
      archive,
      $"{archive}.common.ovl");
    Assert.That(path, Does.Exist, $"Installed {archive} OVL not found: {path}");

    using var ovl = Ovl.Load(path);
    var splines = Splines.Extract(ovl);

    TestContext.Progress.WriteLine(
      $"Installed SPL evidence {archive}: splines={splines.Count}, " +
      $"nodes={splines.Sum(spline => spline.Nodes.Count)}, " +
      $"segments={splines.Sum(spline => spline.Segments.Count)}, " +
      $"cyclic={splines.Count(spline => spline.Cyclic)}, " +
      $"maxNodes={splines.Max(spline => spline.Nodes.Count)}");
    using (Assert.EnterMultipleScope()) {
      Assert.That(splines, Is.Not.Empty);
      Assert.That(splines.All(spline => spline.Nodes.Count > 0), Is.True);
      Assert.That(splines.All(spline => spline.Segments.Count ==
        spline.Nodes.Count - (spline.Cyclic ? 0 : 1)), Is.True);
      Assert.That(splines.SelectMany(spline => spline.Nodes).All(IsFinite), Is.True);
      Assert.That(splines.SelectMany(spline => spline.Segments)
        .All(segment => float.IsFinite(segment.Length) && segment.TravelData.Count == 14), Is.True);
    }
  }

  private static bool IsFinite(SplineNode node) =>
    IsFinite(node.Position) &&
    IsFinite(node.PreviousControlOffset) &&
    IsFinite(node.NextControlOffset);

  private static bool IsFinite(Vector3 value) =>
    float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

  private sealed class SplineFixture {
    private const uint HeaderAddress = 1_000;
    private const uint NodesAddress = 2_000;
    private const uint LengthsAddress = 3_000;
    private const uint TravelAddress = 4_000;
    private const string SourcePath = "fixture.common.ovl";

    private readonly byte[] header = new byte[32];
    private readonly byte[] nodes = new byte[72];
    private readonly byte[] lengths;
    private readonly byte[] travel;
    private readonly FakeSplineDataSource source = new();
    private OvlLoaderEntry owner = new("spl", HeaderAddress, SourcePath, 900);

    public SplineFixture(bool cyclic) {
      var segmentCount = cyclic ? 2 : 1;
      lengths = new byte[segmentCount * sizeof(float)];
      travel = new byte[segmentCount * 14];

      WriteUInt32(header, 0, 2);
      WriteUInt32(header, 4, NodesAddress);
      WriteUInt32(header, 8, cyclic ? 1u : 0u);
      WriteSingle(header, 12, cyclic ? 11f : 5f);
      WriteSingle(header, 16, cyclic ? 1f / 11f : 0.2f);
      WriteUInt32(header, 20, LengthsAddress);
      WriteUInt32(header, 24, TravelAddress);
      WriteSingle(header, 28, 20f);

      WriteVector(nodes, 0, new Vector3(1, 2, 3));
      WriteVector(nodes, 12, new Vector3(-1, -2, -3));
      WriteVector(nodes, 24, new Vector3(4, 5, 6));
      WriteVector(nodes, 36, new Vector3(7, 8, 9));
      WriteVector(nodes, 48, new Vector3(-4, -5, -6));
      WriteVector(nodes, 60, new Vector3(10, 11, 12));
      WriteSingle(lengths, 0, 5f);
      if (cyclic) WriteSingle(lengths, 4, 6f);
      foreach (var index in Enumerable.Range(0, travel.Length))
        travel[index] = Convert.ToByte(index + 1);

      source.SetData(HeaderAddress, header);
      source.SetData(NodesAddress, nodes);
      source.SetData(LengthsAddress, lengths);
      source.SetData(TravelAddress, travel);
      source.SetRelocation(HeaderAddress + 4, NodesAddress);
      source.SetRelocation(HeaderAddress + 20, LengthsAddress);
      source.SetRelocation(HeaderAddress + 24, TravelAddress);
    }

    public Spline Decode() => Splines.Decode("synthetic", owner, source);

    public Spline Decode(SplineDecodeLimits limits) =>
      Splines.Decode("synthetic", owner, source, limits);

    public void MakeMalformed(MalformedSpline malformed) {
      switch (malformed) {
        case MalformedSpline.TruncatedHeader:
          source.SetData(HeaderAddress, header[..31]);
          break;
        case MalformedSpline.ZeroNodeCount:
          WriteUInt32(header, 0, 0);
          break;
        case MalformedSpline.ExcessiveNodeCount:
          WriteUInt32(header, 0, 1_000_001);
          break;
        case MalformedSpline.UnsupportedCyclicFlag:
          WriteUInt32(header, 8, 2);
          break;
        case MalformedSpline.MissingNodeRelocation:
          source.RemoveRelocation(HeaderAddress + 4);
          break;
        case MalformedSpline.MismatchedNodeRelocation:
          source.SetRelocation(HeaderAddress + 4, NodesAddress + 4);
          break;
        case MalformedSpline.ZeroNodeTarget:
          WriteUInt32(header, 4, 0);
          source.SetRelocation(HeaderAddress + 4, 0);
          break;
        case MalformedSpline.TruncatedNodes:
          source.SetData(NodesAddress, nodes[..71]);
          break;
        case MalformedSpline.NonFiniteTotalLength:
          WriteSingle(header, 12, float.NaN);
          break;
        case MalformedSpline.NonFiniteInverseTotalLength:
          WriteSingle(header, 16, float.PositiveInfinity);
          break;
        case MalformedSpline.NonFiniteMaximumY:
          WriteSingle(header, 28, float.NegativeInfinity);
          break;
        case MalformedSpline.NonFinitePosition:
          WriteSingle(nodes, 0, float.NaN);
          break;
        case MalformedSpline.NonFinitePreviousControl:
          WriteSingle(nodes, 12, float.PositiveInfinity);
          break;
        case MalformedSpline.NonFiniteNextControl:
          WriteSingle(nodes, 24, float.NegativeInfinity);
          break;
        case MalformedSpline.MissingLengthRelocation:
          source.RemoveRelocation(HeaderAddress + 20);
          break;
        case MalformedSpline.MismatchedLengthRelocation:
          source.SetRelocation(HeaderAddress + 20, LengthsAddress + 4);
          break;
        case MalformedSpline.TruncatedLengths:
          source.SetData(LengthsAddress, lengths[..3]);
          break;
        case MalformedSpline.NonFiniteSegmentLength:
          WriteSingle(lengths, 0, float.NaN);
          break;
        case MalformedSpline.MissingTravelRelocation:
          source.RemoveRelocation(HeaderAddress + 24);
          break;
        case MalformedSpline.MismatchedTravelRelocation:
          source.SetRelocation(HeaderAddress + 24, TravelAddress + 1);
          break;
        case MalformedSpline.TruncatedTravelData:
          source.SetData(TravelAddress, travel[..13]);
          break;
        case MalformedSpline.WrongLoaderTag:
          owner = owner with { Tag = "tks" };
          break;
        default:
          throw new ArgumentOutOfRangeException(nameof(malformed), malformed, null);
      }
    }

    private static void WriteUInt32(byte[] bytes, int offset, uint value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteSingle(byte[] bytes, int offset, float value) =>
      BitConverter.GetBytes(value).CopyTo(bytes, offset);

    private static void WriteVector(byte[] bytes, int offset, Vector3 value) {
      WriteSingle(bytes, offset, value.X);
      WriteSingle(bytes, offset + 4, value.Y);
      WriteSingle(bytes, offset + 8, value.Z);
    }
  }

  private sealed class FakeSplineDataSource : ISplineDataSource {
    private readonly Dictionary<uint, byte[]> data = [];
    private readonly Dictionary<uint, uint> relocations = [];

    public void SetData(uint address, byte[] bytes) => data[address] = bytes;

    public void SetRelocation(uint address, uint target) => relocations[address] = target;

    public void RemoveRelocation(uint address) => relocations.Remove(address);

    public bool TryReadBytes(uint address, int length, out byte[] bytes) {
      foreach (var pair in data) {
        if (address < pair.Key) continue;
        var offset = Convert.ToUInt64(address - pair.Key);
        if (offset > Convert.ToUInt64(pair.Value.Length) ||
            Convert.ToUInt64(length) > Convert.ToUInt64(pair.Value.Length) - offset)
          continue;
        bytes = pair.Value.AsSpan(Convert.ToInt32(offset), length).ToArray();
        return true;
      }
      bytes = [];
      return false;
    }

    public bool TryGetRelocationSource(uint address, out uint value) =>
      relocations.TryGetValue(address, out value);
  }
}

public enum MalformedSpline {
  TruncatedHeader,
  ZeroNodeCount,
  ExcessiveNodeCount,
  UnsupportedCyclicFlag,
  MissingNodeRelocation,
  MismatchedNodeRelocation,
  ZeroNodeTarget,
  TruncatedNodes,
  NonFiniteTotalLength,
  NonFiniteInverseTotalLength,
  NonFiniteMaximumY,
  NonFinitePosition,
  NonFinitePreviousControl,
  NonFiniteNextControl,
  MissingLengthRelocation,
  MismatchedLengthRelocation,
  TruncatedLengths,
  NonFiniteSegmentLength,
  MissingTravelRelocation,
  MismatchedTravelRelocation,
  TruncatedTravelData,
  WrongLoaderTag,
}
