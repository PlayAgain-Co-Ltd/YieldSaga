using System.Collections.Immutable;
using YieldSaga;

namespace TicTacToe;

// ─── Domain ──────────────────────────────────────────────────────

public enum Mark { Empty, X, O }
public enum Player { X, O }

public sealed record TicTacToeState(
    ImmutableArray<Mark> Board,
    Player ToMove,
    Player? Winner,
    bool IsDraw)
{
    public static TicTacToeState Initial { get; } = new(
        Enumerable.Repeat(Mark.Empty, 9).ToImmutableArray(),
        Player.X,
        Winner: null,
        IsDraw: false);

    public bool GameOver => Winner is not null || IsDraw;
}

// Sender はフレームワーク側で default を持たない（ドメインごとに定義する）。
public sealed record GameSender : Sender;

// ─── Events (State を変える事実) ────────────────────────────────

public sealed record MarkPlacedEvent(int Cell, Player By) : Event;
public sealed record GameWonEvent(Player Winner) : Event;
public sealed record GameDrawEvent : Event;

// ─── Intents (やりたいこと) ─────────────────────────────────────

public sealed record HumanTurnIntent(Player Player, ImmutableArray<Mark> Board) : Intent;
public sealed record CpuTurnIntent(Player Player, ImmutableArray<Mark> Board) : Intent;

// ─── Prompt (Take が要求する入力の仕様) ─────────────────────────

public sealed record MovePrompt(Player ToMove, ImmutableArray<Mark> Board) : Prompt
{
    public override bool Accepts(object input)
        => input is int i && i >= 0 && i < 9 && Board[i] == Mark.Empty;
}

// ─── Sagas (Intent → Effect 列に展開) ──────────────────────────

public sealed class HumanTurnSaga : ISaga<HumanTurnIntent>
{
    public IEnumerable<Effect> Run(HumanTurnIntent intent, Sender sender)
    {
        // Take を yield すると Runtime は外部入力が来るまで中断。
        var take = new Take(new MovePrompt(intent.Player, intent.Board));
        yield return take;
        // Resume されると同じ iterator がここから再開する。
        var index = (int)take.ResolvedInput!;
        yield return new MarkPlacedEvent(index, intent.Player);
    }
}

public sealed class CpuTurnSaga : ISaga<CpuTurnIntent>
{
    public IEnumerable<Effect> Run(CpuTurnIntent intent, Sender sender)
    {
        var emptyCells = new List<int>();
        for (int i = 0; i < 9; i++)
            if (intent.Board[i] == Mark.Empty) emptyCells.Add(i);
        var choice = emptyCells[Random.Shared.Next(emptyCells.Count)];
        yield return new MarkPlacedEvent(choice, intent.Player);
    }
}

// ─── Appliers (Event を State に反映、純粋関数) ─────────────────

public sealed class MarkPlacedApplier : IEventApplier<TicTacToeState, MarkPlacedEvent>
{
    public TicTacToeState Apply(TicTacToeState s, MarkPlacedEvent e) => s with
    {
        Board = s.Board.SetItem(e.Cell, e.By == Player.X ? Mark.X : Mark.O),
        ToMove = s.ToMove == Player.X ? Player.O : Player.X,
    };
}

public sealed class GameWonApplier : IEventApplier<TicTacToeState, GameWonEvent>
{
    public TicTacToeState Apply(TicTacToeState s, GameWonEvent e) => s with { Winner = e.Winner };
}

public sealed class GameDrawApplier : IEventApplier<TicTacToeState, GameDrawEvent>
{
    public TicTacToeState Apply(TicTacToeState s, GameDrawEvent e) => s with { IsDraw = true };
}

// ─── Interceptor (横断ルール: 勝敗判定) ────────────────────────

public sealed class WinDetectionInterceptor : IInterceptor<TicTacToeState, MarkPlacedEvent>
{
    private static readonly int[][] Lines =
    [
        [0,1,2], [3,4,5], [6,7,8],
        [0,3,6], [1,4,7], [2,5,8],
        [0,4,8], [2,4,6],
    ];

    public InterceptResult Apply(MarkPlacedEvent ev, TicTacToeState preState)
    {
        // この event を適用した「後」の盤面をシミュレートして勝敗を見る。
        var mark = ev.By == Player.X ? Mark.X : Mark.O;
        var post = preState.Board.SetItem(ev.Cell, mark);

        // 元 event を含む emit → Decorate モード自動判別。
        // 元 event は素通り、追加された Game*Event は別 Event として後段にも流れる。
        if (HasWin(post, mark))
            return InterceptResult.Emit(ev, new GameWonEvent(ev.By));
        if (post.All(c => c != Mark.Empty))
            return InterceptResult.Emit(ev, new GameDrawEvent());
        return InterceptResult.Skip;
    }

    private static bool HasWin(ImmutableArray<Mark> board, Mark mark) =>
        Lines.Any(line => line.All(i => board[i] == mark));
}

// ─── AutoIntentProducer (State 駆動でターンを自動発火) ─────────

public sealed class HumanTurnProducer : IAutoIntentProducer<TicTacToeState>
{
    public bool CanProduce(TicTacToeState s) => !s.GameOver && s.ToMove == Player.X;
    public IEnumerable<Intent> Produce(IStateProvider<TicTacToeState> state)
    {
        var s = state.Current;
        yield return new HumanTurnIntent(s.ToMove, s.Board);
    }
}

public sealed class CpuTurnProducer : IAutoIntentProducer<TicTacToeState>
{
    public bool CanProduce(TicTacToeState s) => !s.GameOver && s.ToMove == Player.O;
    public IEnumerable<Intent> Produce(IStateProvider<TicTacToeState> state)
    {
        var s = state.Current;
        yield return new CpuTurnIntent(s.ToMove, s.Board);
    }
}

// ─── Program (Runtime を組み立てて回す) ────────────────────────

public static class Program
{
    public static void Main()
    {
        var sagas = new SagaRegistry();
        sagas.Register(new HumanTurnSaga());
        sagas.Register(new CpuTurnSaga());

        var appliers = new EventApplierRegistry<TicTacToeState>();
        appliers.Register(new MarkPlacedApplier());
        appliers.Register(new GameWonApplier());
        appliers.Register(new GameDrawApplier());

        var interceptors = new InterceptorRegistry<TicTacToeState>();
        interceptors.Register(new WinDetectionInterceptor());

        var auto = new AutoIntentProducerRegistry<TicTacToeState>();
        auto.Register(new HumanTurnProducer());
        auto.Register(new CpuTurnProducer());

        var runtime = new Runtime<TicTacToeState>(
            TicTacToeState.Initial,
            sagas,
            appliers,
            interceptors,
            autoProducers: auto);

        var sender = new GameSender();

        Console.WriteLine("Tic-Tac-Toe (You: X, CPU: O)\n");

        // Tick で自走ループを始動: 初手は X (人間) なので HumanTurnSaga が Take を出して即中断。
        runtime.Tick(sender);

        while (!runtime.Latest.GameOver)
        {
            if (runtime.IsPaused)
            {
                var prompt = (MovePrompt)runtime.PendingTake!.Prompt;
                PrintBoard(runtime.Latest.Board);
                int cell = AskCell(prompt);
                // Resume すると saga が再開し、その後 auto-loop が CPU 手番まで進めて再中断 or 終了。
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
