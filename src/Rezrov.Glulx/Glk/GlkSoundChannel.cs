namespace Rezrov.Glulx.Glk;

/// <summary>
/// A Glk sound channel: a place one sound plays at a time, with a
/// volume and a pause of its own.
/// </summary>
/// <remarks>
/// [glk #sound] A channel plays exactly one sound at a time; more
/// sounds at once take more channels, and one sound may play on
/// several. The library sets what is here and the frontend reads it;
/// the frontend's own state for the channel, a mixer voice or the
/// like, is the frontend's to keep.
/// </remarks>
public sealed class GlkSoundChannel : GlkObject
{
    /// <summary>
    /// [glk #sound_channels] Full volume, which a channel starts at.
    /// </summary>
    public const uint FullVolume = 0x10000;

    internal GlkSoundChannel(uint rock, uint volume)
        : base(rock)
    {
        Volume = volume;
    }

    /// <summary>
    /// [glk #sound_channels] The volume, 0 for silence and
    /// <see cref="FullVolume"/> for full, or the volume a change under
    /// way is heading for.
    /// </summary>
    public uint Volume { get; internal set; }

    /// <summary>
    /// [glk op:schannel_pause] Whether the channel is paused.
    /// </summary>
    public bool Paused { get; internal set; }

    /// <summary>
    /// [glk #sound_playing] The resource number of the sound playing,
    /// or null when nothing is, as far as the library has heard.
    /// </summary>
    public uint? Playing { get; internal set; }

    /// <summary>
    /// The notify value of the sound playing, zero for no event wanted.
    /// </summary>
    internal uint Notify { get; set; }

    /// <summary>
    /// Counts the sounds started on the channel, so that the end of a
    /// sound that was stopped or replaced is told from the end of the
    /// one playing now.
    /// </summary>
    internal uint Generation { get; set; }

    /// <summary>
    /// Counts the volume changes, for the same reason.
    /// </summary>
    internal uint VolumeGeneration { get; set; }
}
