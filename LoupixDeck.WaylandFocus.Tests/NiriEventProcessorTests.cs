using LoupixDeck.WaylandFocus.Sources;

namespace LoupixDeck.WaylandFocus.Tests;

public class NiriEventProcessorTests
{
    private const string InitialState = """
        {"Ok":{"WindowsChanged":{"windows":[
          {"id":1,"title":"One","app_id":"app.one","pid":111,"is_focused":true},
          {"id":2,"title":"Two","app_id":"app.two","pid":222,"is_focused":false}
        ]}}}
        """;

    [Fact]
    public void InitialState_ReportsTheFocusedWindowWithPid()
    {
        NiriEventProcessor processor = new();

        Assert.True(processor.Process(InitialState, out FocusInfo? focus));
        Assert.Equal(new FocusInfo("app.one", "One", 111), focus);
    }

    [Fact]
    public void FocusChanged_MovesToTheNewWindow()
    {
        NiriEventProcessor processor = new();
        processor.Process(InitialState, out _);

        Assert.True(processor.Process("""{"Ok":{"WindowFocusChanged":{"id":2}}}""", out FocusInfo? focus));
        Assert.Equal(new FocusInfo("app.two", "Two", 222), focus);
    }

    [Fact]
    public void FocusChangedToNull_ReportsNoFocusedToplevel()
    {
        NiriEventProcessor processor = new();
        processor.Process(InitialState, out _);

        Assert.True(processor.Process("""{"Ok":{"WindowFocusChanged":{"id":null}}}""", out FocusInfo? focus));
        Assert.Null(focus);
    }

    [Fact]
    public void DuplicateFocusEmission_IsSuppressed()
    {
        NiriEventProcessor processor = new();
        processor.Process(InitialState, out _);
        Assert.True(processor.Process("""{"Ok":{"WindowFocusChanged":{"id":2}}}""", out _));

        Assert.False(processor.Process("""{"Ok":{"WindowFocusChanged":{"id":2}}}""", out _));
    }

    [Fact]
    public void TitleChangeOnFocusedWindow_IsEmitted()
    {
        NiriEventProcessor processor = new();
        processor.Process(InitialState, out _);

        const string changed = """
            {"Ok":{"WindowOpenedOrChanged":{"window":
              {"id":1,"title":"One (renamed)","app_id":"app.one","pid":111,"is_focused":true}
            }}}
            """;
        Assert.True(processor.Process(changed, out FocusInfo? focus));
        Assert.Equal("One (renamed)", focus!.Title);
    }

    [Fact]
    public void ClosingTheFocusedWindow_ReportsNoFocus()
    {
        NiriEventProcessor processor = new();
        processor.Process(InitialState, out _);

        Assert.True(processor.Process("""{"Ok":{"WindowClosed":{"id":1}}}""", out FocusInfo? focus));
        Assert.Null(focus);
    }

    [Fact]
    public void ErrResponses_AreIgnored()
    {
        NiriEventProcessor processor = new();

        Assert.False(processor.Process("""{"Err":"something went wrong"}""", out _));
    }
}