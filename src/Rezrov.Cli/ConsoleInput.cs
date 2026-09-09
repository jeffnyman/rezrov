using Rezrov.ZMachine;
using Rezrov.ZMachine.Execution;
using Rezrov.ZMachine.Input;
using Rezrov.ZMachine.Text;

namespace Rezrov.Cli;

/// <summary>
/// The console as a keyboard: a <see cref="TextReaderInput"/> over
/// standard input, with two things a plain reader cannot know about.
/// </summary>
/// <remarks>
/// When standard input is a pipe or a file nothing shows the commands
/// as they are consumed, so they are echoed, which keeps a transcript
/// readable. And [zm 10.2.3] when the game asks for input stream 1 the
/// player is asked for a file name, which is the simplest of the "any
/// method" the standard allows.
/// </remarks>
internal sealed class ConsoleInput : IInput
{
    private readonly TextReaderInput _keyboard;
    private readonly TextWriterOutput _echo;

    public ConsoleInput(StoryHeader header, ZMemory memory)
    {
        _keyboard = new TextReaderInput(Console.In, header, memory);
        _echo = new TextWriterOutput(Console.Out, header, memory);
    }

    public bool SupportsTimedInput => false;

    public LineInput ReadLine(LineInputRequest request)
    {
        var line = _keyboard.ReadLine(request);

        if (Console.IsInputRedirected)
        {
            for (var i = request.Initial.Count; i < line.Text.Count; i++)
            {
                _echo.Print(line.Text[i]);
            }

            _echo.Print(Zscii.Newline);
        }

        return line;
    }

    public ushort ReadKey(InputTimer? timer) => _keyboard.ReadKey(timer);

    public TextReader? OpenCommandFile()
    {
        Console.Error.Write("Command file: ");
        var path = Console.In.ReadLine()?.Trim();

        if (string.IsNullOrEmpty(path))
        {
            return null;
        }

        if (!File.Exists(path))
        {
            Console.Error.WriteLine($"rezrov: no such file: {path}");
            return null;
        }

        return new StreamReader(path);
    }
}
