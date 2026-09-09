namespace Rezrov.ZMachine.Sound;

/// <summary>
/// One sound effect a game can ask for, as a frontend receives it.
/// </summary>
/// <remarks>
/// [zm 9.2] Sound effects are numbered from 3 upward, 1 and 2 being
/// the bleeps, and [zm 9.2.1] come in two kinds, samples and music,
/// which the game cannot tell apart but the interpreter must, since
/// [zm 9.4.2] only one of each kind plays at a time. [blorb 3] The
/// format says which kind a sound is.
/// </remarks>
/// <param name="Number">
/// [zm 9.2] The number the game uses, 3 or more.
/// </param>
/// <param name="Format">
/// [blorb 3] AIFF, OGGV, MOD, or SONG, as the resource file has it.
/// </param>
/// <param name="Data">
/// The sound file's bytes, a complete AIFF file for that format.
/// </param>
/// <param name="PlaysOnce">
/// [blorb 11.4] For a Version 3 game, whether the sound plays once
/// rather than repeating until stopped, which the resource file says
/// because the opcode cannot.
/// </param>
public sealed record SoundResource(int Number, string Format, ReadOnlyMemory<byte> Data, bool PlaysOnce = true)
{
    /// <summary>
    /// [blorb 14.3] Music, which has its own channel: MOD and SONG.
    /// </summary>
    public bool IsMusic => Format is "MOD " or "SONG";
}
