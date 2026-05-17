namespace YieldSaga;

/// <summary>
/// Spec §3: Intent を Effect 列に展開する純粋関数。
/// State 読みが必要なら <see cref="Query{T}"/> を yield して外側で埋めてもらう。
/// </summary>
public interface ISaga<in TIntent> where TIntent : Intent
{
    IEnumerable<Effect> Run(TIntent intent, Sender sender);
}
