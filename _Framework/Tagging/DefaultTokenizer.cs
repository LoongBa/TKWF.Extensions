using System;

namespace TKWF.Ext.Tagging;

/// <summary>
/// 默认分词器：简单的按空格和常见标点切分
/// </summary>
public class DefaultTokenizer : ITokenizer
{
    public void Tokenize(string text, Action<TokenText> receiver)
    {
        if (string.IsNullOrWhiteSpace(text)) return;

        var span = text.AsSpan();
        var wordStart = -1;

        for (var i = 0; i < span.Length; i++)
        {
            // 简单判断是否是分隔符（可根据需要添加中文标点）
            if (char.IsWhiteSpace(span[i]) || char.IsPunctuation(span[i]))
            {
                if (wordStart != -1)
                {
                    receiver(new TokenText(wordStart, i - wordStart));
                    wordStart = -1;
                }
            }
            else if (wordStart == -1)
            {
                wordStart = i;
            }
        }

        // 处理结尾的词
        if (wordStart != -1)
        {
            receiver(new TokenText(wordStart, span.Length - wordStart));
        }
    }
}
