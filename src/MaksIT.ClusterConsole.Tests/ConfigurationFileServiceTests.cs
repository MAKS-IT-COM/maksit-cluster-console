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
}
