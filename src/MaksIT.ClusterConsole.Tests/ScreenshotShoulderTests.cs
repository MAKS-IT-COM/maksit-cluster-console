using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ScreenshotShoulderTests {
  [Theory]
  [InlineData("pods", DetailTab.Chat)]
  [InlineData("pods", DetailTab.Overview)]
  [InlineData("secrets", DetailTab.Chat)]
  public void Skips_chat_and_the_overview_page_shot(string viewId, string tab) {
    Assert.True(ScreenshotShoulder.Skip(viewId, tab));
  }

  [Theory]
  [InlineData("pods", DetailTab.Yaml)]
  [InlineData("pods", DetailTab.Logs)]
  [InlineData("pods", DetailTab.Terminal)]
  [InlineData("nodes", DetailTab.Images)]
  [InlineData("deployments", DetailTab.Pods)]
  [InlineData("deployments", DetailTab.Events)]
  [InlineData("helm-releases", DetailTab.History)]
  [InlineData("helm-releases", DetailTab.Manifest)]
  [InlineData("configmaps", DetailTab.Data)]
  public void Keeps_every_other_visible_tab(string viewId, string tab) {
    Assert.False(ScreenshotShoulder.Skip(viewId, tab));
  }

  [Theory]
  [InlineData("secrets", DetailTab.Data)]
  [InlineData("configmaps", DetailTab.Data)]
  [InlineData("secrets", DetailTab.Yaml)]
  [InlineData("configmaps", DetailTab.Yaml)]
  [InlineData("components", DetailTab.Yaml)]
  [InlineData("helm-releases", DetailTab.Values)]
  [InlineData("helm-releases", DetailTab.Manifest)]
  public void Blurs_secret_material(string viewId, string tab) {
    Assert.True(ScreenshotShoulder.Blur(viewId, tab));
  }

  [Theory]
  [InlineData("pods", DetailTab.Yaml)]
  [InlineData("pods", DetailTab.Logs)]
  [InlineData("pods", DetailTab.Terminal)]
  [InlineData("pods", DetailTab.Overview)]
  [InlineData("nodes", DetailTab.Images)]
  [InlineData("deployments", DetailTab.Events)]
  [InlineData("secrets", DetailTab.Overview)]
  [InlineData("secrets", DetailTab.Events)]
  public void Leaves_other_shoulder_text_readable(string viewId, string tab) {
    Assert.False(ScreenshotShoulder.Blur(viewId, tab));
  }
}
