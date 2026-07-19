// Material
//
// Authors:
//   - Chance Snow <git@chancesnow.me>
//
// Copyright © 2026 OpenRCT3 Contributors. All rights reserved

using OpenCobra.GDK.Shaders;
using System.ComponentModel;

namespace OpenCobra.GDK.Materials;

public enum MaterialBlendMode {
  Opaque,
  Alpha
}

public readonly record struct MaterialRenderState(
  MaterialBlendMode BlendMode,
  bool DepthWrite
) {
  public static MaterialRenderState Opaque => new(MaterialBlendMode.Opaque, true);
  public static MaterialRenderState AlphaBlend => new(MaterialBlendMode.Alpha, false);

  [Browsable(false)]
  public bool IsTransparent => BlendMode != MaterialBlendMode.Opaque;
}

public abstract class Material : IResource, IDisposable {
  // FIXME: Inline this into `Material.State`.
  private bool disposed;
  private Texture? albedoTexture;
  private Texture? normalMap;
  private Texture? specularMap;
  private Texture? emissiveMap;
  private IDisposable? albedoLease;
  private IDisposable? normalLease;
  private IDisposable? specularLease;
  private IDisposable? emissiveLease;

  [Category("GPU")]
  public ShaderSource Shaders { get; protected set; }

  [Category("Rendering")]
  public MaterialRenderState RenderState { get; protected set; } = MaterialRenderState.Opaque;

  [Browsable(false)]
  public MaterialCacheKey CacheKey => new(Shaders);

  [Category("Appearance")]
  public Texture? AlbedoTexture {
    get => albedoTexture;
    set => SetTexture(ref albedoTexture, ref albedoLease, value);
  }
  [Category("Appearance")]
  public Texture? NormalMap {
    get => normalMap;
    set => SetTexture(ref normalMap, ref normalLease, value);
  }
  [Category("Appearance")]
  public Texture? SpecularMap {
    get => specularMap;
    set => SetTexture(ref specularMap, ref specularLease, value);
  }
  [Category("Appearance")]
  public Texture? EmissiveMap {
    get => emissiveMap;
    set => SetTexture(ref emissiveMap, ref emissiveLease, value);
  }

  public IEnumerable<Texture> Textures {
    get {
      Texture?[] textures = [AlbedoTexture, NormalMap, SpecularMap, EmissiveMap];
      return textures.Where(t => t != null).Cast<Texture>();
    }
  }

  [Category("GPU")]
  public State State {
    get {
      if (disposed) return State.Disposed;

      var textures = Textures.ToArray();
      if (textures.Length == 0) return State.Ready;
      else if (textures.Any(t => t.State != State.Ready)) return State.Uninitialized;
      else return State.Ready;
    }
  }

  public void Dispose() {
    if (disposed) return;
    disposed = true;
    GC.SuppressFinalize(this);

    // FIXME: Dispose of shader sources
    IDisposable?[] leases = [albedoLease, normalLease, specularLease, emissiveLease];
    foreach (var lease in leases.Where(lease => lease != null).Cast<IDisposable>())
      lease.Dispose();
  }

  private void SetTexture(ref Texture? field, ref IDisposable? lease, Texture? value) {
    ObjectDisposedException.ThrowIf(disposed, this);
    if (ReferenceEquals(field, value)) return;

    var replacementLease = value?.AcquireLease();
    lease?.Dispose();
    field = value;
    lease = replacementLease;
  }
}

public readonly record struct MaterialCacheKey(ShaderSource Shaders);

public class Flat : Material {
  public Flat() {
    // Core-profile GLSL matching SurfaceSettings' CoreProfileBit | ForwardCompatibleBit context: no
    // `attribute`/`varying`/`gl_FragColor`, all removed from core profile. Mixing #version 120
    // compatibility syntax with a forward-compatible core context is driver-dependent — some drivers
    // compile it without error but silently fail to wire up the deprecated built-ins (notably
    // gl_FragColor), which was rendering every fragment black regardless of vertex color.
    var vertexSource = @"#version 410 core
in vec3 a_Position;
in vec4 a_Color;

uniform mat4 u_Model;
uniform mat4 u_ViewProj;

out vec4 v_Color;

void main() {
    gl_Position = u_ViewProj * u_Model * vec4(a_Position, 1.0);
    v_Color = a_Color;
}";
    var fragmentSource = @"#version 410 core
in vec4 v_Color;

out vec4 FragColor;

void main() {
    FragColor = v_Color;
}";

    Shaders = new(vertexSource, fragmentSource);
  }
}

public class Textured : Material {
  public Textured() {
    var vertexSource = @"#version 410 core
in vec3 a_Position;
in vec3 a_Normal;
in vec2 a_TexCoord;
in vec4 a_Color;

uniform mat4 u_Model;
uniform mat4 u_ViewProj;

out vec2 v_TexCoord;
out vec4 v_Color;
out float v_Light;

void main() {
    gl_Position = u_ViewProj * u_Model * vec4(a_Position, 1.0);
    v_TexCoord = a_TexCoord; // FIXME: Flip Y if needed for texture orientation
    v_Color = a_Color;
    vec3 transformedNormal = mat3(transpose(inverse(u_Model))) * a_Normal;
    float normalLength = length(transformedNormal);
    vec3 worldNormal = normalLength > 0.0001
        ? transformedNormal / normalLength
        : vec3(0.0, 0.0, 1.0);
    vec3 lightDirection = normalize(vec3(-0.35, -0.45, 0.82));
    float diffuse = max(dot(worldNormal, lightDirection), 0.0);
    v_Light = 0.45 + (0.55 * diffuse);
}";
    var fragmentSource = @"#version 410 core
uniform sampler2D u_Texture;
in vec2 v_TexCoord;
in vec4 v_Color;
in float v_Light;

out vec4 FragColor;

void main() {
    vec4 texColor = texture(u_Texture, v_TexCoord);
    FragColor = vec4(texColor.rgb * v_Color.rgb * v_Light, texColor.a * v_Color.a);
}";

    Shaders = new(vertexSource, fragmentSource);
  }
}

public class Water : Material {
  public const string CameraPositionUniformName = "u_CameraPosition";

  public Water() {
    RenderState = MaterialRenderState.AlphaBlend;

    var vertexSource = @"#version 410 core
in vec3 a_Position;
in vec3 a_Normal;
in vec4 a_Color;

uniform mat4 u_Model;
uniform mat4 u_ViewProj;

out vec3 v_WorldPosition;
out vec3 v_WorldNormal;
out vec4 v_Color;

void main() {
    vec4 worldPosition = u_Model * vec4(a_Position, 1.0);
    gl_Position = u_ViewProj * worldPosition;
    v_WorldPosition = worldPosition.xyz;
    vec3 transformedNormal = mat3(transpose(inverse(u_Model))) * a_Normal;
    float normalLength = length(transformedNormal);
    v_WorldNormal = normalLength > 0.0001
        ? transformedNormal / normalLength
        : vec3(0.0, 0.0, 1.0);
    v_Color = a_Color;
}";
    var fragmentSource = @"#version 410 core
uniform vec3 u_CameraPosition;

in vec3 v_WorldPosition;
in vec3 v_WorldNormal;
in vec4 v_Color;

out vec4 FragColor;

void main() {
    vec3 normal = normalize(v_WorldNormal);
    vec3 viewDirection = normalize(u_CameraPosition - v_WorldPosition);
    vec3 lightDirection = normalize(vec3(-0.35, -0.45, 0.82));
    float diffuse = max(dot(normal, lightDirection), 0.0);
    vec3 halfVector = normalize(lightDirection + viewDirection);
    float specular = pow(max(dot(normal, halfVector), 0.0), 48.0) * 0.10;
    float fresnel = pow(1.0 - max(dot(normal, viewDirection), 0.0), 3.0);

    vec3 litColor = v_Color.rgb * (0.62 + (0.24 * diffuse));
    vec3 reflectedColor = mix(litColor, vec3(0.48, 0.72, 0.88), fresnel * 0.18);
    vec3 finalColor = reflectedColor + vec3(specular);
    float alpha = clamp(v_Color.a * (0.48 + (fresnel * 0.12)), 0.0, 0.60);
    FragColor = vec4(finalColor, alpha);
}";

    Shaders = new(vertexSource, fragmentSource);
  }
}
