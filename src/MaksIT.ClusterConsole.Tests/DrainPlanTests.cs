using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Tests;

public class DrainPlanTests {
  [Fact]
  public void ForNode_evicts_managed_pods_and_holds_disruption_budgets() {
    var pods = new[] {
      Pod("api-0", "apps", "node-a", "Running", "ReplicaSet", new JsonObject { ["app"] = "api" }),
      Pod("db-0", "apps", "node-a", "Running", "StatefulSet", new JsonObject { ["cnpg.io/cluster"] = "db" }),
      Pod("cilium", "kube-system", "node-a", "Running", "DaemonSet", []),
      Pod("done", "apps", "node-a", "Succeeded", "Job", []),
      Pod("other", "apps", "node-b", "Running", "ReplicaSet", [])
    };
    var budgets = new[] {
      new JsonObject {
        ["metadata"] = new JsonObject { ["name"] = "db", ["namespace"] = "apps" },
        ["spec"] = new JsonObject {
          ["selector"] = new JsonObject {
            ["matchLabels"] = new JsonObject { ["cnpg.io/cluster"] = "db" }
          }
        },
        ["status"] = new JsonObject { ["disruptionsAllowed"] = 0 }
      }
    };

    var plan = DrainPlan.ForNode("node-a", pods, budgets);

    Assert.Equal(DrainPlan.Evict, Action(plan, "api-0"));
    Assert.Equal(DrainPlan.Blocked, Action(plan, "db-0"));
    Assert.Contains("allows 0", Reason(plan, "db-0"));
    Assert.Equal(DrainPlan.Skip, Action(plan, "cilium"));
    Assert.Equal("DaemonSet", Reason(plan, "cilium"));
    Assert.Equal(DrainPlan.Skip, Action(plan, "done"));
    Assert.DoesNotContain(plan.Pods, pod => pod.Name == "other");
    var text = DrainPlan.Format([plan]);
    Assert.Contains("Will move (1)", text);
    Assert.Contains("apps/api-0", text);
    Assert.Contains("Will remain (3)", text);
    Assert.Contains("apps/db-0", text);
    Assert.Contains("DaemonSet", text);
    Assert.Contains("not deleted", text);
  }

  [Fact]
  public void ForNode_skips_mirror_terminating_and_unmanaged_pods() {
    var pods = new[] {
      Mirror("static"),
      Terminating("gone"),
      Unmanaged("manual"),
      Pod("keep", "apps", "node-a", "Running", "ReplicaSet", new JsonObject { ["app"] = "api" }),
      Pod("drop", "apps", "node-a", "Running", "ReplicaSet", new JsonObject { ["app"] = "batch" })
    };
    var budgets = new[] {
      new JsonObject {
        ["metadata"] = new JsonObject { ["name"] = "api", ["namespace"] = "apps" },
        ["spec"] = new JsonObject {
          ["selector"] = new JsonObject {
            ["matchExpressions"] = new JsonArray {
              new JsonObject {
                ["key"] = "app",
                ["operator"] = "In",
                ["values"] = new JsonArray("api")
              }
            }
          }
        },
        ["status"] = new JsonObject { ["disruptionsAllowed"] = 0 }
      },
      new JsonObject {
        ["metadata"] = new JsonObject { ["name"] = "ignored", ["namespace"] = "apps" },
        ["spec"] = new JsonObject { ["selector"] = new JsonObject() }
      }
    };

    var plan = DrainPlan.ForNode("node-a", pods, budgets);

    Assert.Equal("mirror pod", Reason(plan, "static"));
    Assert.Equal("terminating", Reason(plan, "gone"));
    Assert.Equal("no controller", Reason(plan, "manual"));
    Assert.Equal(DrainPlan.Blocked, Action(plan, "keep"));
    Assert.Equal(DrainPlan.Evict, Action(plan, "drop"));

    var empty = DrainPlan.Format([new DrainNodePlan("idle", [])]);
    Assert.Contains("Will move (0)", empty);
    Assert.Contains("None", empty);
  }

  private static JsonObject Mirror(string name) {
    var pod = Pod(name, "kube-system", "node-a", "Running", "Node", []);
    pod["metadata"]!["annotations"] = new JsonObject { ["kubernetes.io/config.mirror"] = "mirror" };

    return pod;
  }

  private static JsonObject Terminating(string name) {
    var pod = Pod(name, "apps", "node-a", "Running", "ReplicaSet", []);
    pod["metadata"]!["deletionTimestamp"] = "2026-08-19T10:00:00Z";

    return pod;
  }

  private static JsonObject Unmanaged(string name) =>
    new() {
      ["metadata"] = new JsonObject { ["name"] = name, ["namespace"] = "apps" },
      ["spec"] = new JsonObject { ["nodeName"] = "node-a" },
      ["status"] = new JsonObject { ["phase"] = "Running" }
    };

  private static string Action(DrainNodePlan plan, string name) =>
    plan.Pods.Single(pod => pod.Name == name).Action;

  private static string Reason(DrainNodePlan plan, string name) =>
    plan.Pods.Single(pod => pod.Name == name).Reason;

  private static JsonObject Pod(
    string name,
    string ns,
    string node,
    string phase,
    string ownerKind,
    JsonObject labels) =>
    new() {
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = ns,
        ["labels"] = labels,
        ["ownerReferences"] = new JsonArray {
          new JsonObject { ["kind"] = ownerKind, ["name"] = ownerKind.ToLowerInvariant(), ["controller"] = true }
        }
      },
      ["spec"] = new JsonObject { ["nodeName"] = node },
      ["status"] = new JsonObject { ["phase"] = phase }
    };
}
