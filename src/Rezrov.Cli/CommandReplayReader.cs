namespace Rezrov.Cli;

/// <summary>
/// Lines from a command file first, shown on the console as they are
/// played so the transcript reads as if they had been typed, and then
/// lines from the console.
/// </summary>
/// <remarks>
/// This is the Glulx side of the command line program's --commands
/// option. The Z-Machine has its own command file mechanism in the
/// standard, section 10.2, and its interpreter plays such a file
/// itself; Glk has none, so the display's reader plays the part.
/// </remarks>
internal sealed class CommandReplayReader : TextReader
{
    private readonly TextReader _console;
    private readonly TextWriter _echo;
    private TextReader? _commands;

    public CommandReplayReader(TextReader commands, TextReader console, TextWriter echo)
    {
        _commands = commands;
        _console = console;
        _echo = echo;
    }

    public override string? ReadLine()
    {
        if (_commands is not null)
        {
            var line = _commands.ReadLine();
            if (line is not null)
            {
                _echo.WriteLine(line);
                return line;
            }

            _commands.Dispose();
            _commands = null;
        }

        return _console.ReadLine();
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _commands?.Dispose();
            _commands = null;
        }

        base.Dispose(disposing);
    }
}
