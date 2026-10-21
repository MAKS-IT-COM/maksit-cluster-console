using MaksIT.ClusterConsole.Client.KubeConfig;


namespace MaksIT.ClusterConsole.Tests;

public class CloudKubeconfigTests {
  [Fact]
  public void Google_azure_and_amazon_write_a_full_context() {
    var path = Path.Combine(Path.GetTempPath(), "kubeconfig");
    var google = CloudKubeconfig.Build(new CloudFetchRequest("google", "prod", "europe-west1", "demo", path));
    Assert.Equal("gcloud", google.Value!.FileName);
    Assert.Equal(
      ["container", "clusters", "get-credentials", "prod", "--location", "europe-west1", "--project", "demo"],
      google.Value.Arguments);

    var azure = CloudKubeconfig.Build(new CloudFetchRequest("azure", "prod", null, "rg", path));
    Assert.Equal("az", azure.Value!.FileName);
    Assert.Contains("--resource-group", azure.Value.Arguments);
    Assert.Contains("rg", azure.Value.Arguments);
    Assert.Contains(path, azure.Value.Arguments);

    var amazon = CloudKubeconfig.Build(new CloudFetchRequest("amazon", "prod", "eu-central-1", null, path));
    Assert.Equal("aws", amazon.Value!.FileName);
    Assert.Equal(
      ["eks", "update-kubeconfig", "--name", "prod", "--region", "eu-central-1", "--kubeconfig", path],
      amazon.Value.Arguments);
  }

  [Fact]
  public void Oracle_digitalocean_and_ibm_are_included() {
    var path = Path.Combine(Path.GetTempPath(), "kubeconfig");
    var oracle = CloudKubeconfig.Build(new CloudFetchRequest("oracle", "ocid1.cluster.oc1.example", "eu-frankfurt-1", null, path));
    Assert.Equal("oci", oracle.Value!.FileName);
    Assert.Contains("2.0.0", oracle.Value.Arguments);
    Assert.Contains(path, oracle.Value.Arguments);

    var ocean = CloudKubeconfig.Build(new CloudFetchRequest("digitalocean", "cluster-id", null, null, path));
    Assert.Equal(["kubernetes", "cluster", "kubeconfig", "save", "cluster-id"], ocean.Value!.Arguments);

    var ibm = CloudKubeconfig.Build(new CloudFetchRequest("ibm", "prod", null, null, path));
    Assert.Equal("ibmcloud", ibm.Value!.FileName);
    Assert.Equal(["ks", "cluster", "config", "--cluster", "prod"], ibm.Value.Arguments);
    Assert.True(CloudExecPreset.Ibm.FetchOnly);
  }

  [Fact]
  public void A_provider_without_a_cli_is_rejected() {
    var built = CloudKubeconfig.Build(new CloudFetchRequest("custom", "prod", null, null, "kubeconfig"));

    Assert.False(built.IsSuccess);
  }
}
