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
        Assert.Equal("My Shader", result.ResultValue.Title);
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
}
