namespace MaksIT.ClusterConsole.Shared;

public static class CollectionSync {
  public static void MergeByKey<T, TKey>(
    IList<T> target,
    IReadOnlyList<T> source,
    Func<T, TKey> keySelector,
    Action<T, T>? copy = null,
    bool matchSourceOrder = false)
    where TKey : notnull {
    ArgumentNullException.ThrowIfNull(target);
    ArgumentNullException.ThrowIfNull(source);
    ArgumentNullException.ThrowIfNull(keySelector);

    var nextKeys = new HashSet<TKey>();
    var nextItems = new List<(TKey Key, T Item)>(source.Count);
    foreach (var item in source) {
      var key = keySelector(item);
      if (!nextKeys.Add(key))
        continue;

      nextItems.Add((key, item));
    }

    for (var i = target.Count - 1; i >= 0; i--) {
      if (!nextKeys.Contains(keySelector(target[i])))
        target.RemoveAt(i);
    }

    var existing = new Dictionary<TKey, T>();
    foreach (var item in target)
      existing[keySelector(item)] = item;

    foreach (var (key, item) in nextItems) {
      if (!existing.TryGetValue(key, out var current)) {
        target.Add(item);
        continue;
      }

      if (copy is not null) {
        copy(current, item);
        continue;
      }

      if (EqualityComparer<T>.Default.Equals(current, item))
        continue;

      var index = target.IndexOf(current);
      if (index >= 0)
        target[index] = item;
    }

    if (!matchSourceOrder)
      return;

    var byKey = new Dictionary<TKey, T>();
    foreach (var item in target)
      byKey[keySelector(item)] = item;

    for (var i = 0; i < nextItems.Count; i++) {
      if (!byKey.TryGetValue(nextItems[i].Key, out var item))
        continue;
      var at = target.IndexOf(item);
      if (at < 0 || at == i)
        continue;
      target.RemoveAt(at);
      target.Insert(i, item);
    }
  }
}
