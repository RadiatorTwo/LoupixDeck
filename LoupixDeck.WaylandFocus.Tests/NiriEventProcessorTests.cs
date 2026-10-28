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
        var processor = new NiriEventProcessor();

        Assert.True(processor.Process(InitialState, out var focus));
        Assert.Equal(new FocusInfo("app.one", "One", 111), focus);
    }

    [Fact]
    public void FocusChanged_MovesToTheNewWindow()
    {
        var processor = new NiriEventProcessor();
        processor.Process(InitialState, out _);

        Assert.True(processor.Process("""{"Ok":{"WindowFocusChanged":{"id":2}}}""", out var focus));
        Assert.Equal(new FocusInfo("app.two", "Two", 222), focus);
    }

    [Fact]
    public void FocusChangedToNull_ReportsNoFocusedToplevel()
    {
        var processor = new NiriEventProcessor();
        processor.Process(InitialState, out _);

        Assert.True(processor.Process("""{"Ok":{"WindowFocusChanged":{"id":null}}}""", out var focus));
        Assert.Null(focus);
    }

    [Fact]
    public void DuplicateFocusEmission_IsSuppressed()
    {
        var processor = new NiriEventProcessor();
        processor.Process(InitialState, out _);
        Assert.True(processor.Process("""{"Ok":{"WindowFocusChanged":{"id":2}}}""", out _));

        Assert.False(processor.Process("""{"Ok":{"WindowFocusChanged":{"id":2}}}""", out _));
    }

    [Fact]
    public void TitleChangeOnFocusedWindow_IsEmitted()
    {
        var processor = new NiriEventProcessor();
        processor.Process(InitialState, out _);

        const string changed = """
            {"Ok":{"WindowOpenedOrChanged":{"window":
              {"id":1,"title":"One (renamed)","app_id":"app.one","pid":111,"is_focused":true}
            }}}
            """;
        Assert.True(processor.Process(changed, out var focus));
        Assert.Equal("One (renamed)", focus!.Title);
    }

    [Fact]
    public void ClosingTheFocusedWindow_ReportsNoFocus()
    {
        var processor = new NiriEventProcessor();
        processor.Process(InitialState, out _);

        Assert.True(processor.Process("""{"Ok":{"WindowClosed":{"id":1}}}""", out var focus));
        Assert.Null(focus);
    }

    [Fact]
    public void ErrResponses_AreIgnored()
    {
        var processor = new NiriEventProcessor();

        Assert.False(processor.Process("""{"Err":"something went wrong"}""", out _));
    }
}
