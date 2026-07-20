using System.Numerics;
using OpenCobra.GDK.Materials;
using OpenRCT3.OpenGL;

namespace OpenRCT3.Tests.OpenGL;

[TestFixture]
public class RendererDisplayListTests {
  [Test]
  public void OrderDisplayList_DrawsOpaqueThenTransparentBackToFront() {
    var transparentNear = CreateNode(
      "transparent-near",
      MaterialRenderState.AlphaBlend,
      4);
    var opaqueFirst = CreateNode(
      "opaque-first",
      MaterialRenderState.Opaque,
      1);
    var transparentFar = CreateNode(
      "transparent-far",
      MaterialRenderState.AlphaBlend,
      64);
    var opaqueSecond = CreateNode(
      "opaque-second",
      MaterialRenderState.Opaque,
      100);
    var masked = CreateNode(
      "masked",
      MaterialRenderState.AlphaMask,
      256);

    var ordered = Renderer.OrderDisplayList([
      transparentNear,
      opaqueFirst,
      transparentFar,
      masked,
      opaqueSecond,
    ]);

    Assert.That(
      ordered.Select(node => node.Name),
      Is.EqualTo(new[] {
        "opaque-first",
        "masked",
        "opaque-second",
        "transparent-far",
        "transparent-near",
      }));
  }

  [Test]
  public void OrderDisplayList_PreservesTransparentOrderAtEqualDistance() {
    var first = CreateNode("first", MaterialRenderState.AlphaBlend, 16);
    var second = CreateNode("second", MaterialRenderState.AlphaBlend, 16);

    var ordered = Renderer.OrderDisplayList([first, second]);

    Assert.That(
      ordered.Select(node => node.Name),
      Is.EqualTo(new[] { "first", "second" }));
  }

  [Test]
  public void OrderDisplayList_PreservesPerDrawFaceCullingState() {
    var singleSided = CreateNode(
      "single-sided",
      MaterialRenderState.Opaque with { CullBackFaces = true },
      1);
    var doubleSided = CreateNode("double-sided", MaterialRenderState.Opaque, 2);

    var ordered = Renderer.OrderDisplayList([singleSided, doubleSided]);

    using (Assert.EnterMultipleScope()) {
      Assert.That(ordered[0].RenderState.CullBackFaces, Is.True);
      Assert.That(ordered[1].RenderState.CullBackFaces, Is.False);
    }
  }

  private static DrawNode CreateNode(
    string name,
    MaterialRenderState renderState,
    float cameraDistanceSquared
  ) => new(
    Name: name,
    Vao: 1,
    Vbo: 2,
    TextureHandle: null,
    ShaderHandle: 3,
    IndexCount: 3,
    ModelTransform: Matrix4x4.Identity,
    RenderState: renderState,
    CameraDistanceSquared: cameraDistanceSquared);
}
