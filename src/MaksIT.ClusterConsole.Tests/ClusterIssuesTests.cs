using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ClusterIssuesTests {
  [Fact]
  public void Collect_includes_node_conditions_and_warning_events() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var nodeCreated = now.AddDays(-120);
    var eventSeen = now.AddMinutes(-176);
    var nodes = new[] {
      Node("k3ssrv0001", "n1", nodeCreated, [
        Condition("Ready", "True"),
        Condition("EtcdIsVoter", "True", "Node is a voting member of the etcd cluster"),
        Condition("MemoryPressure", "True", "kubelet has memory pressure")
      ])
    };
    var events = new[] {
      Event("e1", "Warning", "invalid capacity 0 on image filesystem", "Node", "k3ssrv0001", "n1", eventSeen),
      Event("e2", "Normal", "Started", "Pod", "ok", "p-ok", eventSeen)
    };

    var set = ClusterIssues.Collect(nodes, events, [], now);

    Assert.Equal(2, set.Warnings.Count);
    Assert.Empty(set.Errors);
    var node = Assert.Single(set.Warnings, w => w.Kind == "Node");
    Assert.Equal("k3ssrv0001", node.ObjectName);
    Assert.Contains("memory pressure", node.Message);
    Assert.Equal(ClusterIssues.Active, node.State);
    Assert.Equal("120d", node.Age);

    var warning = Assert.Single(set.Warnings, w => w.Kind == "Event");
    Assert.StartsWith("invalid capacity", warning.Message);
    Assert.Equal(ClusterIssues.Resolved, warning.State);
    Assert.Equal("2h56m", warning.Age);
  }

  [Fact]
  public void Collect_skips_healthy_etcd_voter_condition() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var nodes = new[] {
      Node("k3ssrv0001", "n1", now.AddDays(-121), [
        Condition("Ready", "True"),
        Condition("EtcdIsVoter", "True", "Node is a voting member of the etcd cluster")
      ])
    };

    var set = ClusterIssues.Collect(nodes, [], [], now);

    Assert.Empty(set.Warnings);
    Assert.Empty(set.Errors);
  }

  [Fact]
  public void Collect_warns_when_etcd_is_not_a_voter() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var nodeCreated = now.AddDays(-121);
    var nodes = new[] {
      Node("k3ssrv0001", "n1", nodeCreated, [
        Condition("Ready", "True"),
        Condition("EtcdIsVoter", "False", "this server has not yet been promoted from learner to voting member")
      ])
    };

    var set = ClusterIssues.Collect(nodes, [], [], now);

    var warning = Assert.Single(set.Warnings);
    Assert.Equal("Node", warning.Kind);
    Assert.Equal("k3ssrv0001", warning.ObjectName);
    Assert.Contains("learner", warning.Message);
    Assert.Equal(ClusterIssues.Active, warning.State);
    Assert.Equal("121d", warning.Age);
    Assert.Empty(set.Errors);
  }

  [Fact]
  public void Collect_keeps_latest_warning_per_involved_object() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var older = now.AddHours(-4);
    var newer = now.AddMinutes(-2);
    var events = new[] {
      Event("e1", "Warning", "old", "Node", "n", "uid", older, "Ready"),
      Event("e2", "Warning", "new", "Node", "n", "uid", newer, "Ready")
    };

    var set = ClusterIssues.Collect([], events, [], now);

    var warning = Assert.Single(set.Warnings);
    Assert.Equal("new", warning.Message);
    Assert.Equal(ClusterIssues.Active, warning.State);
  }

  [Fact]
  public void Collect_skips_healthy_pod_warnings_and_keeps_unready_pods() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var seen = now.AddMinutes(-3);
    var healthy = Pod("ok", "p-ok", "Running", ready: true, priority: 0);
    var unready = Pod("bad", "p-bad", "Running", ready: false, priority: 0);
    var events = new[] {
      Event("e1", "Warning", "probe failed", "Pod", "ok", "p-ok", seen),
      Event("e2", "Warning", "probe failed", "Pod", "bad", "p-bad", seen)
    };

    var set = ClusterIssues.Collect([], events, [healthy, unready], now);

    var warning = Assert.Single(set.Warnings);
    Assert.Equal("bad", warning.ObjectName);
    Assert.Equal(ClusterIssues.Active, warning.State);
  }

  [Fact]
  public void Collect_puts_error_typed_events_in_errors() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var seen = now.AddMinutes(-5);
    var events = new[] {
      Event("e1", "Error", "sync failed", "Deployment", "api", "d1", seen)
    };

    var set = ClusterIssues.Collect([], events, [], now);

    Assert.Empty(set.Warnings);
    var error = Assert.Single(set.Errors);
    Assert.Equal("Event", error.Kind);
    Assert.Equal("sync failed", error.Message);
    Assert.Equal("Error", error.Severity);
    Assert.Equal(ClusterIssues.Active, error.State);
  }

  [Fact]
  public void Collect_marks_stale_events_resolved() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var events = new[] {
      Event(
        "e1",
        "Warning",
        "Waiting for disk default-disk-080000000000 (/mnt/longhorn) on node k3ssrv0001 to be ready",
        "Node",
        "k3ssrv0001",
        "n1",
        now.AddMinutes(-26),
        "Ready")
    };

    var set = ClusterIssues.Collect([], events, [], now);

    var warning = Assert.Single(set.Warnings);
    Assert.Equal(ClusterIssues.Resolved, warning.State);
  }

  [Fact]
  public void Collect_marks_warning_resolved_when_later_normal_has_same_reason() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var warningAt = now.AddMinutes(-3);
    var events = new[] {
      Event("e1", "Warning", "Waiting for disk to be ready", "Node", "k3ssrv0001", "n1", warningAt, "Ready"),
      Event("e2", "Normal", "Disk default-disk-080000000000(/mnt/longhorn) on node k3ssrv0001 is ready", "Node", "k3ssrv0001", "n1", warningAt.AddSeconds(1), "Ready")
    };

    var set = ClusterIssues.Collect([], events, [], now);

    var warning = Assert.Single(set.Warnings);
    Assert.Equal(ClusterIssues.Resolved, warning.State);
    Assert.StartsWith("Waiting for disk", warning.Message);
  }

  [Fact]
  public void Collect_keeps_unhealthy_pod_warning_active_when_stale() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var unready = Pod("bad", "p-bad", "Pending", ready: false, priority: 0);
    var events = new[] {
      Event("e1", "Warning", "0/3 nodes are available", "Pod", "bad", "p-bad", now.AddMinutes(-20), "FailedScheduling")
    };

    var set = ClusterIssues.Collect([], events, [unready], now);

    var warning = Assert.Single(set.Warnings);
    Assert.Equal(ClusterIssues.Active, warning.State);
  }

  [Fact]
  public void Caption_mentions_resolved_counts() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    Assert.Equal("Warnings: 0", ClusterIssues.Caption("Warnings", []));
    Assert.Equal(
      "Warnings: 1",
      ClusterIssues.Caption("Warnings", [
        Issue("a", ClusterIssues.Active, now)
      ]));
    Assert.Equal(
      "Warnings: 2 resolved",
      ClusterIssues.Caption("Warnings", [
        Issue("a", ClusterIssues.Resolved, now),
        Issue("b", ClusterIssues.Resolved, now)
      ]));
    Assert.Equal(
      "Warnings: 1 (2 resolved)",
      ClusterIssues.Caption("Warnings", [
        Issue("a", ClusterIssues.Active, now),
        Issue("b", ClusterIssues.Resolved, now),
        Issue("c", ClusterIssues.Resolved, now)
      ]));
  }

  [Fact]
  public void Age_formats_hours_and_minutes() {
    var now = DateTimeOffset.Parse("2026-08-19T15:00:00Z");
    var when = now.AddHours(-4).AddMinutes(-30);
    Assert.Equal("4h30m", JsonPath.Age(when, now));
  }

  private static ClusterIssue Issue(string id, string state, DateTimeOffset at) =>
    new(id, "msg", "obj", "Event", "1m", at, "Warning", state);

  private static JsonObject Node(string name, string uid, DateTimeOffset created, JsonObject[] conditions) =>
    new() {
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["uid"] = uid,
        ["creationTimestamp"] = created.UtcDateTime.ToString("o")
      },
      ["status"] = new JsonObject {
        ["conditions"] = new JsonArray(conditions.Select(c => c.DeepClone()).ToArray())
      }
    };

  private static JsonObject Condition(string type, string status, string? message = null) {
    var obj = new JsonObject { ["type"] = type, ["status"] = status };
    if (message is not null)
      obj["message"] = message;
    return obj;
  }

  private static JsonObject Event(
    string uid,
    string type,
    string message,
    string kind,
    string name,
    string involvedUid,
    DateTimeOffset lastTimestamp,
    string? reason = null) =>
    new() {
      ["metadata"] = new JsonObject { ["uid"] = uid, ["creationTimestamp"] = lastTimestamp.UtcDateTime.ToString("o") },
      ["type"] = type,
      ["reason"] = reason ?? type,
      ["message"] = message,
      ["lastTimestamp"] = lastTimestamp.UtcDateTime.ToString("o"),
      ["involvedObject"] = new JsonObject {
        ["kind"] = kind,
        ["name"] = name,
        ["uid"] = involvedUid
      }
    };

  private static JsonObject Pod(string name, string uid, string phase, bool ready, int priority) =>
    new() {
      ["metadata"] = new JsonObject { ["name"] = name, ["uid"] = uid, ["namespace"] = "kube-system" },
      ["spec"] = new JsonObject { ["priority"] = priority },
      ["status"] = new JsonObject {
        ["phase"] = phase,
        ["conditions"] = new JsonArray {
          new JsonObject { ["type"] = "Ready", ["status"] = ready ? "True" : "False" }
        }
      }
    };
}
