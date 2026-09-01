namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobStoringOptions 测试——覆盖默认值验证。
/// </summary>
public class BlobStoringOptionsTests
{
    [Fact]
    public void Default_RootPath_IsBlobs()
    {
        var options = new BlobStoringOptions();
        Assert.Equal("./blobs", options.RootPath);
    }

    [Fact]
    public void Default_IsEnabled_IsTrue()
    {
        var options = new BlobStoringOptions();
        Assert.True(options.IsEnabled);
    }

    [Fact]
    public void Custom_RootPath_IsRespected()
    {
        var options = new BlobStoringOptions { RootPath = "/data/blobs" };
        Assert.Equal("/data/blobs", options.RootPath);
    }
}
