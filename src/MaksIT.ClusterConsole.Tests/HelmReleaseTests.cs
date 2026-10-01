using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class HelmReleaseTests {
  [Fact]
  public void Read_keeps_revision_user_values_and_manifest() {
    var release = JsonNode.Parse("""
      {
        "name": "vault",
        "namespace": "vault",
        "version": 3,
        "info": {
          "status": "deployed",
          "description": "Upgrade complete",
          "last_deployed": "2026-08-19T12:00:00Z"
        },
        "chart": { "metadata": { "name": "vault", "version": "1.2.3", "appVersion": "2.0.0" } },
        "config": { "replicas": 3 },
        "manifest": "apiVersion: v1\nkind: ConfigMap\n"
      }
      """) as JsonObject;

    var revision = HelmRelease.Read(release!);

    Assert.NotNull(revision);
    Assert.Equal(3, revision.Revision);
    Assert.Equal("vault-1.2.3", revision.Chart);
    Assert.Equal("vault", revision.ChartName);
    Assert.Equal("1.2.3", revision.ChartVersion);
    Assert.Equal("2.0.0", revision.AppVersion);
    Assert.Contains("replicas: 3", revision.ValuesYaml);
    Assert.Contains("kind: ConfigMap", revision.Manifest);
    Assert.Equal("r3 · deployed", revision.Label);
  }

  [Fact]
  public void Read_says_when_the_release_has_no_user_values() {
    var release = new JsonObject {
      ["name"] = "empty",
      ["namespace"] = "default",
      ["version"] = 1,
      ["info"] = new JsonObject { ["status"] = "deployed" },
      ["config"] = new JsonObject()
    };

    var revision = HelmRelease.Read(release);

    Assert.NotNull(revision);
    Assert.Equal("No user-supplied values.\n", revision.ValuesYaml);
  }

  [Fact]
  public void Charts_groups_latest_revisions_by_chart_version() {
    var older = Revision("vault", "security", 1, "vault", "1.0.0", "superseded");
    var current = Revision("vault", "security", 2, "vault", "1.2.3", "deployed");
    var other = Revision("vault", "apps", 4, "vault", "1.2.3", "deployed");
    var cilium = Revision("cilium", "kube-system", 1, "cilium", "1.16.3", "failed");

    var charts = HelmRelease.Charts([older, current, other, cilium]);

    Assert.Equal(2, charts.Count);
    var vault = Assert.Single(charts, chart => chart.Name == "vault");
    Assert.Equal("1.2.3", vault.Version);
    Assert.Equal(2, vault.Releases);
    Assert.Equal("deployed", vault.Status);
    Assert.Equal("apps, security", vault.Namespaces);
    Assert.Contains("security/vault  deployed  r2", vault.ReleaseLines);
    Assert.DoesNotContain("r1", vault.ReleaseLines);
    Assert.Equal("failed", charts.Single(chart => chart.Name == "cilium").Status);
  }

  private static HelmRevision Revision(
    string name,
    string ns,
    int revision,
    string chart,
    string version,
    string status) =>
    new(
      revision,
      name,
      ns,
      status,
      $"{chart}-{version}",
      chart,
      version,
      "1.0.0",
      "",
      null,
      "",
      "");

  [Fact]
  public void Diff_shows_removed_and_added_lines() {
    var diff = HelmRelease.Diff("replicas: 1\n", "replicas: 3\n");

    Assert.Contains("- replicas: 1", diff);
    Assert.Contains("+ replicas: 3", diff);
  }

  [Fact]
  public void Diff_reports_identical_text() {
    Assert.Equal("No differences.", HelmRelease.Diff("a: 1\n", "a: 1\n"));
  }
}
