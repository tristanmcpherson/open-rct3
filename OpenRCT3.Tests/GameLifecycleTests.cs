namespace OpenRCT3.Tests;

[TestFixture]
public class GameLifecycleTests {
  [Test]
  public void DisposeOwnedResources_AttemptsSceneAndWorldAndAlwaysClearsState() {
    var released = new List<string>();

    var error = Assert.Throws<AggregateException>(new Action(() =>
      Game.DisposeOwnedResources(
        () => {
          released.Add("scene");
          throw new InvalidOperationException("Injected scene disposal failure.");
        },
        () => {
          released.Add("world");
          throw new InvalidOperationException("Injected world disposal failure.");
        },
        () => released.Add("state"))));

    using (Assert.EnterMultipleScope()) {
      Assert.That(error.InnerExceptions, Has.Count.EqualTo(2));
      Assert.That(released, Is.EqualTo(new[] { "scene", "world", "state" }));
    }
  }
}
