using System.Collections.Immutable;
using Microsoft.Extensions.DependencyInjection;
using TicTacToe;
using YieldSaga;
using YieldSaga.Extensions.DependencyInjection;

namespace TicTacToe.Di;

public static class Program
{
    public static void Main()
    {
        var services = new ServiceCollection();
        services.AddYieldSagaRuntime<TicTacToeState>(b => b
            .WithInitialState(TicTacToeState.Initial)
            .ScanFromAssemblyOf<HumanTurnSaga>());
        services.AddSingleton<Sender, GameSender>();

        using var sp = services.BuildServiceProvider();
        var runtime = sp.GetRequiredService<Runtime<TicTacToeState>>();
        var sender = sp.GetRequiredService<Sender>();

        Console.WriteLine("Tic-Tac-Toe (You: X, CPU: O) — DI edition\n");

        runtime.Tick(sender);

        while (!runtime.Latest.GameOver)
        {
            if (runtime.IsPaused)
            {
                var prompt = (MovePrompt)runtime.PendingTake!.Prompt;
                PrintBoard(runtime.Latest.Board);
                int cell = AskCell(prompt);
                runtime.Resume(cell);
            }
            else
            {
                runtime.Tick(sender);
            }
        }

        PrintBoard(runtime.Latest.Board);
        Console.WriteLine(runtime.Latest.Winner is { } w ? $"Winner: {w}" : "Draw");
    }

    private static int AskCell(MovePrompt prompt)
    {
        while (true)
        {
            Console.Write("Your move (0-8): ");
            var line = Console.ReadLine();
            if (int.TryParse(line, out var cell) && prompt.Accepts(cell))
                return cell;
            Console.WriteLine("Invalid. Pick an empty cell index 0-8.");
        }
    }

    private static void PrintBoard(ImmutableArray<Mark> board)
    {
        static char Sym(Mark m, int i) => m switch
        {
            Mark.X => 'X',
            Mark.O => 'O',
            _ => (char)('0' + i),
        };
        for (int row = 0; row < 3; row++)
        {
            int b = row * 3;
            Console.WriteLine($" {Sym(board[b], b)} | {Sym(board[b+1], b+1)} | {Sym(board[b+2], b+2)}");
            if (row < 2) Console.WriteLine("---+---+---");
        }
        Console.WriteLine();
    }
}
