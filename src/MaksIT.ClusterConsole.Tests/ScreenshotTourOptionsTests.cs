using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ScreenshotTourOptionsTests {
  [Fact]
  public void Missing_flag_is_not_a_tour() {
    var parsed = ScreenshotTourOptions.TryParse(["--other"], out var options, out var error);

    Assert.True(parsed);
    Assert.Null(options);
    Assert.Null(error);
  }

  [Fact]
  public void Parses_directory_context_views_and_settle() {
    var parsed = ScreenshotTourOptions.TryParse(
      ["--screenshots", @"D:\shots", "--context", "staging", "--views", "pods, services", "--settle-ms", "250"],
      out var options,
      out var error);

    Assert.True(parsed);
    Assert.Null(error);
    Assert.NotNull(options);
    Assert.Equal(@"D:\shots", options.Directory);
    Assert.Equal("staging", options.Context);
    Assert.Equal(["pods", "services"], options.Views);
    Assert.False(options.AllFeatures);
    Assert.Equal(250, options.SettleMilliseconds);
  }

  [Fact]
  public void Defaults_views_when_only_the_directory_is_set() {
    var parsed = ScreenshotTourOptions.TryParse(["--screenshots", "out"], out var options, out var error);

    Assert.True(parsed);
    Assert.Null(error);
    Assert.NotNull(options);
    Assert.Equal([ScreenshotTourOptions.WelcomeView], options.Views);
    Assert.True(options.AllFeatures);
    Assert.Equal(600, options.SettleMilliseconds);
  }

  [Theory]
  [InlineData("--screenshots")]
  [InlineData("--screenshots", "--context")]
  public void Rejects_a_missing_directory(params string[] args) {
    var parsed = ScreenshotTourOptions.TryParse(args, out var options, out var error);

    Assert.False(parsed);
    Assert.Null(options);
    Assert.NotNull(error);
  }
}
