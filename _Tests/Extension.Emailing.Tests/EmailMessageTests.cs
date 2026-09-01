namespace TKWF.Ext.Emailing.Tests;

/// <summary>
/// EmailMessage 测试——覆盖构造/默认值。
/// </summary>
public class EmailMessageTests
{
    [Fact]
    public void Constructor_DefaultValues_AreCorrect()
    {
        // Act
        var message = new EmailMessage();

        // Assert
        Assert.Equal("", message.To);
        Assert.Null(message.From);
        Assert.Equal("", message.Subject);
        Assert.Null(message.Body);
        Assert.False(message.IsHtml);
    }

    [Fact]
    public void Constructor_WithValues_PropertiesSet()
    {
        // Act
        var message = new EmailMessage
        {
            To = "test@example.com",
            From = "sender@example.com",
            Subject = "Hello",
            Body = "<p>World</p>",
            IsHtml = true
        };

        // Assert
        Assert.Equal("test@example.com", message.To);
        Assert.Equal("sender@example.com", message.From);
        Assert.Equal("Hello", message.Subject);
        Assert.Equal("<p>World</p>", message.Body);
        Assert.True(message.IsHtml);
    }

    [Fact]
    public void Constructor_MultipleRecipients_CommaSeparated()
    {
        // Act
        var message = new EmailMessage
        {
            To = "a@example.com,b@example.com,c@example.com"
        };

        // Assert
        Assert.Equal("a@example.com,b@example.com,c@example.com", message.To);
    }
}
