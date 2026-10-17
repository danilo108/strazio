namespace Strazio.Cli;

public interface IUserInput
{
    bool IsAvailable { get; }
    string? ReadLine();
}

public sealed class ConsoleUserInput : IUserInput
{
    public bool IsAvailable => !Console.IsInputRedirected;
    public string? ReadLine() => Console.ReadLine();
}
