using System.Text.Json;
using Xunit;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Tests;

public class OcrResultTests
{
    [Fact]
    public void OcrResult_DefaultEngine_IsEmptyString()
    {
        var result = new OcrResult();

        Assert.Equal("", result.Engine);
    }

    [Fact]
    public void OcrRect_RightEqualsXPlusWidth_BottomEqualsYPlusHeight()
    {
        var rect = new OcrRect { X = 10, Y = 20, Width = 100, Height = 50 };

        Assert.Equal(110, rect.Right);
        Assert.Equal(70, rect.Bottom);
    }

    [Fact]
    public void OcrResult_WithErrorSet_ErrorIsNotNull()
    {
        var result = new OcrResult { Error = "OCR engine failed" };

        Assert.NotNull(result.Error);
        Assert.Equal("OCR engine failed", result.Error);
    }

    [Fact]
    public void OcrWord_WithBoundingBox_BoundingBoxNotNull()
    {
        var word = new OcrWord
        {
            Text = "hello",
            BoundingBox = new OcrRect { X = 0, Y = 0, Width = 50, Height = 20 },
            Confidence = 0.95
        };

        Assert.NotNull(word.BoundingBox);
        Assert.Equal("hello", word.Text);
        Assert.Equal(0.95, word.Confidence);
    }

    [Fact]
    public void OcrResult_JsonRoundTrip_PreservesValues()
    {
        var original = new OcrResult
        {
            Text = "test text",
            Language = "en",
            Confidence = 0.88,
            Engine = "",
            Words = new List<OcrWord>
            {
                new() { Text = "test", BoundingBox = new OcrRect { X = 1, Y = 2, Width = 30, Height = 10 }, Confidence = 0.9 },
                new() { Text = "text", BoundingBox = new OcrRect { X = 35, Y = 2, Width = 25, Height = 10 }, Confidence = 0.85 }
            }
        };

        var json = JsonSerializer.Serialize(original);
        var deserialized = JsonSerializer.Deserialize<OcrResult>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(original.Text, deserialized.Text);
        Assert.Equal(original.Language, deserialized.Language);
        Assert.Equal(original.Confidence, deserialized.Confidence);
        Assert.Equal(original.Engine, deserialized.Engine);
        Assert.Equal(2, deserialized.Words.Count);
        Assert.Equal("test", deserialized.Words[0].Text);
        Assert.Equal(0.9, deserialized.Words[0].Confidence);
    }
}
