using System.Reflection;

namespace YieldSaga;

/// <summary>
/// Spec §5 順序制御: <see cref="RunBeforeAttribute"/> / <see cref="RunAfterAttribute"/> を
/// 各要素のランタイム型から読み取り、Kahn's algorithm でトポロジカルソートする。
///
/// 同列要素（依存関係のないもの）は登録順を保つよう、優先キューに登録 index を使う。
/// 循環は検出して例外を投げる（メッセージに残った要素の型名を列挙）。
/// </summary>
internal static class TopologicalSort
{
    public static List<T> Sort<T>(IReadOnlyList<T> items, Func<T, Type> typeOf)
    {
        var n = items.Count;
        if (n <= 1) return new List<T>(items);

        // 同一型の複数登録に対応するため type → indices のマップを作る
        var typeToIndices = new Dictionary<Type, List<int>>();
        for (int i = 0; i < n; i++)
        {
            var t = typeOf(items[i]);
            if (!typeToIndices.TryGetValue(t, out var list))
            {
                list = new List<int>();
                typeToIndices[t] = list;
            }
            list.Add(i);
        }

        // 隣接: edges[i] = i の後に来るべき頂点の集合
        var edges = new HashSet<int>[n];
        var inDegree = new int[n];
        for (int i = 0; i < n; i++) edges[i] = new HashSet<int>();

        for (int i = 0; i < n; i++)
        {
            var t = typeOf(items[i]);
            foreach (var before in t.GetCustomAttributes<RunBeforeAttribute>(inherit: true))
            {
                if (!typeToIndices.TryGetValue(before.Target, out var targets)) continue;
                foreach (var j in targets)
                {
                    if (j == i) continue;
                    if (edges[i].Add(j)) inDegree[j]++;
                }
            }
            foreach (var after in t.GetCustomAttributes<RunAfterAttribute>(inherit: true))
            {
                if (!typeToIndices.TryGetValue(after.Target, out var sources)) continue;
                foreach (var j in sources)
                {
                    if (j == i) continue;
                    if (edges[j].Add(i)) inDegree[i]++;
                }
            }
        }

        // 安定性のため、in-degree 0 の中で登録 index の小さい方から取り出す
        var ready = new SortedSet<int>();
        for (int i = 0; i < n; i++)
            if (inDegree[i] == 0) ready.Add(i);

        var result = new List<T>(n);
        while (ready.Count > 0)
        {
            var i = ready.Min;
            ready.Remove(i);
            result.Add(items[i]);
            foreach (var j in edges[i])
                if (--inDegree[j] == 0) ready.Add(j);
        }

        if (result.Count < n)
        {
            var remaining = new List<string>();
            for (int i = 0; i < n; i++)
                if (inDegree[i] > 0) remaining.Add(typeOf(items[i]).Name);
            throw new InvalidOperationException(
                $"Cycle detected in topological ordering. Remaining: {string.Join(", ", remaining)}");
        }

        return result;
    }
}
