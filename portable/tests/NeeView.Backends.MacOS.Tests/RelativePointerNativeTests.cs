using NeeView.Backends;

namespace NeeView.Backends.MacOS.Tests;

/// <summary>Lifecycle checks use invalid handles only; they never capture or move the real pointer.</summary>
public sealed class RelativePointerNativeTests
{
    [Fact]
    public void InvalidWindowReturnsNoLeaseAndDoesNotInvokeHandler()
    {
        var called = false;
        using var input = new MacTrackpadInput();
        var lease = input.BeginRelativePointer(new nint(-1), (_, _) => called = true);
        Assert.Null(lease);
        Assert.False(called);
    }

    [Fact]
    public void ZeroWindowReturnsNoLease()
    {
        using var input = new MacTrackpadInput();
        Assert.Null(input.BeginRelativePointer(0, (_, _) => { }));
    }
}
