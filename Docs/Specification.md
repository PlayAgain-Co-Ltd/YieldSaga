# Yield-Saga 仕様

ターン制 / ステップ制の処理を、**Intent → Saga → Effect 列 → State** の流れに分解して書くためのフレームワーク。
ユーザーは「状態 / 事実 / やりたいこと / 例外的ルール / 副作用」だけを書けばよい。

ゲーム（StS 系・将棋・三目並べ）に限らず、ターン的な進行を持つもの一般を対象とする
（ワークフロー、対話エージェント、シミュレータなど）。

> **命名の方針**: Redux-saga と意味が一致する saga/effect 側（`ISaga`, `Take`, `Call`）は名前を寄せている。一方、State 更新モデルは CQRS+ES 寄りで Redux と根本的に違うため、その層（`Runtime`, `Intent`, `IEventApplier`, `EffectDispatcher`）は **あえて Redux 用語を使わない**。Redux 経験者が「同じだろう」と読んで誤読しないようにするため。

---

## 1. コアコンセプト

```
Intent ──▶ EffectPipeline ──▶ Effect 列 ──▶ Runtime ──▶ State 更新
                                                │
                                                └─▶ EffectDispatcher ──▶ EffectHandler（副作用）
```

3 層分離が原則:

| 層 | 何をする | 主な型 |
|----|---------|--------|
| 書き換え層（内側） | Intent を Event 列に展開し、Interceptor で Event を加工 | `EffectPipeline` / `ISaga` / `ISagaDecorator` / `IInterceptor` |
| 適用層（中央） | Event を State に反映し、Take/Query を解釈して進行を制御 | `Runtime` / `IEventApplier` / `IAutoIntentProducer` / `IIntentProducer` |
| 観測層（外側） | Effect を購読して副作用を起こす（State は変えない） | `EffectDispatcher` / `IEffectHandler` / `EffectBatch` |

---

## 2. Effect 階層

パイプラインを流れる値は全て `Effect` のサブタイプ。**「状態を変える Event」と「Runtime への制御信号 Signal」の 2 系統に分かれる**。

```
Effect
├── Event            ← ドメインの状態変化
└── Signal           ← Runtime への制御指示（State 不変）
    ├── Take
    ├── Query<T>
    └── Call
```

| 名前 | 役割 | 特徴 |
|------|------|------|
| `Effect` | 全値の親 | `abstract record` |
| `Event : Effect` | 過去形の事実 | **State に適用される唯一のサブタイプ** |
| `Signal : Effect` | Runtime への制御指示 | State 不変、Interceptor 対象外 |
| `Take : Signal` | 入力待ち中断シグナル（Redux-saga `take` 相当） | `Prompt` を持つ。`TryResolve(input)` で解決 |
| `Query<T> : Signal` | 外側で値が埋まる照会 | 純粋な読み取り、State 不変 |
| `Call : Signal` | Intent を展開せよという信号（Redux-saga `call` 相当） | `Intent`, `Sender`, `Scope?`（per-Call `SignalScope`） |

### Prompt（Take が持つ仕様）

| メンバ | 役割 |
|--------|------|
| `Accepts(input)` | この入力を受け付けるか判定 |
| `GetAutoResolution()` | 入力なしで自動解決可能なら入力を返す |

`Prompt` は外側に通知され、対応する入力が来ると `Take` が解決される。

---

## 3. Intent と展開

### Intent
ユーザが投入する論理操作。`abstract record Intent`。

### ISaga\<TIntent\>
Intent を Effect 列に展開する純粋関数（Redux-saga における saga 本体）。
```
IEnumerable<Effect> Run(TIntent intent, Sender sender)
```

### ISagaDecorator\<TIntent\>
展開後の Effect 列を外側で wrap する。
```
IEnumerable<Effect> Decorate(IEnumerable<Effect> effects)
```

継承チェーンに沿って積み重なる（基底型に登録した Decorator は最外層 wrapper として走る）。

---

## 4. State と Event

### State
ドメインの状態。`record` でイミュータブルに持つ。**1 Runtime = 1 State 型**に強制。

### Event
State を変えうる唯一のサブタイプ。`abstract record Event : Effect`。

### IEventApplier\<TState, TEvent\>
ユーザは Event 型ごとに 1 つ実装する。
```
TState Apply(TState state, TEvent ev)
```

ルール:
- 純粋関数。副作用禁止
- 1 Event = 1 Applier（細粒度の方が Interceptor で扱いやすい）

> Redux の `Reducer` と signature が似ているがあえて別名にしている。Redux reducer は **Action** を受けるが、こちらは **Event** を受ける（Action/Event を分けるため）。

フレームワーク側がディスパッチャを内蔵していて、Effect を受け取ったら
Event だけを抽出して該当 Applier に流す（Signal は素通し）。
ユーザはディスパッチャを意識しない。

---

## 5. Interceptor（書き換え層）

Event の発行に介入して、ルールを変える仕組み。装備 / バフ / 状態異常 / 例外的処理など、横断的なロジックを宣言的に書ける。

### IInterceptor\<TEvent\>
```
InterceptResult Apply(TEvent ev, TState preState)
```

`preState` は **その Event が適用される前** の State スナップショット。

### InterceptResult（4 モード自動判別）

| Result | 意味 |
|--------|------|
| `Skip` | この Interceptor は関与しない。次の Interceptor へ |
| `Drop` | Event を完全に消す。以降の Interceptor も走らない |
| `Emit(effects)` | Effect 列を返す。中身に応じて自動判別される ↓ |

`Emit` の自動判別:

| ストリームの中身 | 解釈 |
|------------------|------|
| 受け取った event を**含む** | **装飾**: 周辺 Effect を prefix/suffix に蓄積、次の Interceptor も同 event を見る |
| 単一の同型 event | **変換**: event を新値で置換、次の Interceptor は新 event を見る |
| それ以外 | **置換**: 以降の Interceptor は走らず、ストリームをそのまま流す |

### 順序制御
属性で宣言、構築時にトポロジカルソート、循環検出。
```
[RunBefore(typeof(OtherInterceptor))]
[RunAfter(typeof(OtherInterceptor))]
```

`ISagaDecorator` も同じ属性で順序制御できる。

### 二重発火防止
Interceptor が同型 Event を再 emit したとき、起動元の Interceptor 型は除外される（call-stack ローカルに「適用済み Interceptor 型」を持つ）。

---

## 6. Take（入力待ち）

`Take` を yield すると EffectPipeline は中断する。

| メンバ | 役割 |
|--------|------|
| `Prompt` | 何を待つかの仕様 |
| `IsResolved` | 入力が埋まったか |
| `TryResolve(input)` | 入力を入れる。`Prompt.Accepts(input)` を満たせば成功 |

中断時の挙動:
1. `Runtime` が EffectPipeline を中断
2. `Prompt` を EffectHandler に通知（外側で UI 等を出す）
3. 入力到着 → `TryResolve` → 残り Effect を resume

自動解決:
- `Prompt.GetAutoResolution()` が非 null を返した場合、Runtime は `Take` を即時解決して中断しない
- 例: 選択肢が 1 つしかない場合、外部入力なしで進める

並列入力待ちは非サポート。**直列のみ**。

---

## 7. Runtime（駆動層）

EffectPipeline を駆動するエンジン本体。

> Redux store と違って受動的な state holder ではなく、能動的に pipeline を回す主体。`dispatch` 概念とも違う（reducer に投げるのでなく、pipeline 全体を進める）。

| メンバ | 役割 |
|--------|------|
| `Update(input) → IReadOnlyList<(Effect, State)>` | 入力 1 つを処理し、生成された Effect と適用後 State の列を返す |
| `Latest` | 現在の State |

責務:
- 入力を `IIntentProducer<TInput, TState>` で Intent 列に変換
- Intent を `EffectPipeline` で Effect 列に展開
- 各 Effect を `IEventApplier` で State に反映
- `Take` で停止、入力到着で resume
- `IAutoIntentProducer<TState>` で入力なしの自走進行を回す

> **設計上の narrowing**: Producer は **Effect ではなく Intent** を吐く。Saga が既に Intent → Effect の展開ロジックを持っているので、Producer は「いつ何の Intent を投げるか」だけに集中する。Interceptor / SagaDecorator / Call の再帰展開も全部 Saga 経由で素通りする。

### IIntentProducer\<TInput, TState\>
```
bool CanProduce(TInput input, TState state)
IEnumerable<Intent> Produce(TInput input, IStateProvider<TState> state)
```
外部入力を Intent 列に変換する。Runtime は `CanProduce` を満たす最初の Producer を選んで `Produce` を呼ぶ（dispatch routing）。

### IAutoIntentProducer\<TState\>
```
bool CanProduce(TState state)
IEnumerable<Intent> Produce(IStateProvider<TState> state)
```
State を見て、条件を満たしたら自動で Intent を発行する。外部入力なしで動く。
Runtime は `CanProduce` を満たすものを順に呼んで自走進行を回す。

> **命名**: 役割語 `Producer`（プロアクティブに産み出す）+ Output `Intent` を suffix 直前に置く（`IMessageProducer<T>` 流の BCL 慣例）。`Handler` は reactive ニュアンスのため避ける。

---

## 8. EffectHandler（観測層）

Runtime が出した Effect を購読して副作用を起こす。

### IEffectHandler\<TEffect\>
```
Task OnEffectAsync(TEffect effect, TState state, EffectBatch batch)
```

- DI で集約され、Effect 型階層で自動ディスパッチ
- 並列実行（複数 EffectHandler が同じ Effect を受ける）
- State は変えない（変えたければ Interceptor で）

### EffectBatch
次の `Take` までの Effect 列をまとめ、先読み・重複処理マークを提供する。

| メンバ | 役割 |
|--------|------|
| `Current`, `CurrentState` | 現在処理中の Effect / State |
| `Remaining` | 現在位置から残りの Effect 列 |
| `MoveNext()` | 次の Effect に進む |
| `MarkAsProcessed(handler, effect)` | この EffectHandler が処理済みとマーク |
| `IsProcessed(handler, effect)` | マーク済みか確認 |
| `TakeWhileAndMark<T>(handler, pred)` | 条件を満たす連続 Effect をまとめて取得しマーク |

連続する同型 Effect（連続ダメージ等）を 1 つの演出にまとめたい場合に使う。

### EffectDispatcher
DI で集めた EffectHandler を型階層で引いてバッチ配信する。

> Redux の `store.dispatch` とは別物（あちらは reducer に action を投げる）。こちらは **副作用 handler に effect をブロードキャスト** する観測層の broadcaster。

| メンバ | 役割 |
|--------|------|
| `Task DispatchAsync(EffectBatch batch)` | バッチ内 Effect を順次配信 |
| `Observable<Effect> OnEffect` | Effect 処理完了の通知ストリーム |

---

## 9. EffectPipeline（書き換え層の核）

```
IEnumerable<Effect> Process(Effect effect, SignalScope? scope = null)
```

入力 Effect を 1 つ受け取り、`ISaga` → `ISagaDecorator` → `IInterceptor` を通して最終的な Effect 列を吐く。
`scope` を渡すと、ストリーム中の `Take` / `Query<T>` / `Call` に対する handler が効く（省略時は `SignalScope.Empty`）。

ふるまい:
- `Call`
  - dispatch scope と `Call.Scope` を `Merge` し、以降の再帰に持ち回る（per-Call scope は最深部の Signal まで届く）
  - merged scope の `OnCall<TIntent>` で完全 override されていればそれを採用（`ISagaDecorator` chain も skip）
  - そうでなければ `ISaga` で展開 → `ISagaDecorator` を適用 → 各 Effect を `Process(_, merged scope)` で再帰
- `Event` → `IInterceptor` チェーンを通す。Interceptor が emit した非 Event は `Process(_, scope)` で再投入（scope が深さに関わらず効く）
- `Take` → 解決順序は `Prompt.GetAutoResolution` → `scope.OnTake` → 未解決のまま yield（Runtime が中断）
- `Query<T>` → `scope.OnQuery<T>` で `.Value` を埋めて drop、解決できなければ yield（Runtime が型不一致を throw 検出）

呼び出し契約:
- 各 Effect を `IEventApplier` で State に適用してから次を取り出すこと（遅延列挙）
- `ToList()` 等で事前 materialize 禁止（後続が古い State を読むため）
- Runtime 経由の通常ルートでは TState 用 `OnQuery<TState>` 内蔵 scope が必ず渡されるので、未解決 `Query<TState>` は出てこない

### SignalScope

immutable な handler 束。Pipeline 処理中の Signal に対する 3 種の介入を持つ:

| メソッド | 役割 |
|----------|------|
| `OnTake(Func<Take, object?>)` | 未解決 Take を pre-resolve。null フォールバックで chain |
| `OnQuery<TState>(Func<Query<TState>, TState>)` | Query の `.Value` を埋めて drop |
| `OnCall<TIntent>(Func<TIntent, IEnumerable<Effect>>)` | Intent 展開を完全 override（Decorator も skip） |
| `Merge(SignalScope?)` | 2 つを結合。`other` が `self` を上書き優先 |

3 階層で合成:
1. Runtime が内蔵する `OnQuery<TState>(_ => Latest)`（TState 用 Query resolver、必ず仕込まれる）
2. `Dispatch/Update/Tick/Resume` の optional `scope` 引数で渡す user scope（1 と Merge される）
3. `Call.Scope`（per-Call scope）— Pipeline が Call を展開するとき dispatch scope と Merge される

用途:
- per-Dispatch: AI モード / リプレイ / What-if シミュレーション / テスト用 State モック
- per-Call: SF2 の `WithTargetEnemy(invocation, id)` 相当（内側 Take を pre-resolve / サブ Intent をテストでモック）

---

## 10. Multi-State（複数 State の扱い）

**1 Runtime = 1 State 型**に強制する。複数 State が必要なら Runtime を複数登録。

理由:
- フェーズ（Battle / Map / Shop 等）は状態の形が大きく違う
- Runtime のライフサイクルを State 単位で管理できる
- Interceptor / EffectHandler の登録を State ごとに分割できる（型安全）

### IBridge\<TFromEvent, TToInput\>

複数 Runtime 間の連携は Bridge を書く。
```
IEnumerable<TToInput> Bridge(TFromEvent ev)
```

- 出処側 Runtime の `TFromEvent` が発火したら呼ばれる
- 戻り値の `TToInput` は宛先 Runtime の `Update` に同期で流れる
- `TToInput` は宛先の `IIntentProducer` が受け付ける型（Intent も Input もどちらでも可）
- 単方向のみ（B→A も必要なら別 Bridge を書く）
- DI で自動収集され、出処側に EffectHandler として配線される

### 結果として立ち上がる meta-pipeline

複数 Runtime を Bridge で繋ぐと、外から 1 入力を叩いただけで
**「入力 → 全 State 横断の Event カスケード」** が同期で走る。

```
external input → runtimeA.Update
    → A の Event 列 → Bridge → runtimeB.Update
        → B の Event 列 → Bridge → runtimeC.Update
            → ...
```

これは emergent property であって、フレームワーク側に MetaRuntime のような
統合点は提供しない（型安全を保ち、各 Runtime の独立性を維持するため）。
全 Effect を統一的に観たいユーザは、各 Runtime の EffectHandler 出力を自前で merge する。

---

## 11. 自動進行ロジックはフレームワーク外

「StateA → StateB → StateA → ...」のような遷移ルール自体は **フレームワーク提供しない**。

ユーザーは:
1. State 遷移を表す Event（例: `StateChangedEvent`）を自前で定義
2. `IInterceptor<StateChangedEvent>` で次の Intent を `Emit` する

`IAutoIntentProducer<TState>` は「この State なら自動的に発火する Intent」を持つだけ。
遷移ロジックそのものはユーザー側に置く（State の形に依存するため）。

---

## 12. Interceptor と EffectHandler の住み分け

| | Interceptor | EffectHandler |
|--|-------------|---------------|
| 場所 | EffectPipeline 内部 | Runtime の出力先 |
| いつ | Event 適用**前** | Effect 適用**後** |
| 何ができる | Event を装飾/変換/置換/drop | 副作用（描画・音・ログ・永続化） |
| State | 変えうる（Event を増減して） | 変えられない |
| 入出力 | `Apply(event, preState) → InterceptResult` | `OnEffectAsync(effect, state, batch)` |
| 並列性 | 順序ソートで直列 | 同一 Effect に複数 EffectHandler が並列 |

---

## 13. ユーザーが書くもの一覧

| 種別 | 何を書くか | 何を実装するか |
|------|-----------|--------------|
| 状態 | `TState` | `record` |
| 事実 | `*Event` | `record : Event` |
| 適用 | Event ごとの State 遷移 | `IEventApplier<TState, TEvent>` |
| 操作 | `*Intent` | `record : Intent` |
| 展開 | Intent → Effect 列 | `ISaga<TIntent>` |
| 修飾 | Intent の外側 wrap | `ISagaDecorator<TIntent>`（任意） |
| 介入 | 横断的ルール | `IInterceptor<TEvent>`（任意） |
| 入力 | プロンプト型 | `Prompt` 派生 |
| 入力結線 | 外部入力 → Intent 列 | `IIntentProducer<TInput, TState>` |
| 自動進行 | State を見て自動発火 | `IAutoIntentProducer<TState>`（任意） |
| 観測 | 副作用 | `IEffectHandler<TEffect>` |
| Runtime 連携 | 別 Runtime へ橋渡し | `IBridge<TFromEvent, TToInput>`（任意） |

フレームワーク側が提供するのは:
`Effect` / `Event` / `Signal` / `Take` / `Query<T>` / `Call` / `Intent` / `InterceptResult` / `EffectPipeline` / `Runtime` / `EffectBatch` / `EffectDispatcher` / `IBridge` の配線基盤。

---

## 14. Redux / Redux-saga との対応

「Redux-saga と似てるけど違う」部分を明示するための対応表。

| Yield-Saga | Redux/Redux-saga | 関係 |
|------------|------------------|------|
| `ISaga<TIntent>` | `function* saga(action)` | ほぼ同じ（ジェネレータで Effect を yield） |
| `Take` | `take(pattern)` | ほぼ同じ（マッチする入力を待つ） |
| `Call` | `call(fn, ...)` | ほぼ同じ（別の関数/saga を呼ぶ） |
| `Effect` | `effect` | ほぼ同じ（yield される値） |
| `Query<T>` | `select(selector)` | **違う**: Redux `select` は state 読み。こちらは外部値の埋まり |
| `Intent` | `Action` | **違う**: Redux Action は reducer に直行。Intent は Saga で Event に展開される中間 |
| `IEventApplier<S,E>` | `Reducer<S,A>` | **違う**: 引数の型が Event であり Action ではない（CQRS+ES 系統） |
| `Runtime` | `Store` | **違う**: Redux store は受動的。Runtime は能動的に pipeline 駆動 |
| `EffectDispatcher` | `store.dispatch` | **違う**: あちらは reducer に投げる。こちらは EffectHandler にブロードキャスト |
| `IInterceptor<TEvent>` | `middleware` | **違う**: Redux middleware は Action 単位。Interceptor は Event 単位 |
| `IIntentProducer<TInput, TState>` | — | 独自（外部入力 → Intent 産生） |
| `IAutoIntentProducer<TState>` | — | 独自（State 駆動の自走進行、Intent 産生） |
| `IBridge` | — | 独自（Multi-Runtime 間の橋渡し） |

---

## 15. 全体ストーリー（一文）

> **Intent** を `Runtime` に投げると、`EffectPipeline` が **`ISaga` → `ISagaDecorator` → `IInterceptor`** を経て **Effect 列**（Event と Signal）に展開し、`IEventApplier` で State を更新しつつ **`IEffectHandler`** に流す。`Take` で止まり、入力が来たら resume する。
