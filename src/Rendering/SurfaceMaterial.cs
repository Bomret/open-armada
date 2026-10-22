using System.Collections.Generic;
using Godot;
using OpenArmada.Assets;

namespace OpenArmada.Rendering;

/// <summary>
/// The Godot material of a model surface: one shader that follows the game's rules. A constant material
/// is unshaded; Lambert and Phong add the scene's lights to the material's ambient, with Phong's specular
/// term from the specular color and power. The texture multiplies the lit color (texture stage 0 modulates
/// the lit vertex color in the game). Blend modes: opaque, alpha test at 128/255, alpha (with the alpha
/// test when the state asks), add.
///
/// Known difference: the game adds the scene ambient and the material's ambient, clamps, and then modulates
/// the texture. Godot adds the scene ambient scaled by the albedo (texture * diffuse), and the shader cannot
/// clamp before the texture without leaving Godot's lighting; the material ambient is added as emission.
/// </summary>
public static class SurfaceMaterial
{
    static readonly Dictionary<string, Shader> Shaders = new();

    /// <summary>
    /// A new material with the surface's lighting model and colors and the blend of <c>blend</c>, textured when
    /// <c>texture</c> is not null; <c>cull</c> true culls back faces, false draws both sides. Materials with the
    /// same lighting model, blend mode, filtering, alpha test, and culling share one shader, built on first use
    /// and kept for the life of the process.
    /// </summary>
    public static ShaderMaterial Create(Surface surface, BlendState blend, Texture2D? texture, bool cull)
    {
        var mat = new ShaderMaterial { Shader = ShaderFor(surface.Model, blend, cull) };
        if (texture != null)
        {
            mat.SetShaderParameter("albedo_tex", texture);
        }

        mat.SetShaderParameter("has_tex", texture != null);
        mat.SetShaderParameter("opacity", blend.Opacity);
        mat.SetShaderParameter("uv_offset", Vector2.Zero);
        Apply(mat, surface);

        return mat;
    }

    /// <summary>Sets the colors and power; a team material calls it again when the team changes.</summary>
    public static void Apply(ShaderMaterial mat, Surface s)
    {
        mat.SetShaderParameter("ambient", ToGodot(s.Ambient));
        mat.SetShaderParameter("diffuse", ToGodot(s.Diffuse));
        mat.SetShaderParameter("specular", ToGodot(s.Specular));
        mat.SetShaderParameter("power", s.Power);
    }

    /// <summary>
    /// Draws the material in one color: ambient and diffuse become <c>color</c>, the specular goes out, and the
    /// texture still modulates. A model drawn in one color, such as a shield effect, calls it on each surface's own
    /// copy of the material.
    /// </summary>
    public static void Colorize(ShaderMaterial mat, Color color)
    {
        var rgb = new Vector3(color.R, color.G, color.B);
        mat.SetShaderParameter("ambient", rgb);
        mat.SetShaderParameter("diffuse", rgb);
        mat.SetShaderParameter("specular", Vector3.Zero);
    }

    static Vector3 ToGodot(System.Numerics.Vector3 v) => new(v.X, v.Y, v.Z);

    static Shader ShaderFor(LightingModel model, BlendState blend, bool cull)
    {
        var key = $"{model}/{blend.Mode}/{blend.Nearest}/{blend.AlphaTest}/{cull}";
        if (Shaders.TryGetValue(key, out var shader))
            return shader;
        var render = new List<string> { cull ? "cull_back" : "cull_disabled" };
        if (model == LightingModel.Constant)
        {
            render.Add("unshaded");
        }

        if (blend.Mode == BlendMode.Add)
        {
            render.Add("blend_add");
        }

        var alpha = blend.Mode switch
        {
            BlendMode.Cutout => "if (a < 128.0 / 255.0) discard;",
            BlendMode.Alpha => (blend.AlphaTest ? "if (a < 128.0 / 255.0) discard; " : "") + "ALPHA = a;",
            BlendMode.Add => "ALPHA = 1.0;",
            _ => "",
        };
        var emission = model == LightingModel.Constant ? "" : "EMISSION = t.rgb * ambient;";
        var specular =
            model == LightingModel.Phong
                ? "SPECULAR_LIGHT += specular * LIGHT_COLOR * ATTENUATION / PI * pow(max(dot(reflect(-LIGHT, NORMAL), VIEW), 0.0), max(power, 1.0)) * step(0.0, dot(NORMAL, LIGHT));"
                : "";
        shader = new Shader
        {
            Code =
                $@"shader_type spatial;
render_mode {string.Join(", ", render)};
uniform sampler2D albedo_tex : source_color, {(blend.Nearest ? "filter_nearest_mipmap" : "filter_linear_mipmap")}, repeat_enable;
uniform bool has_tex;
uniform vec3 ambient;
uniform vec3 diffuse;
uniform vec3 specular;
uniform float power;
uniform float opacity;
uniform vec2 uv_offset;
void fragment() {{
    vec4 t = has_tex ? texture(albedo_tex, UV + uv_offset) : vec4(1.0);
    float a = t.a * opacity;
    ALBEDO = t.rgb * diffuse;
    {emission}
    {alpha}
}}
void light() {{
    DIFFUSE_LIGHT += LIGHT_COLOR * ATTENUATION / PI * max(dot(NORMAL, LIGHT), 0.0);
    {specular}
}}
",
        };

        return Shaders[key] = shader;
    }
}
