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
