using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Shared.Chat;

public static class DrainAdvice {
  public static string SystemPrompt() =>
    $"""
    You are the SRE assistant inside {AppInfo.ProductName}.
    The operator is about to drain a Kubernetes node. The plan below is already computed. Do not invent pods, nodes, or budgets.
    Write a short note in plain sentences.
    Say whether it is safe to continue, and why.
    Name the pods expected to remain, and why each one stays.
    Name the pods that will move.
    A pod that remains is not deleted. A disruption budget that would refuse eviction is a reason to be careful.
    Do not tell the operator which button to press. Do not claim the drain has already run.
    """;

  public static string UserPrompt(string? plan) {
    var text = string.IsNullOrWhiteSpace(plan) ? "No drain plan was provided." : plan.Trim();
    const int max = 6000;

    if (text.Length <= max)
      return text;

    const int half = 2800;

    return text[..half] + "\n…\n" + text[^half..];
  }
}
