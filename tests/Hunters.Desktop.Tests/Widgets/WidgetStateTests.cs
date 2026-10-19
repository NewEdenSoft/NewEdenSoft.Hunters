using NewEdenSoft.Hunters.Desktop.Settings;
using NewEdenSoft.Hunters.Desktop.Widgets;

namespace NewEdenSoft.Hunters.Desktop.Tests.Widgets;

public sealed class WidgetStateTests
{
    private static readonly WidgetDefinition Definition =
        new("fleet", "Fleet", "Fleet members", typeof(TestWidget), DefaultWidth: 320, DefaultHeight: 220, MinWidth: 200, MinHeight: 120);

    [Fact]
    public void New_widget_uses_defaults()
    {
        var state = new WidgetState(Definition, new TestWidget(), new WidgetSettings());

        Assert.False(state.IsFloating);
        Assert.Null(state.X);
        Assert.Equal(320, state.Width);
        Assert.Equal(220, state.Height);
        Assert.True(state.CanMove);
    }

    [Fact]
    public void Saved_layout_round_trips()
    {
        var saved = new WidgetSettings
        {
            IsFloating = true, X = 100, Y = -50, Width = 400, Height = 300,
            Opacity = 0.55, ClickThrough = true, Locked = true,
        };

        var state = new WidgetState(Definition, new TestWidget(), saved);

        Assert.Equal(saved, state.ToSettings());
    }

    [Fact]
    public void Size_and_opacity_are_clamped()
    {
        var state = new WidgetState(Definition, new TestWidget(), new WidgetSettings { Width = 50, Height = 10, Opacity = 0.01 });

        Assert.Equal(200, state.Width);
        Assert.Equal(120, state.Height);
        Assert.Equal(WidgetState.MinOpacity, state.Opacity);
    }

    [Theory]
    [InlineData(false, false, true, "MOVABLE")]
    [InlineData(true, false, false, "LOCKED")]
    [InlineData(false, true, false, "CLICK-THROUGH")]
    [InlineData(true, true, false, "CLICK-THROUGH")]
    public void Lock_and_click_through_control_moving(bool locked, bool clickThrough, bool canMove, string mode)
    {
        var state = new WidgetState(Definition, new TestWidget(), new WidgetSettings { Locked = locked, ClickThrough = clickThrough });

        Assert.Equal(canMove, state.CanMove);
        Assert.Equal(mode, state.ModeLabel);
    }

    [Fact]
    public void Changing_lock_notifies_can_move()
    {
        var state = new WidgetState(Definition, new TestWidget(), new WidgetSettings());
        var changed = new List<string?>();
        state.PropertyChanged += (_, e) => changed.Add(e.PropertyName);

        state.Locked = true;

        Assert.Contains(nameof(WidgetState.CanMove), changed);
        Assert.Contains(nameof(WidgetState.ModeLabel), changed);
    }

    private sealed class TestWidget : WidgetViewModelBase;
}
