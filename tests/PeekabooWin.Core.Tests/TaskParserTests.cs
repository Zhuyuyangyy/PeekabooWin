using System.Net.Http;
using Xunit;
using PeekabooWin.Core.Agent;
using PeekabooWin.Core.Models;

namespace PeekabooWin.Core.Tests;

public class TaskParserTests
{
    private readonly TaskParser _parser = new(new HttpClient());

    [Fact]
    public async Task ParseTask_ClickWithCoordinates_ReturnsClickAction()
    {
        var steps = await _parser.ParseTaskAsync("click 100 200");

        Assert.Single(steps);
        Assert.Equal("click", steps[0].Action);
        Assert.Equal("100", steps[0].Args["x"]);
        Assert.Equal("200", steps[0].Args["y"]);
    }

    [Fact]
    public async Task ParseTask_TypeText_ReturnsTypeAction()
    {
        var steps = await _parser.ParseTaskAsync("type hello");

        Assert.Single(steps);
        Assert.Equal("type", steps[0].Action);
        Assert.Equal("hello", steps[0].Args["text"]);
    }

    [Fact]
    public async Task ParseTask_PressKey_ReturnsPressAction()
    {
        var steps = await _parser.ParseTaskAsync("press enter");

        Assert.Single(steps);
        Assert.Equal("press", steps[0].Action);
        Assert.Equal("enter", steps[0].Args["key"]);
    }

    [Fact]
    public async Task ParseTask_PressHotkey_ReturnsHotkeyAction()
    {
        var steps = await _parser.ParseTaskAsync("press ctrl+a");

        Assert.Single(steps);
        Assert.Equal("hotkey", steps[0].Action);
        Assert.Equal("ctrl+a", steps[0].Args["keys"]);
    }

    [Fact]
    public async Task ParseTask_OpenApp_ReturnsFocusWindowAction()
    {
        var steps = await _parser.ParseTaskAsync("open notepad");

        Assert.Single(steps);
        Assert.Equal("focus-window", steps[0].Action);
        Assert.Equal("notepad", steps[0].Args["title"]);
    }

    [Fact]
    public async Task ParseTask_Screenshot_ReturnsScreenshotAction()
    {
        var steps = await _parser.ParseTaskAsync("screenshot");

        Assert.Single(steps);
        Assert.Equal("screenshot", steps[0].Action);
    }

    [Fact]
    public async Task ParseTask_UnknownTask_NoApiKey_ReturnsErrorAction()
    {
        var originalKey = Environment.GetEnvironmentVariable("MINIMAX_API_KEY");
        Environment.SetEnvironmentVariable("MINIMAX_API_KEY", null);

        try
        {
            var steps = await _parser.ParseTaskAsync("do something completely unknown xyz");

            Assert.Single(steps);
            Assert.Equal("error", steps[0].Action);
            Assert.NotEmpty(_parser.LastFallbackReason);
            Assert.Equal("MISSING_API_KEY", _parser.LastLlmErrorCode);
        }
        finally
        {
            Environment.SetEnvironmentVariable("MINIMAX_API_KEY", originalKey);
        }
    }

    [Fact]
    public async Task GetLastParseMetadata_AfterRuleBasedParse_ReturnsCorrectMode()
    {
        await _parser.ParseTaskAsync("click 100 200");

        var meta = _parser.GetLastParseMetadata();

        Assert.Equal("rule_based", meta.ParserMode);
        Assert.True(meta.LlmEnabled);
        Assert.Empty(meta.LlmErrorCode);
    }
}