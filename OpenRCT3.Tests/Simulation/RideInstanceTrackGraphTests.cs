// Ride Instance Track Graph Tests
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved.

using OpenRCT3.Serialization;
using OpenRCT3.Simulation;

namespace OpenRCT3.Tests.Simulation;

[TestFixture]
public class RideInstanceTrackGraphTests {
  [Test]
  public void Build_LinksExactReciprocalEntryIdsInInstanceOrder() {
    var firstInstance = Instance(entryId: 900, track: 700);
    var secondInstance = Instance(entryId: 901, track: 701);
    var firstTrack = Track(entryId: 700, instanceReference: 900);
    var secondTrack = Track(entryId: 701, instanceReference: 901);

    var graph = RideInstanceTrackGraph.Build(
      [firstInstance, secondInstance],
      [secondTrack, firstTrack]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(graph.Links, Has.Count.EqualTo(2));
      Assert.That(graph.Links[0].Instance, Is.SameAs(firstInstance));
      Assert.That(graph.Links[0].Track, Is.SameAs(firstTrack));
      Assert.That(graph.Links[0].InstanceEntryId, Is.EqualTo(900));
      Assert.That(graph.Links[0].TrackEntryId, Is.EqualTo(700));
      Assert.That(graph.Links[1].Instance, Is.SameAs(secondInstance));
      Assert.That(graph.Links[1].Track, Is.SameAs(secondTrack));
    }
  }

  [Test]
  public void Build_RejectsDuplicateInstanceEntryIds() {
    var first = Instance(entryId: 900, track: 700);
    var duplicate = Instance(entryId: 900, track: 701);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackGraph.Build(
        [first, duplicate],
        [Track(700, 900), Track(701, 900)])));
  }

  [Test]
  public void Build_RejectsDuplicateTrackEntryIds() {
    var first = Track(entryId: 700, instanceReference: 900);
    var duplicate = Track(entryId: 700, instanceReference: 901);

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackGraph.Build(
        [Instance(900, 700), Instance(901, 701)],
        [first, duplicate])));
  }

  [Test]
  public void Build_RejectsDanglingInstanceToTrackReference() {
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackGraph.Build(
        [Instance(entryId: 900, track: 999)],
        [Track(entryId: 700, instanceReference: 900)])));
  }

  [Test]
  public void Build_RejectsDanglingTrackToInstanceReference() {
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackGraph.Build(
        [Instance(entryId: 900, track: 700)],
        [
          Track(entryId: 700, instanceReference: 900),
          Track(entryId: 701, instanceReference: 999),
        ])));
  }

  [Test]
  public void Build_RejectsCrossLinkedReciprocalIdentities() {
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackGraph.Build(
        [Instance(900, 700), Instance(901, 701)],
        [Track(700, 901), Track(701, 900)])));
  }

  [Test]
  public void Build_RejectsUnprovenZeroInstanceTrackReference() {
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackGraph.Build(
        [Instance(entryId: 900, track: 0)],
        [Track(entryId: 700, instanceReference: 900)])));
  }

  [Test]
  public void Build_RejectsUnprovenZeroTrackInstanceReference() {
    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackGraph.Build(
        [Instance(entryId: 900, track: 700)],
        [Track(entryId: 700, instanceReference: 0)])));
  }

  [Test]
  public void Build_RejectsNodeCountAboveBoundBeforeIndexing() {
    var instances = Enumerable.Repeat(Instance(900, 700), 100_001).ToArray();

    Assert.Throws<InvalidDataException>(new Action(() =>
      RideInstanceTrackGraph.Build(instances, [])));
  }

  [Test]
  [Explicit("Requires installed RCT3 assets via RCT3_PATH.")]
  public void Build_InstalledCampaignsHaveExactNonSentinelReciprocalLinks() {
    var root = Environment.GetEnvironmentVariable("RCT3_PATH");
    Assert.That(
      string.IsNullOrWhiteSpace(root),
      Is.False,
      "RCT3_PATH must identify an installed RCT3 directory.");
    Assert.That(
      Directory.Exists(root),
      Is.True,
      "RCT3_PATH must identify an installed RCT3 directory.");

    foreach (var campaign in InstalledCampaigns) {
      var path = Path.Combine(
        root!,
        campaign.RelativePath.Replace('/', Path.DirectorySeparatorChar));
      Assert.That(File.Exists(path), Is.True, path);
      var data = DatTerrainReader.Read(path);
      var tracks = data.RideTracks.Select(SemanticTrack).ToArray();

      var graph = RideInstanceTrackGraph.Build(data.TrackedRideInstances, tracks);
      var actualLinks = graph.Links.Select(link => new InstalledLink(
        link.InstanceEntryId,
        link.TrackEntryId,
        link.Track.TrackedRideInstanceReference)).ToArray();

      using (Assert.EnterMultipleScope()) {
        Assert.That(actualLinks, Is.EquivalentTo(campaign.Links), campaign.RelativePath);
        Assert.That(graph.Links, Has.Count.EqualTo(data.TrackedRideInstances.Count));
        Assert.That(graph.Links, Has.Count.EqualTo(data.RideTracks.Count));
        Assert.That(
          graph.Links.All(link => link.InstanceEntryId != 0),
          Is.True,
          campaign.RelativePath);
        Assert.That(
          graph.Links.All(link => link.TrackEntryId != 0),
          Is.True,
          campaign.RelativePath);
        Assert.That(graph.Links.All(link =>
          link.Instance.Track == link.Track.SourceEntryId), Is.True, campaign.RelativePath);
        Assert.That(graph.Links.All(link =>
          link.Track.TrackedRideInstanceReference == link.Instance.EntryId),
          Is.True,
          campaign.RelativePath);
      }

      foreach (var link in graph.Links) {
        TestContext.Progress.WriteLine(
          $"{campaign.RelativePath}|instance={link.InstanceEntryId}|" +
          $"track={link.TrackEntryId}|" +
          $"back={link.Track.TrackedRideInstanceReference}");
      }
    }
  }

  private static DatTrackedRideInstanceData Instance(ulong entryId, ulong track) => new(
    entryId,
    "Synthetic coaster",
    track,
    "Tracks\\TrackedRides\\Synthetic\\Synthetic",
    "Synthetic:trr",
    nTrains: 1,
    nCarsPerTrain: 4,
    trainSelection: 0,
    trains: [1_000]);

  private static RideTrack Track(ulong entryId, ulong instanceReference) => new(
    entryId,
    direction: 0,
    firstSegmentSourceEntryId: 0,
    lastSegmentSourceEntryId: 0,
    isCircuit: false,
    prototype: false,
    hasSerializedTrackPieceOrder: true,
    trackPieceSourceEntryIds: [],
    segmentSourceEntryIds: [],
    flexiColour0: 0,
    flexiColour1: 0,
    flexiColour2: 0,
    trackedRideInstanceReference: instanceReference,
    flippedTrackSections: null,
    tunnelLightColour: null);

  private static RideTrack SemanticTrack(DatRideTrackData source) => new(
    source.EntryId,
    source.Direction,
    source.FirstSegment,
    source.LastSegment,
    source.IsCircuit,
    source.Prototype,
    source.TrackPieces != null,
    source.TrackPieces?.ToArray() ?? [],
    [],
    source.TrackFlexiColours.Col0,
    source.TrackFlexiColours.Col1,
    source.TrackFlexiColours.Col2,
    source.TrackedRideInstance,
    source.FlippedTrackSections,
    source.TunnelLightColour);

  private static readonly InstalledCampaign[] InstalledCampaigns = [
    new(
      "Campaigns/Base/BoxOffice.dat",
      [new(InstanceEntryId: 3986, TrackEntryId: 4007, ReciprocalInstanceEntryId: 3986)]),
    new(
      "Campaigns/Base/Soaked/Atlantis.dat",
      [
        new(InstanceEntryId: 5679, TrackEntryId: 5699, ReciprocalInstanceEntryId: 5679),
        new(InstanceEntryId: 5889, TrackEntryId: 5908, ReciprocalInstanceEntryId: 5889),
      ]),
    new(
      "Campaigns/Base/Wild/GeminiBasin.dat",
      [
        new(InstanceEntryId: 5636, TrackEntryId: 5639, ReciprocalInstanceEntryId: 5636),
        new(InstanceEntryId: 7794, TrackEntryId: 7797, ReciprocalInstanceEntryId: 7794),
      ]),
  ];

  private sealed record InstalledCampaign(
    string RelativePath,
    IReadOnlyList<InstalledLink> Links);

  private sealed record InstalledLink(
    ulong InstanceEntryId,
    ulong TrackEntryId,
    ulong ReciprocalInstanceEntryId);
}
