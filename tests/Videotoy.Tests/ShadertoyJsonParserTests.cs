using Xunit;

namespace Videotoy.Tests;

public class ShadertoyJsonParserTests
{
    [Fact]
    public void Parse_ChannelSrcFieldIsNotAString_DoesNotThrow()
    {
        // Régression : un champ "src" mal typé (nombre au lieu de chaîne)
        // dans un export Shadertoy JSON partagé/téléchargé levait autrefois
        // InvalidOperationException depuis JsonElement.GetString() et
        // plantait tout le flux "Ouvrir un shader" (voir
        // ShadertoyJsonParser.tryGetStringValue).
        const string json = """
        {
          "Shader": {
            "renderpass": [
              {
                "type": "image",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }",
                "inputs": [
                  { "channel": 0, "type": "texture", "src": 123 }
                ]
              }
            ]
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsOk);
    }

    [Fact]
    public void Parse_ChannelIndexFieldIsNotANumber_DoesNotThrow()
    {
        // Régression : un champ "channel" mal typé (chaîne au lieu de
        // nombre) levait autrefois InvalidOperationException depuis
        // JsonElement.GetInt32() (voir ShadertoyJsonParser.parseChannel).
        const string json = """
        {
          "Shader": {
            "renderpass": [
              {
                "type": "image",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }",
                "inputs": [
                  { "channel": "zero", "type": "texture", "src": "tex.png" }
                ]
              }
            ]
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsOk);
    }

    [Fact]
    public void Parse_WellFormedExport_ResolvesImagePass()
    {
        const string json = """
        {
          "Shader": {
            "info": { "name": "My Shader" },
            "renderpass": [
              {
                "type": "image",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }",
                "inputs": []
              }
            ]
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsOk);
        Assert.Equal("My Shader", result.ResultValue.Item1.Title);
    }

    [Fact]
    public void Parse_MissingImagePass_ReturnsError()
    {
        const string json = """
        {
          "Shader": {
            "renderpass": []
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsError);
    }

    [Fact]
    public void Parse_InvalidJson_ReturnsErrorNotException()
    {
        var result = Videotoy.Core.ShadertoyJsonParser.parse("{ not valid json", "test.json");

        Assert.True(result.IsError);
    }

    [Fact]
    public void Parse_KeyboardChannel_SucceedsWithWarningInsteadOfSilentDrop()
    {
        // Le pipeline de rendu est déterministe : un iChannel "Keyboard"
        // n'a pas de sens (interaction temps réel) mais ne doit pas planter
        // ni être ignoré silencieusement — un avertissement clair doit
        // apparaître dans le panneau Shader Issues (Phase 3 du ROADMAP).
        const string json = """
        {
          "Shader": {
            "renderpass": [
              {
                "type": "image",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }",
                "inputs": [
                  { "channel": 0, "type": "keyboard", "src": "/presets/keyboard.png" }
                ]
              }
            ]
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsOk);
        var (project, warnings) = result.ResultValue;
        Assert.Null(project.ImagePass.Channel0);
        Assert.Contains(warnings, issue => issue.Message.Contains("Keyboard"));
    }

    [Fact]
    public void Parse_BufferLinkedByOpaqueId_ResolvesToBufferPassName()
    {
        // Un export JSON réel obtenu depuis shadertoy.com relie ses passes
        // par un `id` alphanumérique opaque partagé entre `outputs[].id` et
        // `inputs[].id` — le nom lisible ("Buffer A") n'apparaît que dans
        // `renderpass[].name`, jamais dans le lien inputs/outputs lui-même,
        // et le `src` d'un input de type "buffer" n'est qu'un aperçu
        // miniature (`/media/previz/buffer00.png`), pas la source réelle
        // (Phase 3 du ROADMAP, item "résolution des liaisons inter-passes").
        const string json = """
        {
          "Shader": {
            "renderpass": [
              {
                "name": "Buffer A",
                "type": "buffera",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }",
                "inputs": [],
                "outputs": [ { "id": "4dXGR8", "channel": 0 } ]
              },
              {
                "name": "Image",
                "type": "image",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = texture(iChannel0, fragCoord).rgba; }",
                "inputs": [
                  { "id": "4dXGR8", "channel": 0, "type": "buffer", "src": "/media/previz/buffer00.png" }
                ],
                "outputs": [ { "id": "4dXGR9", "channel": 0 } ]
              }
            ]
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsOk);
        var (project, _) = result.ResultValue;
        var channel0 = project.ImagePass.Channel0;
        Assert.NotNull(channel0);
        Assert.Equal("Buffer A", channel0!.Value.BufferName);
    }

    [Fact]
    public void Parse_BufferWithoutId_FallsBackToSrcAsBufferName()
    {
        // Export plus ancien ou construit/édité à la main sans `id` : `src`
        // contient alors déjà directement un nom de buffer exploitable
        // (normalisé par PassGraph.normalizeBufferName), comportement
        // préexistant à préserver.
        const string json = """
        {
          "Shader": {
            "renderpass": [
              {
                "type": "image",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = texture(iChannel0, fragCoord).rgba; }",
                "inputs": [
                  { "channel": 0, "type": "buffer", "src": "Buffer A" }
                ]
              },
              {
                "type": "buffera",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }",
                "inputs": []
              }
            ]
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsOk);
        var (project, _) = result.ResultValue;
        var channel0 = project.ImagePass.Channel0;
        Assert.NotNull(channel0);
        Assert.Equal("Buffer A", channel0!.Value.BufferName);
    }

    [Fact]
    public void Parse_MicChannel_SucceedsWithWarningInsteadOfSilentDrop()
    {
        const string json = """
        {
          "Shader": {
            "renderpass": [
              {
                "type": "image",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }",
                "inputs": [
                  { "channel": 0, "type": "mic", "src": "" }
                ]
              }
            ]
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsOk);
        var (project, warnings) = result.ResultValue;
        Assert.Null(project.ImagePass.Channel0);
        Assert.Contains(warnings, issue => issue.Message.Contains("Mic") || issue.Message.Contains("microphone"));
    }

    [Fact]
    public void Parse_UnrecognizedChannelType_SucceedsWithWarningInsteadOfSilentDrop()
    {
        // Phase 6 du ROADMAP : un type de canal qui n'est ni l'un des types
        // gérés (texture/buffer/video/cubemap/volume/music/musicstream) ni
        // l'un des types non supportés par design connus (keyboard/mic) —
        // un futur type Shadertoy jamais rencontré, ou une valeur malformée
        // dans un export tiers — doit lui aussi produire un avertissement
        // explicite plutôt que de faire disparaître le canal sans aucune
        // trace dans le panneau Shader Issues.
        const string json = """
        {
          "Shader": {
            "renderpass": [
              {
                "type": "image",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }",
                "inputs": [
                  { "channel": 0, "type": "some_future_type", "src": "" }
                ]
              }
            ]
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsOk);
        var (project, warnings) = result.ResultValue;
        Assert.Null(project.ImagePass.Channel0);
        Assert.Contains(warnings, issue => issue.Message.Contains("some_future_type"));
    }

    [Fact]
    public void Parse_RecognizedChannelType_DoesNotProduceUnrecognizedTypeWarning()
    {
        // Garde-fou : un type reconnu et supporté (ex. "texture") ne doit
        // jamais déclencher l'avertissement générique de type non reconnu
        // ajouté pour l'item précédent.
        const string json = """
        {
          "Shader": {
            "renderpass": [
              {
                "type": "image",
                "code": "void mainImage(out vec4 fragColor, in vec2 fragCoord) { fragColor = vec4(1.0); }",
                "inputs": [
                  { "channel": 0, "type": "texture", "src": "tex.png" }
                ]
              }
            ]
          }
        }
        """;

        var result = Videotoy.Core.ShadertoyJsonParser.parse(json, "test.json");

        Assert.True(result.IsOk);
        var (_, warnings) = result.ResultValue;
        Assert.Empty(warnings);
    }
}
