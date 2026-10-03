using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class NodeSchedulingTests {
  [Fact]
  public void Single_ready_node_cannot_be_taken_offline() {
    var nodes = new[] { Node("only", ready: true) };

    Assert.False(NodeScheduling.LeavesAvailableNode(nodes, ["only"]));
  }

  [Fact]
  public void One_of_two_ready_nodes_can_be_taken_offline() {
    var nodes = new[] { Node("a", ready: true), Node("b", ready: true) };

    Assert.True(NodeScheduling.LeavesAvailableNode(nodes, ["a"]));
    Assert.False(NodeScheduling.LeavesAvailableNode(nodes, ["a", "b"]));
  }

  [Fact]
  public void NotReady_or_cordoned_nodes_do_not_count_as_available() {
    var nodes = new[] {
      Node("ready", ready: true),
      Node("down", ready: false),
      Node("cordoned", ready: true, unschedulable: true)
    };

    Assert.True(NodeScheduling.IsAvailable(nodes[0]));
    Assert.False(NodeScheduling.IsAvailable(nodes[1]));
    Assert.False(NodeScheduling.IsAvailable(nodes[2]));
    Assert.False(NodeScheduling.LeavesAvailableNode(nodes, ["ready"]));
    Assert.True(NodeScheduling.LeavesAvailableNode(nodes, ["cordoned"]));
  }

  private static JsonObject Node(string name, bool ready, bool unschedulable = false) {
    var spec = new JsonObject();

    if (unschedulable)
      spec["unschedulable"] = true;

    return new JsonObject {
      ["metadata"] = new JsonObject { ["name"] = name },
      ["spec"] = spec,
      ["status"] = new JsonObject {
        ["conditions"] = new JsonArray {
          new JsonObject { ["type"] = "Ready", ["status"] = ready ? "True" : "False" }
        }
      }
    };
  }
}
