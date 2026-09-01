namespace TKWF.Ext.BlobStoring.Tests;

/// <summary>
/// BlobInfo 测试——覆盖构造/默认值。
/// </summary>
public class BlobInfoTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        // Act
        var info = new BlobInfo();

        // Assert
        Assert.Equal("", info.Name);
        Assert.Equal("", info.Path);
        Assert.Null(info.ContentType);
        Assert.Equal(0, info.Size);
    }

    [Fact]
    public void Constructor_WithValues_PropertiesSet()
    {
        // Act
        var info = new BlobInfo
        {
            Name = "photo.png",
            Path = "abc/photo.png",
            ContentType = "image/png",
            Size = 1024
        };

        // Assert
        Assert.Equal("photo.png", info.Name);
        Assert.Equal("abc/photo.png", info.Path);
        Assert.Equal("image/png", info.ContentType);
        Assert.Equal(1024, info.Size);
    }

    [Fact]
    public void Constructor_NullContentType_IsAllowed()
    {
        // Act
        var info = new BlobInfo
        {
            Name = "unknown",
            ContentType = null
        };

        // Assert
        Assert.Null(info.ContentType);
    }
}
