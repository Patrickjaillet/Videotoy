using System;
using Xunit;

namespace Videotoy.Tests;

public class GlslToHlslTranspilerTests
{
    private static Videotoy.Core.ShaderTranspiler.TranspileResult Transpile(string sourceCode)
    {
        var pass = Videotoy.Core.ShaderModel.emptyPass("Image", sourceCode);
        return Videotoy.Core.GlslToHlslTranspiler.transpilePass(null, pass);
    }

    [Fact]
    public void Transpile_CanonicalMainImageSignature_ProducesNoMissingEntryPointError()
    {
        const string source = "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }";

        var result = Transpile(source);

        Assert.DoesNotContain(result.Diagnostics, issue => issue.Message.Contains("Missing 'mainImage'"));
        Assert.Contains("PSMain", result.HlslSource);
    }

    [Fact]
    public void Transpile_MainImageWithoutInQualifier_StillRecognized()
    {
        // Shadertoy documente `in vec2 fragCoord` mais accepte aussi bien la
        // forme sans qualificatif explicite (implicitement `in` en GLSL) —
        // rencontrée occasionnellement dans du code copié/collé (Phase 3 du
        // ROADMAP, item "variantes de syntaxe d'entrée").
        const string source = "void mainImage(out vec4 fragColor, vec2 fragCoord) { fragColor = vec4(1.0); }";

        var result = Transpile(source);

        Assert.DoesNotContain(result.Diagnostics, issue => issue.Message.Contains("Missing 'mainImage'"));
        Assert.Contains("PSMain", result.HlslSource);
    }

    [Fact]
    public void Transpile_MainImageWithExtraWhitespaceAndNewlines_StillRecognized()
    {
        const string source = "void   mainImage (   out   vec4   fragColor ,\n    in vec2 fragCoord   )\n{\n    fragColor = vec4(1.0);\n}";

        var result = Transpile(source);

        Assert.DoesNotContain(result.Diagnostics, issue => issue.Message.Contains("Missing 'mainImage'"));
        Assert.Contains("PSMain", result.HlslSource);
    }

    [Fact]
    public void Transpile_DifferentParameterNames_AreRespectedInGeneratedCode()
    {
        const string source = "void mainImage(out vec4 outColor, in vec2 coord) { outColor = vec4(coord, 0.0, 1.0); }";

        var result = Transpile(source);

        Assert.DoesNotContain(result.Diagnostics, issue => issue.Message.Contains("Missing 'mainImage'"));
        Assert.Contains("outColor", result.HlslSource);
        Assert.Contains("coord", result.HlslSource);
    }

    [Fact]
    public void Transpile_NoMainImageEntryPoint_ProducesClearError()
    {
        // Un shader historique qui n'écrit que `gl_FragColor` sans jamais
        // déclarer `mainImage` n'est pas supporté (pas de point d'entrée
        // Shadertoy standard) — doit produire un diagnostic clair plutôt
        // qu'un échec de compilation HLSL opaque en aval.
        const string source = "void main() { gl_FragColor = vec4(1.0); }";

        var result = Transpile(source);

        Assert.Contains(result.Diagnostics, issue => issue.Message.Contains("Missing 'mainImage'"));
    }

    [Fact]
    public void Transpile_TexelFetch_ProducesDeclaredHelperFunctionNotUndefinedIdentifier()
    {
        // Régression : texelFetch/textureLod étaient réécrits vers
        // __texelFetch/__textureLod (Phase 4 du ROADMAP) sans jamais être
        // déclarés nulle part dans le HLSL généré — tout shader les utilisant
        // échouait à la compilation FXC avec un identifiant non déclaré.
        const string source = """
            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                fragColor = texelFetch(iChannel0, ivec2(fragCoord), 0);
            }
            """;

        var result = Transpile(source);

        Assert.DoesNotContain(result.Diagnostics, issue => issue.Message.Contains("Missing 'mainImage'"));
        Assert.Contains("__texelFetch0(", result.HlslSource);
        Assert.Contains("float4 __texelFetch0(int2 p, int lod)", result.HlslSource);
        Assert.Contains("iChannel0.Load(int3(p, lod))", result.HlslSource);
    }

    [Fact]
    public void Transpile_TextureLod_ProducesDeclaredHelperFunction()
    {
        const string source = """
            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                vec2 uv = fragCoord / iResolution.xy;
                fragColor = textureLod(iChannel1, uv, 2.0);
            }
            """;

        var result = Transpile(source);

        Assert.Contains("__textureLod1(", result.HlslSource);
        Assert.Contains("float4 __textureLod1(float2 uv, float lod)", result.HlslSource);
        Assert.Contains("iChannel1.SampleLevel(iChannel1Sampler, uv, lod)", result.HlslSource);
    }

    [Fact]
    public void Transpile_SingleArgumentVectorConversion_IsNotDuplicated()
    {
        // Régression : ivec2(fragCoord) (conversion GLSL d'un vec2 déjà de
        // la bonne taille vers ivec2, syntaxe identique et valide en HLSL)
        // était incorrectement traité comme une diffusion scalaire
        // (vec3(0.0) style) et dupliqué en int2(fragCoord, fragCoord) — 4
        // floats pour un constructeur à 2 composantes, provoquant
        // "error X3014: incorrect number of arguments to numeric-type
        // constructor" à la compilation FXC réelle (confirmé manuellement).
        const string source = """
            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                ivec2 p = ivec2(fragCoord);
                fragColor = vec4(float(p.x), float(p.y), 0.0, 1.0);
            }
            """;

        var result = Transpile(source);

        Assert.Contains("int2 p = int2(fragCoord);", result.HlslSource);
        Assert.DoesNotContain("int2(fragCoord, fragCoord)", result.HlslSource);
    }

    [Fact]
    public void Transpile_ScalarBroadcastConstructor_IsStillDuplicated()
    {
        // Le cas dominant que expandScalarVectorConstructors doit continuer
        // à couvrir : un vrai scalaire (littéral ou expression arithmétique,
        // jamais un identifiant nu) diffusé sur toutes les composantes.
        const string source = """
            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                vec3 c = vec3(0.5 + 0.5 * sin(iTime));
                fragColor = vec4(c, 1.0);
            }
            """;

        var result = Transpile(source);

        Assert.Contains("float3(0.5 + 0.5 * sin(iTime), 0.5 + 0.5 * sin(iTime), 0.5 + 0.5 * sin(iTime))", result.HlslSource);
    }

    [Fact]
    public void Transpile_TexelFetchOnDifferentChannels_UsesDistinctHelperFunctions()
    {
        // Chaque canal a son propre wrapper (__texelFetchN) puisque HLSL n'a
        // pas de fonction libre générique équivalente prenant la texture en
        // argument comme le fait GLSL — vérifie que deux canaux différents
        // dans le même shader ne collisionnent pas sur un seul wrapper.
        const string source = """
            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                vec4 a = texelFetch(iChannel0, ivec2(fragCoord), 0);
                vec4 b = texelFetch(iChannel2, ivec2(fragCoord), 0);
                fragColor = a + b;
            }
            """;

        var result = Transpile(source);

        Assert.Contains("__texelFetch0(", result.HlslSource);
        Assert.Contains("__texelFetch2(", result.HlslSource);
    }

    [Fact]
    public void Transpile_PreprocessorDirectives_PassThroughUnchangedForFxc()
    {
        // `#define`/`#ifdef`/`#endif` (Phase 4 du ROADMAP) ne sont pas
        // traités par ce transpileur : ils sont volontairement laissés tels
        // quels dans le HLSL généré, puisque FXC (le compilateur HLSL réel,
        // invoqué en aval par MultiPassRenderer) a son propre préprocesseur
        // C compatible qui les gère nativement — confirmé manuellement par
        // compilation FXC réelle d'un shader utilisant une macro paramétrée
        // et un bloc #ifdef. Seul le texte est vérifié ici (pas de
        // compilation FXC dans ce projet de tests) pour garder une
        // régression rapide si ce passage devait un jour être altéré par
        // erreur (ex. une passe de nettoyage de commentaires trop agressive).
        const string source = """
            #define BRIGHTNESS 0.8
            #define TINT(c) (c * BRIGHTNESS)

            #ifdef BRIGHTNESS
            #define HAS_BRIGHTNESS 1
            #endif

            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                vec3 color = vec3(0.5);
                fragColor = vec4(TINT(color), 1.0);
            }
            """;

        var result = Transpile(source);

        Assert.Contains("#define BRIGHTNESS 0.8", result.HlslSource);
        Assert.Contains("#define TINT(c) (c * BRIGHTNESS)", result.HlslSource);
        Assert.Contains("#ifdef BRIGHTNESS", result.HlslSource);
        Assert.Contains("#endif", result.HlslSource);
    }

    [Fact]
    public void Transpile_MatrixTimesVector_UsesMulIntrinsicNotComponentwiseOperator()
    {
        // Régression : `mat2 * vec2` (multiplication matricielle GLSL, très
        // courante pour une rotation 2D) devenait `float2x2 * float2` en
        // HLSL après la seule conversion de types — HLSL réserve `*` à une
        // multiplication composante-par-composante et lève "error X3020:
        // type mismatch" pour ce cas (confirmé manuellement par compilation
        // FXC réelle) ; la vraie multiplication matricielle nécessite
        // l'intrinsèque mul(a, b).
        const string source = """
            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                vec2 uv = fragCoord / iResolution.xy;
                mat2 rot = mat2(cos(iTime), -sin(iTime), sin(iTime), cos(iTime));
                vec2 rotated = rot * uv;
                fragColor = vec4(rotated, 0.0, 1.0);
            }
            """;

        var result = Transpile(source);

        Assert.Contains("mul(rot, uv)", result.HlslSource);
        Assert.DoesNotContain("rot * uv", result.HlslSource);
    }

    [Fact]
    public void Transpile_NestedScalarBroadcastInsideMultiArgumentConstructor_IsStillExpanded()
    {
        // Régression : dans `float4(float3(glow) + x, 1.0)`, le scan à
        // parenthèses équilibrées repérait `float4(...)` en premier, voyait
        // plusieurs arguments top-level (donc pas de diffusion pour lui-
        // même) et recopiait tout son contenu tel quel — y compris l'appel
        // imbriqué `float3(glow)`, qui n'était alors jamais réexaminé
        // séparément et restait à 1 seul argument, provoquant "error X3014:
        // incorrect number of arguments" à la compilation FXC réelle
        // (confirmé manuellement). expandScalarVectorConstructorsWithScalars
        // doit retraiter récursivement le contenu d'un appel qui n'est
        // lui-même pas développé.
        const string source = """
            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                float glow = 0.5;
                fragColor = vec4(vec3(glow) + fragCoord.x * 0.1, 1.0);
            }
            """;

        var result = Transpile(source);

        Assert.Contains("float3(glow, glow, glow)", result.HlslSource);
    }

    [Fact]
    public void Transpile_CommonCode_IsPrefixedBeforePassSource()
    {
        // La passe Common (Phase 4 du ROADMAP, item "#include inter-passes")
        // doit être injectée avant le code de la passe elle-même, comme le
        // fait Shadertoy nativement — confirme le comportement de
        // transpilePass plutôt que de le supposer.
        const string commonCode = "float sharedHelper() { return 0.42; }";
        const string passSource = """
            void mainImage(out vec4 fragColor, in vec2 fragCoord)
            {
                fragColor = vec4(sharedHelper(), 0.0, 0.0, 1.0);
            }
            """;

        var pass = Videotoy.Core.ShaderModel.emptyPass("Image", passSource);
        var result = Videotoy.Core.GlslToHlslTranspiler.transpilePass(commonCode, pass);

        Assert.Contains("sharedHelper", result.HlslSource);
        var commonIndex = result.HlslSource.IndexOf("sharedHelper()", StringComparison.Ordinal);
        var callIndex = result.HlslSource.IndexOf("sharedHelper(), 0.0", StringComparison.Ordinal);
        Assert.True(commonIndex >= 0 && callIndex > commonIndex, "Common code must appear before its usage in the pass.");
    }
}
