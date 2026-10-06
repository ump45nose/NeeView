using NeeView;

namespace NeeView.Engine.Tests;

public sealed class AnimatedPlaybackTests
{
    private static AnimatedMediaPlayer Player(params int[] delays) => new(new(new(10, 10), AnimatedImageType.Gif, delays.Select(value => TimeSpan.FromMilliseconds(value)).ToArray(), 0));

    [Fact] public void DefaultsAndSeek() { using var p = Player(100, 200, 300); Assert.True(p.IsRepeat); Assert.False(p.IsPlaying); p.Position = .6; Assert.Equal(1, p.CurrentFrameIndex); p.AddPosition(TimeSpan.FromMilliseconds(-200)); Assert.Equal(0, p.CurrentFrameIndex); }
    [Fact] public void VariableDelayPauseDisableAndEnd() { using var p = Player(100, 300); p.Play(); p.Advance(TimeSpan.FromMilliseconds(99)); Assert.Equal(0,p.CurrentFrameIndex); p.Advance(TimeSpan.FromMilliseconds(1)); Assert.Equal(1,p.CurrentFrameIndex); p.Pause(); p.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(1,p.CurrentFrameIndex); p.Play(); p.IsEnabled=false; p.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(1,p.CurrentFrameIndex); }
    [Fact] public void RepeatAndLongElapsedAreBounded() { using var p = Player(10, 10); var eos=0; p.MediaEndOfStreamReached += (_,_)=>eos++; p.Play(); p.Advance(TimeSpan.FromMilliseconds(1000)); Assert.Equal(1,eos); Assert.Equal(50,p.EndOfStreamCount); Assert.Equal(0,p.CurrentFrameIndex); p.Advance(TimeSpan.MaxValue); Assert.Equal(2,eos); Assert.Equal(int.MaxValue,p.EndOfStreamCount); }
    [Fact] public void DisposeSuppressesCallbacks() { using var p = Player(10,10); var calls=0; p.MediaEndOfStreamReached += (_,_)=>calls++; p.Play(); p.Dispose(); p.Advance(TimeSpan.FromSeconds(1)); Assert.Equal(0,calls); }
}
