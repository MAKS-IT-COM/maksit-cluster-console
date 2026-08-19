using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class PodStatusTests {
  [Fact]
  public void Waiting_crashloop_overrides_running_phase() {
    var pod = Pod("""
      {
        "status": {
          "phase": "Running",
          "conditions": [{ "type": "Ready", "status": "False" }],
          "containerStatuses": [{
            "name": "server",
            "ready": false,
            "restartCount": 37,
            "state": { "waiting": { "reason": "CrashLoopBackOff", "message": "back-off restart" } },
            "lastState": { "terminated": { "exitCode": 1, "reason": "Error" } }
          }]
        }
      }
      """);

    Assert.Equal("CrashLoopBackOff", PodStatus.Of(pod));
    Assert.Equal("CrashLoopBackOff", ResourceRow.From(pod, ResourceCatalog.Find("pods")!).Cells["Status"]);
  }

  [Fact]
  public void Running_window_of_a_crash_loop_is_not_reported_as_running() {
    var pod = Pod("""
      {
        "status": {
          "phase": "Running",
          "conditions": [{ "type": "Ready", "status": "False" }],
          "containerStatuses": [{
            "name": "server",
            "ready": false,
            "restartCount": 37,
            "state": { "running": { "startedAt": "2026-08-19T10:00:00Z" } },
            "lastState": { "terminated": { "exitCode": 1, "reason": "Error" } }
          }]
        }
      }
      """);

    Assert.Equal("CrashLoopBackOff", PodStatus.Of(pod));
  }

  [Fact]
  public void Oomkill_reason_wins_over_generic_crash_loop() {
    var pod = Pod("""
      {
        "status": {
          "phase": "Running",
          "conditions": [{ "type": "Ready", "status": "False" }],
          "containerStatuses": [{
            "name": "server",
            "ready": false,
            "restartCount": 4,
            "state": { "running": {} },
            "lastState": { "terminated": { "exitCode": 137, "reason": "OOMKilled" } }
          }]
        }
      }
      """);

    Assert.Equal("OOMKilled", PodStatus.Of(pod));
  }

  [Fact]
  public void Image_pull_and_init_failures_surface_container_reasons() {
    var pull = Pod("""
      {
        "status": {
          "phase": "Pending",
          "containerStatuses": [{
            "name": "server",
            "ready": false,
            "restartCount": 0,
            "state": { "waiting": { "reason": "ImagePullBackOff" } }
          }]
        }
      }
      """);
    var init = Pod("""
      {
        "spec": { "initContainers": [{ "name": "setup" }], "containers": [{ "name": "app" }] },
        "status": {
          "phase": "Pending",
          "initContainerStatuses": [{
            "name": "setup",
            "ready": false,
            "state": { "waiting": { "reason": "CrashLoopBackOff" } }
          }]
        }
      }
      """);

    Assert.Equal("ImagePullBackOff", PodStatus.Of(pull));
    Assert.Equal("Init:CrashLoopBackOff", PodStatus.Of(init));
  }

  [Fact]
  public void Healthy_running_pod_stays_running_even_with_old_restarts() {
    var pod = Pod("""
      {
        "status": {
          "phase": "Running",
          "conditions": [{ "type": "Ready", "status": "True" }],
          "containerStatuses": [{
            "name": "server",
            "ready": true,
            "restartCount": 3,
            "state": { "running": {} },
            "lastState": { "terminated": { "exitCode": 1, "reason": "Error" } }
          }]
        }
      }
      """);

    Assert.Equal("Running", PodStatus.Of(pod));
  }

  [Fact]
  public void Ready_running_containers_default_to_running_when_phase_is_missing() {
    var pod = Pod("""
      {
        "status": {
          "containerStatuses": [{
            "name": "server",
            "ready": true,
            "restartCount": 0,
            "state": { "running": {} }
          }]
        }
      }
      """);

    Assert.Equal("Running", PodStatus.Of(pod));
  }

  [Fact]
  public void Deleting_pod_is_terminating() {
    var pod = Pod("""
      {
        "metadata": { "name": "x", "deletionTimestamp": "2026-08-19T10:00:00Z" },
        "status": { "phase": "Running" }
      }
      """);

    Assert.Equal("Terminating", PodStatus.Of(pod));
  }

  private static JsonObject Pod(string json) {
    var pod = JsonNode.Parse(json) as JsonObject;
    Assert.NotNull(pod);
    pod["kind"] ??= "Pod";
    pod["apiVersion"] ??= "v1";
    pod["metadata"] ??= new JsonObject { ["name"] = "pod" };
    return pod;
  }
}
