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

public sealed record MovePrompt(Player ToMove, ImmutableArray<Mark> Board) : Prompt<int>
{
    public override bool Accepts(int input)
        => input >= 0 && input < 9 && Board[input] == Mark.Empty;
}

// ─── Sagas (Intent → Effect 列に展開) ──────────────────────────

public sealed class HumanTurnSaga : ISaga<HumanTurnIntent>
{
    public IEnumerable<Effect> Run(HumanTurnIntent intent, Sender sender)
    {
        // Read.Input を yield すると Runtime は外部入力が来るまで中断。
        // MovePrompt が Prompt<int> なので <int> 型引数は推論される。
        // Resume されると同じ iterator がここから再開し、move.Value で typed に取れる。
        yield return Read.Input(out var move, new MovePrompt(intent.Player, intent.Board));
        yield return new MarkPlacedEvent(move.Value, intent.Player);
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
    public Intent Produce(TicTacToeState s) => new HumanTurnIntent(s.ToMove, s.Board);
}

public sealed class CpuTurnProducer : IAutoIntentProducer<TicTacToeState>
{
    public bool CanProduce(TicTacToeState s) => !s.GameOver && s.ToMove == Player.O;
    public Intent Produce(TicTacToeState s) => new CpuTurnIntent(s.ToMove, s.Board);
}
