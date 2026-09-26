using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class MarkupSyntaxTests {
  [Fact]
  public void Uses_key_or_file_extension() {
    Assert.Equal(MarkupSyntax.Json, MarkupSyntaxDetector.FromFileName("app.json"));
    Assert.Equal(MarkupSyntax.Json, MarkupSyntaxDetector.FromFileName("config/app.JSON"));
    Assert.Equal(MarkupSyntax.Yaml, MarkupSyntaxDetector.FromFileName("values.yaml"));
    Assert.Equal(MarkupSyntax.Yaml, MarkupSyntaxDetector.FromFileName("chart.yml"));
  }

  [Fact]
  public void Keys_without_a_markup_extension_stay_plain() {
    Assert.Equal(MarkupSyntax.None, MarkupSyntaxDetector.FromFileName("password"));
    Assert.Equal(MarkupSyntax.None, MarkupSyntaxDetector.FromFileName("config.txt"));
    Assert.Equal(MarkupSyntax.None, MarkupSyntaxDetector.FromFileName(null));
    Assert.Equal(MarkupSyntax.None, MarkupSyntaxDetector.Detect("{\n  \"a\": 1\n}"));
  }

  [Fact]
  public void Resource_yaml_tab_is_always_yaml() {
    Assert.Equal(MarkupSyntax.Yaml, MarkupSyntaxDetector.Detect(null, preferYaml: true));
    Assert.Equal(MarkupSyntax.Yaml, MarkupSyntaxDetector.Detect("notes.txt", preferYaml: true));
  }
}
