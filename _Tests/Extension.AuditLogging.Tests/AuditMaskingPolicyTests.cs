using System.Collections.Generic;
using System.Text.Json.Nodes;
using Xunit;

namespace TKWF.Ext.AuditLogging.Tests;

public class AuditMaskingPolicyTests
{
    private static DefaultAuditMaskingPolicy CreatePolicy(IEnumerable<string>? additional = null)
        => new(additional);

    [Fact]
    public void Mask_SensitiveFields_ReplacedWithMask()
    {
        var policy = CreatePolicy();

        var masked = policy.Mask(new Dictionary<string, object?>
        {
            ["password"] = "p@ss",
            ["phone"] = "13800000000",
            ["name"] = "alice",
        });

        var json = ((JsonNode)masked!).ToJsonString();
        Assert.Contains("***", json);
        Assert.DoesNotContain("p@ss", json);
        Assert.DoesNotContain("13800000000", json);
        Assert.Contains("alice", json);
    }

    [Fact]
    public void Mask_AdditionalSensitiveField_ReplacedWithMask()
    {
        var policy = CreatePolicy(new[] { "customfield" });

        var masked = policy.Mask(new Dictionary<string, object?> { ["customfield"] = "secret" });

        var json = ((JsonNode)masked!).ToJsonString();
        Assert.Contains("***", json);
        Assert.DoesNotContain("secret", json);
    }

    [Fact]
    public void Mask_Null_ReturnsNull()
    {
        Assert.Null(CreatePolicy().Mask(null));
    }

    [Fact]
    public void MaskText_JsonString_MasksSensitiveKeys()
    {
        var masked = CreatePolicy().MaskText("{\"password\":\"p@ss\",\"name\":\"alice\"}");

        Assert.NotNull(masked);
        Assert.Contains("***", masked);
        Assert.DoesNotContain("p@ss", masked);
        Assert.Contains("alice", masked);
    }

    [Fact]
    public void MaskText_PlainText_ReturnsUnchanged()
    {
        Assert.Equal("hello world", CreatePolicy().MaskText("hello world"));
    }
}
