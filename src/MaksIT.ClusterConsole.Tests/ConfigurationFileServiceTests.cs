using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ConfigurationFileServiceTests {
  [Fact]
  public void Default_selected_namespace_is_all() {
    Assert.Equal(Configuration.AllNamespaces, new Configuration().SelectedNamespace);
  }

  [Fact]
  public void NamespaceFor_uses_per_context_then_legacy_selected() {
    var cfg = new Configuration { SelectedNamespace = "default" };
    Assert.Equal("default", cfg.NamespaceFor("missing"));

    cfg.SetNamespace("prod", "kube-system");
    Assert.Equal("kube-system", cfg.NamespaceFor("prod"));
    Assert.Equal("kube-system", cfg.SelectedNamespace);
    Assert.Equal("prod", cfg.ActiveContext);
    Assert.Equal(Configuration.AllNamespaces, cfg.NamespaceFor("dev"));
  }

  [Fact]
  public void Save_round_trips_selected_namespace_and_keeps_logging() {
    var path = Path.Combine(Path.GetTempPath(), $"maksit-cluster-console-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, """
      {
        "Logging": { "LogLevel": { "Default": "Information" } },
        "Configuration": { "SelectedNamespace": "all" }
      }
      """);

    try {
      var service = new ConfigurationFileService(path);
      Assert.Equal("all", service.Current.SelectedNamespace);

      service.Save(new Configuration { SelectedNamespace = "kube-system" });

      var reloaded = new ConfigurationFileService(path);
      Assert.Equal("kube-system", reloaded.Current.SelectedNamespace);
      Assert.Contains("\"Logging\"", File.ReadAllText(path), StringComparison.Ordinal);
    }
    finally {
      File.Delete(path);
    }
  }

  [Fact]
  public void Save_keeps_open_contexts_when_updating_namespace() {
    var path = Path.Combine(Path.GetTempPath(), $"maksit-cluster-console-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, """
      {
        "Logging": { "LogLevel": { "Default": "Information" } },
        "Configuration": {
          "SelectedNamespace": "all",
          "ActiveContext": "prod",
          "OpenContexts": [ "prod", "dev" ]
        }
      }
      """);

    try {
      var service = new ConfigurationFileService(path);
      var cfg = service.Current;
      Assert.Equal(["prod", "dev"], cfg.OpenContexts);
      cfg.SetNamespace("prod", "kube-system");
      service.Save(cfg);

      var reloaded = new ConfigurationFileService(path);
      Assert.Equal(["prod", "dev"], reloaded.Current.OpenContexts);
      Assert.Equal("prod", reloaded.Current.ActiveContext);
      Assert.Equal("kube-system", reloaded.Current.NamespaceFor("prod"));
      Assert.Contains("\"Logging\"", File.ReadAllText(path), StringComparison.Ordinal);
    }
    finally {
      File.Delete(path);
    }
  }

  [Fact]
  public void IsNavigatorExpanded_defaults_to_collapsed() {
    var cfg = new Configuration();
    Assert.False(cfg.IsNavigatorExpanded("Workloads"));
    Assert.False(cfg.IsNavigatorExpanded("Custom Resources/cilium.io"));
  }

  [Fact]
  public void Save_round_trips_navigator_expanded() {
    var path = Path.Combine(Path.GetTempPath(), $"maksit-cluster-console-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, """
      {
        "Logging": { "LogLevel": { "Default": "Information" } },
        "Configuration": { "SelectedNamespace": "all" }
      }
      """);

    try {
      var service = new ConfigurationFileService(path);
      var cfg = service.Current;
      cfg.SetNavigatorExpanded(new Dictionary<string, bool> {
        ["Workloads"] = true,
        ["Config"] = false,
        ["Custom Resources/cilium.io"] = true
      });
      service.Save(cfg);

      var json = File.ReadAllText(path);
      Assert.Contains("\"NavigatorExpanded\"", json, StringComparison.Ordinal);
      Assert.Contains("\"Workloads\"", json, StringComparison.Ordinal);

      var reloaded = new ConfigurationFileService(path);
      Assert.True(reloaded.Current.IsNavigatorExpanded("Workloads"));
      Assert.False(reloaded.Current.IsNavigatorExpanded("Config"));
      Assert.True(reloaded.Current.IsNavigatorExpanded("Custom Resources/cilium.io"));
      Assert.False(reloaded.Current.IsNavigatorExpanded("Storage"));
      Assert.Equal("all", reloaded.Current.SelectedNamespace);
    }
    finally {
      File.Delete(path);
    }
  }

  [Fact]
  public void Save_round_trips_layout_without_dropping_open_contexts() {
    var path = Path.Combine(Path.GetTempPath(), $"maksit-cluster-console-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, """
      {
        "Logging": { "LogLevel": { "Default": "Information" } },
        "Configuration": {
          "SelectedNamespace": "all",
          "OpenContexts": [ "prod" ]
        }
      }
      """);

    try {
      var service = new ConfigurationFileService(path);
      var cfg = service.Current;
      cfg.Layout.WindowWidth = 1600;
      cfg.Layout.WindowHeight = 900;
      cfg.Layout.WindowX = 40;
      cfg.Layout.WindowY = 80;
      cfg.Layout.WindowState = "Maximized";
      cfg.Layout.CatalogWidth = 260;
      cfg.Layout.NavigatorWidth = 200;
      cfg.Layout.DetailsWidth = 420;
      cfg.Layout.SetColumns("resources/pods", new Dictionary<string, double> {
        ["Name"] = 220,
        ["Namespace"] = 140
      });
      service.Save(cfg);

      var reloaded = new ConfigurationFileService(path);
      Assert.Equal(["prod"], reloaded.Current.OpenContexts);
      Assert.Equal(1600, reloaded.Current.Layout.WindowWidth);
      Assert.Equal(900, reloaded.Current.Layout.WindowHeight);
      Assert.Equal(40, reloaded.Current.Layout.WindowX);
      Assert.Equal(80, reloaded.Current.Layout.WindowY);
      Assert.Equal("Maximized", reloaded.Current.Layout.WindowState);
      Assert.Equal(260, reloaded.Current.Layout.CatalogWidth);
      Assert.Equal(200, reloaded.Current.Layout.NavigatorWidth);
      Assert.Equal(420, reloaded.Current.Layout.DetailsWidth);
      var columns = reloaded.Current.Layout.ColumnsFor("resources/pods");
      Assert.NotNull(columns);
      Assert.Equal(220, columns["Name"]);
      Assert.Equal(140, columns["Namespace"]);
      Assert.Contains("\"Logging\"", File.ReadAllText(path), StringComparison.Ordinal);
    }
    finally {
      File.Delete(path);
    }
  }

  [Fact]
  public void Save_round_trips_port_forwards() {
    var path = Path.Combine(Path.GetTempPath(), $"maksit-cluster-console-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, """
      {
        "Logging": { "LogLevel": { "Default": "Information" } },
        "Configuration": { "SelectedNamespace": "all" }
      }
      """);

    try {
      var service = new ConfigurationFileService(path);
      var cfg = service.Current;
      cfg.UpsertPortForward(new PersistedPortForward {
        Context = "homelab",
        Kind = "Service",
        Name = "postgres",
        Namespace = "postgresql",
        PodName = "postgres-0",
        LocalPort = 5432,
        RemotePort = 5432
      });
      cfg.UpsertPortForward(new PersistedPortForward {
        Context = "homelab",
        Kind = "Service",
        Name = "postgres",
        Namespace = "postgresql",
        PodName = "postgres-1",
        LocalPort = 5432,
        RemotePort = 5432
      });
      cfg.UpsertPortForward(new PersistedPortForward {
        Context = "dev",
        Kind = "Pod",
        Name = "web",
        Namespace = "apps",
        PodName = "web",
        LocalPort = 8080,
        RemotePort = 80,
        MatchLabels = new Dictionary<string, string> { ["app"] = "web" }
      });
      service.Save(cfg);

      var reloaded = new ConfigurationFileService(path);
      var homelab = reloaded.Current.PortForwardsFor("homelab");
      Assert.Single(homelab);
      Assert.Equal("postgres-1", homelab[0].PodName);
      Assert.Equal(5432, homelab[0].LocalPort);
      Assert.Equal("Service", homelab[0].Kind);

      reloaded.Current.RemovePortForward("homelab", 5432);
      reloaded.Save(reloaded.Current);
      Assert.Empty(new ConfigurationFileService(path).Current.PortForwardsFor("homelab"));
      var dev = new ConfigurationFileService(path).Current.PortForwardsFor("dev");
      Assert.Single(dev);
      Assert.Equal("web", dev[0].MatchLabels!["app"]);
    }
    finally {
      File.Delete(path);
    }
  }

  [Fact]
  public void PortForwardRestoreSummary_formats_success_and_failure() {
    Assert.Equal("Restored 1 port-forward.", new PortForwardRestoreSummary(1, []).Format());
    Assert.Equal("Restored 3 port-forwards.", new PortForwardRestoreSummary(3, []).Format());
    Assert.Equal(
      "Port-forward restore failed: localhost:8080 (pod not found)",
      new PortForwardRestoreSummary(0, ["localhost:8080 (pod not found)"]).Format());
    Assert.Equal(
      "Restored 2 port-forward(s); 1 failed: localhost:80 (address in use)",
      new PortForwardRestoreSummary(2, ["localhost:80 (address in use)"]).Format());
  }

  [Fact]
  public void Default_path_is_appsettings_beside_the_executable() {
    var service = new ConfigurationFileService();
    Assert.Equal(Path.Combine(AppContext.BaseDirectory, "appsettings.json"), service.FilePath);
  }

  [Fact]
  public void Save_round_trips_filters_search_and_selected_nav() {
    var path = Path.Combine(Path.GetTempPath(), $"maksit-cluster-console-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, """
      {
        "Logging": { "LogLevel": { "Default": "Information" } },
        "Configuration": { "SelectedNamespace": "all" }
      }
      """);

    try {
      var service = new ConfigurationFileService(path);
      var cfg = service.Current;
      cfg.Layout.SelectedNavId = "deployments";
      cfg.Layout.SetSearch("pods", "coredns");
      cfg.Layout.SetFilters("resources/pods", new Dictionary<string, SavedColumnFilter> {
        ["Namespace"] = new() {
          Text = "",
          Excluded = ["default", "kube-system"]
        },
        ["Status"] = new() { Text = "Crash" }
      });
      service.Save(cfg);

      var reloaded = new ConfigurationFileService(path);
      Assert.Equal("deployments", reloaded.Current.Layout.SelectedNavId);
      Assert.Equal("coredns", reloaded.Current.Layout.SearchFor("pods"));
      Assert.Equal("", reloaded.Current.Layout.SearchFor("deployments"));
      var ns = reloaded.Current.Layout.FilterFor("resources/pods", "Namespace");
      Assert.NotNull(ns);
      Assert.Equal(["default", "kube-system"], ns.Excluded);
      Assert.Equal("Crash", reloaded.Current.Layout.FilterFor("resources/pods", "Status")?.Text);
    }
    finally {
      File.Delete(path);
    }
  }

  [Fact]
  public void Table_filters_and_sort_are_stored_per_context() {
    var layout = new LayoutSettings();
    layout.SetFilters("resources/pods", new Dictionary<string, SavedColumnFilter> {
      ["Status"] = new() { Text = "legacy" }
    });
    layout.SetFilters("prod", "resources/pods", new Dictionary<string, SavedColumnFilter> {
      ["Status"] = new() { Text = "Crash" }
    });
    layout.SetSort("prod", "resources/pods", new SavedColumnSort {
      Header = "Age",
      Direction = "Descending"
    });
    layout.SetColumns("prod", "resources/pods", new Dictionary<string, double> { ["Name"] = 240 });

    Assert.Equal("Crash", layout.FilterFor("prod", "resources/pods", "Status")?.Text);
    Assert.Equal("legacy", layout.FilterFor("dev", "resources/pods", "Status")?.Text);
    Assert.Equal("Age", layout.SortFor("prod", "resources/pods")?.Header);
    Assert.Equal("Descending", layout.SortFor("prod", "resources/pods")?.Direction);
    Assert.Null(layout.SortFor("dev", "resources/pods"));
    Assert.Equal(240, layout.ColumnsFor("prod", "resources/pods")!["Name"]);
    Assert.Null(layout.ColumnsFor("dev", "resources/pods"));
    Assert.Equal("prod/resources/pods", LayoutSettings.ContextTable("prod", "resources/pods"));
  }

  [Fact]
  public void Save_round_trips_per_context_sort_and_filters() {
    var path = Path.Combine(Path.GetTempPath(), $"maksit-cluster-console-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, """
      {
        "Logging": { "LogLevel": { "Default": "Information" } },
        "Configuration": { "SelectedNamespace": "all" }
      }
      """);

    try {
      var service = new ConfigurationFileService(path);
      var cfg = service.Current;
      cfg.Layout.SetFilters("homelab", "resources/pods", new Dictionary<string, SavedColumnFilter> {
        ["Namespace"] = new() { Excluded = ["default"] }
      });
      cfg.Layout.SetSort("homelab", "resources/pods", new SavedColumnSort {
        Header = "Age",
        Direction = "Descending"
      });
      service.Save(cfg);

      var reloaded = new ConfigurationFileService(path);
      Assert.Equal(["default"], reloaded.Current.Layout.FilterFor("homelab", "resources/pods", "Namespace")?.Excluded);
      Assert.Null(reloaded.Current.Layout.FilterFor("dev", "resources/pods", "Namespace"));
      var sort = reloaded.Current.Layout.SortFor("homelab", "resources/pods");
      Assert.NotNull(sort);
      Assert.Equal("Age", sort.Header);
      Assert.Equal("Descending", sort.Direction);
      Assert.Contains("\"Tables\"", File.ReadAllText(path), StringComparison.Ordinal);
      Assert.DoesNotContain("\"ColumnSorts\"", File.ReadAllText(path), StringComparison.Ordinal);
    }
    finally {
      File.Delete(path);
    }
  }

  [Fact]
  public void Migrates_legacy_column_maps_into_tables() {
    var path = Path.Combine(Path.GetTempPath(), $"maksit-cluster-console-{Guid.NewGuid():N}.json");
    File.WriteAllText(path, """
      {
        "Logging": { "LogLevel": { "Default": "Information" } },
        "Configuration": {
          "Layout": {
            "ColumnWidths": { "resources/pods": { "Name": 220 } },
            "ColumnFilters": {
              "homelab/resources/pods": {
                "Status": { "Text": "Crash", "Excluded": [] }
              }
            },
            "ColumnSorts": {
              "homelab/resources/pods": { "Header": "Age", "Direction": "Descending" }
            },
            "SearchByResource": { "resources/pods": "coredns" }
          }
        }
      }
      """);

    try {
      var service = new ConfigurationFileService(path);
      var layout = service.Current.Layout;
      Assert.Equal(220, layout.ColumnsFor("resources/pods")!["Name"]);
      Assert.Equal("Crash", layout.FilterFor("homelab", "resources/pods", "Status")?.Text);
      Assert.Equal("Age", layout.SortFor("homelab", "resources/pods")?.Header);
      Assert.Equal("coredns", layout.SearchFor("pods"));

      service.Save(service.Current);
      var json = File.ReadAllText(path);
      Assert.Contains("\"Tables\"", json, StringComparison.Ordinal);
      Assert.DoesNotContain("\"ColumnWidths\"", json, StringComparison.Ordinal);
      Assert.DoesNotContain("\"ColumnFilters\"", json, StringComparison.Ordinal);
      Assert.DoesNotContain("\"ColumnSorts\"", json, StringComparison.Ordinal);
      Assert.DoesNotContain("\"SearchByResource\"", json, StringComparison.Ordinal);

      var reloaded = new ConfigurationFileService(path);
      Assert.Equal("Crash", reloaded.Current.Layout.FilterFor("homelab", "resources/pods", "Status")?.Text);
      Assert.Equal("coredns", reloaded.Current.Layout.SearchFor("pods"));
    }
    finally {
      File.Delete(path);
    }
  }
}
