using System.Collections.ObjectModel;
using System.Collections.Specialized;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class CollectionSyncTests {
  [Fact]
  public void MergeByKey_keeps_existing_instances_and_does_not_reset() {
    var first = Item("a", 1);
    var second = Item("b", 1);
    var target = new ObservableCollection<KeyedItem> { first, second };
    var resets = 0;
    target.CollectionChanged += (_, e) => {
      if (e.Action == NotifyCollectionChangedAction.Reset)
        resets++;
    };

    var incomingA = Item("a", 2);
    CollectionSync.MergeByKey(
      target,
      [incomingA, Item("c", 1)],
      item => item.Key,
      static (current, incoming) => current.Value = incoming.Value);

    Assert.Equal(0, resets);
    Assert.Equal(2, target.Count);
    Assert.Same(first, target[0]);
    Assert.Equal(2, first.Value);
    Assert.DoesNotContain(second, target);
    Assert.Equal("c", target[1].Key);
  }

  [Fact]
  public void MergeByKey_can_reorder_to_match_source() {
    var first = Item("a", 1);
    var second = Item("b", 1);
    var third = Item("c", 1);
    var target = new ObservableCollection<KeyedItem> { first, second, third };

    CollectionSync.MergeByKey(
      target,
      [third, first, second],
      item => item.Key,
      matchSourceOrder: true);

    Assert.Equal(["c", "a", "b"], target.Select(item => item.Key).ToList());
    Assert.Same(third, target[0]);
    Assert.Same(first, target[1]);
  }

  [Fact]
  public void MergeByKey_replaces_records_without_reset() {
    var target = new ObservableCollection<ClusterIssue> {
      Issue("w1", "old")
    };
    var resets = 0;
    target.CollectionChanged += (_, e) => {
      if (e.Action == NotifyCollectionChangedAction.Reset)
        resets++;
    };

    CollectionSync.MergeByKey(target, [Issue("w1", "new")], issue => issue.Id);

    Assert.Equal(0, resets);
    Assert.Equal("new", Assert.Single(target).Age);
  }

  private static KeyedItem Item(string key, int value) =>
    new() { Key = key, Value = value };

  private static ClusterIssue Issue(string id, string age) =>
    new(id, "message", "obj", "Pod", age, DateTimeOffset.UnixEpoch, "Warning", ClusterIssues.Active);

  private sealed class KeyedItem {
    public required string Key { get; init; }

    public int Value { get; set; }
  }
}
