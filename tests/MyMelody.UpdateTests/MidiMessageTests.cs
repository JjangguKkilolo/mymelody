using MyMelody.App.Services;
using Xunit;

namespace MyMelody.UpdateTests;

public sealed class MidiMessageTests
{
    [Theory]
    [InlineData(0x643c90u, 60, 100, 1)]
    [InlineData(0x017f9fu, 127, 1, 16)]
    public void ValidNoteOnIsDecoded(uint message, int expectedNote, int expectedVelocity, int expectedChannel)
    {
        Assert.True(MidiInputService.TryParseNoteOn(message, out var note, out var velocity, out var channel));
        Assert.Equal(expectedNote, note);
        Assert.Equal(expectedVelocity, velocity);
        Assert.Equal(expectedChannel, channel);
    }

    [Theory]
    [InlineData(0x003c90u)] // Zero velocity is MIDI note-off.
    [InlineData(0x643c80u)] // Explicit note-off.
    [InlineData(0x0040b0u)] // Sustain/control change.
    [InlineData(0x0000f8u)] // MIDI clock.
    [InlineData(0x0000feu)] // Active sensing.
    [InlineData(0xff3c90u)] // Invalid data byte.
    [InlineData(0x64ff90u)]
    public void NonPerformanceMessagesAreIgnored(uint message)
        => Assert.False(MidiInputService.TryParseNoteOn(message, out _, out _, out _));
}
