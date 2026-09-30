using System.Text.Json.Nodes;
using MaksIT.ClusterConsole.Client.Cluster;


namespace MaksIT.ClusterConsole.Shared;

public sealed record NodeCachedImage(
  string State,
  string Image,
  string OtherNames,
  string Size,
  string Pods,
  long SizeBytes);

public sealed record NodeImageReport(
  IReadOnlyList<NodeCachedImage> Images,
  string Caption);

public static class NodeImages {
  public const int StatusLimit = 50;

  public const string Used = "Used";

  public const string Unused = "Unused";

  public static string PodsOnNodeSelector(string nodeName) {
    var escaped = nodeName
      .Replace("\\", "\\\\", StringComparison.Ordinal)
      .Replace(",", "\\,", StringComparison.Ordinal)
      .Replace("=", "\\=", StringComparison.Ordinal);
    return "spec.nodeName=" + escaped;
  }

  public static NodeImageReport Classify(
    JsonObject? node,
    IReadOnlyList<JsonObject> pods,
    bool podsKnown,
    string? podsError = null) {
    var cached = ReadCached(node);
    var uses = podsKnown ? PodUses(pods) : [];
    var rows = new List<NodeCachedImage>(cached.Count);
    foreach (var image in cached) {
      var podsUsing = uses
        .Where(use => image.Refs.Any(cachedRef => use.Refs.Any(podRef => Matches(cachedRef, podRef))))
        .Select(use => use.Label)
        .Distinct(StringComparer.Ordinal)
        .Order(StringComparer.Ordinal)
        .ToList();
      string state;
      if (!podsKnown)
        state = "—";
      else if (podsUsing.Count > 0)
        state = Used;
      else
        state = Unused;
      rows.Add(new NodeCachedImage(
        state,
        image.Display,
        image.OtherNames,
        KubeQuantity.FormatBytesCompact(image.SizeBytes),
        podsUsing.Count == 0 ? "" : string.Join(", ", podsUsing),
        image.SizeBytes));
    }

    rows.Sort(static (left, right) => {
      var rank = Rank(left.State).CompareTo(Rank(right.State));
      if (rank != 0)
        return rank;
      var size = right.SizeBytes.CompareTo(left.SizeBytes);
      return size != 0 ? size : string.Compare(left.Image, right.Image, StringComparison.OrdinalIgnoreCase);
    });

    var usedRows = rows.Where(row => row.State == Used).ToList();
    var unusedRows = rows.Where(row => row.State == Unused).ToList();
    return new NodeImageReport(
      rows,
      Caption(
        usedRows.Count,
        usedRows.Sum(row => row.SizeBytes),
        unusedRows.Count,
        unusedRows.Sum(row => row.SizeBytes),
        rows.Sum(row => row.SizeBytes),
        cached.Count >= StatusLimit,
        podsKnown,
        podsError,
        cached.Count));
  }

  private static int Rank(string state) =>
    state switch {
      Unused => 0,
      Used => 1,
      _ => 2
    };

  private static string Caption(
    int used,
    long usedBytes,
    int unused,
    long unusedBytes,
    long bytes,
    bool truncated,
    bool podsKnown,
    string? podsError,
    int imageCount) {
    if (!podsKnown) {
      var listed = imageCount == 0
        ? "No cached images reported"
        : $"{imageCount} cached · {KubeQuantity.FormatBytesCompact(bytes)}";
      var why = string.IsNullOrWhiteSpace(podsError) ? "pods could not be listed" : podsError;
      return listed + " · " + why;
    }

    if (imageCount == 0)
      return "No cached images reported on this node.";

    var text = $"{used} used · {KubeQuantity.FormatBytesCompact(usedBytes)} · {unused} unused · {KubeQuantity.FormatBytesCompact(unusedBytes)}";
    if (truncated)
      text += " · kubelet lists at most 50 images, largest first";
    return text;
  }

  private static List<CachedImage> ReadCached(JsonObject? node) {
    var images = node?["status"]?["images"] as JsonArray;
    if (images is null)
      return [];

    var cached = new List<CachedImage>();
    foreach (var item in images.OfType<JsonObject>()) {
      var names = ReadNames(item);
      if (names.Count == 0)
        continue;

      var display = DisplayName(names);
      var others = names.Where(name => !string.Equals(name, display, StringComparison.Ordinal)).ToList();
      cached.Add(new CachedImage(
        display,
        string.Join(", ", others),
        ReadBytes(item["sizeBytes"]),
        names.Select(Parse).ToList()));
    }

    return cached;
  }

  private static List<PodUse> PodUses(IReadOnlyList<JsonObject> pods) {
    var uses = new List<PodUse>();
    foreach (var pod in pods) {
      var meta = pod["metadata"] as JsonObject;
      var name = ReadString(meta?["name"]);
      if (string.IsNullOrEmpty(name))
        continue;

      var ns = ReadString(meta?["namespace"]);
      var label = string.IsNullOrEmpty(ns) ? name : ns + "/" + name;
      var refs = new List<ImageRef>();
      CollectSpec(pod["spec"] as JsonObject, refs);
      CollectStatus(pod["status"] as JsonObject, refs);
      if (refs.Count > 0)
        uses.Add(new PodUse(label, refs));
    }

    return uses;
  }

  private static void CollectSpec(JsonObject? spec, List<ImageRef> refs) {
    if (spec is null)
      return;

    foreach (var field in new[] { "containers", "initContainers", "ephemeralContainers" })
      CollectImages(spec[field] as JsonArray, refs, "image");
  }

  private static void CollectStatus(JsonObject? status, List<ImageRef> refs) {
    if (status is null)
      return;

    foreach (var field in new[] { "containerStatuses", "initContainerStatuses", "ephemeralContainerStatuses" }) {
      var items = status[field] as JsonArray;
      CollectImages(items, refs, "image");
      CollectImages(items, refs, "imageID");
    }
  }

  private static void CollectImages(JsonArray? items, List<ImageRef> refs, string field) {
    if (items is null)
      return;

    foreach (var item in items.OfType<JsonObject>()) {
      var raw = ReadString(item[field]);
      if (!string.IsNullOrWhiteSpace(raw))
        refs.Add(Parse(raw));
    }
  }

  private static bool Matches(ImageRef left, ImageRef right) {
    if (left.Digest is not null && right.Digest is not null)
      return string.Equals(left.Digest, right.Digest, StringComparison.Ordinal);

    if (left.Repository.Length == 0 || right.Repository.Length == 0)
      return false;

    if (!string.Equals(left.Repository, right.Repository, StringComparison.Ordinal))
      return false;

    return string.Equals(TagOrLatest(left.Tag), TagOrLatest(right.Tag), StringComparison.OrdinalIgnoreCase);
  }

  private static string TagOrLatest(string? tag) =>
    string.IsNullOrEmpty(tag) ? "latest" : tag;

  private static ImageRef Parse(string raw) {
    var value = raw.Trim();
    foreach (var prefix in new[] { "docker-pullable://", "containerd://", "cri-o://" }) {
      if (value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        value = value[prefix.Length..];
    }

    if (IsDigest(value))
      return new ImageRef("", null, value.ToLowerInvariant());

    string? digest = null;
    var at = value.LastIndexOf('@');
    if (at >= 0) {
      var tail = value[(at + 1)..];
      if (IsDigest(tail))
        digest = tail.ToLowerInvariant();
      value = value[..at];
    }

    string? tag = null;
    var colon = value.LastIndexOf(':');
    if (colon > 0 && !value[(colon + 1)..].Contains('/')) {
      tag = value[(colon + 1)..];
      value = value[..colon];
    }

    return new ImageRef(NormalizeRepository(value), tag, digest);
  }

  private static bool IsDigest(string value) {
    var colon = value.IndexOf(':');
    if (colon <= 0)
      return false;

    var algo = value[..colon];
    if (!string.Equals(algo, "sha256", StringComparison.OrdinalIgnoreCase)
        && !string.Equals(algo, "sha512", StringComparison.OrdinalIgnoreCase))
      return false;

    var hex = value[(colon + 1)..];
    return hex.Length >= 32 && hex.All(static c => c is >= '0' and <= '9' or >= 'a' and <= 'f' or >= 'A' and <= 'F');
  }

  private static string NormalizeRepository(string name) {
    name = name.ToLowerInvariant();
    if (name.StartsWith("index.docker.io/", StringComparison.Ordinal))
      name = name["index.docker.io/".Length..];
    else if (name.StartsWith("docker.io/", StringComparison.Ordinal))
      name = name["docker.io/".Length..];

    if (name.StartsWith("library/", StringComparison.Ordinal) && !name["library/".Length..].Contains('/'))
      name = name["library/".Length..];

    return name;
  }

  private static string DisplayName(IReadOnlyList<string> names) {
    foreach (var name in names) {
      var parsed = Parse(name);
      if (parsed.Repository.Length > 0 && parsed.Tag is not null && parsed.Digest is null)
        return name;
    }

    return names[0];
  }

  private static List<string> ReadNames(JsonObject image) {
    var names = image["names"] as JsonArray;
    if (names is null)
      return [];

    return names
      .Select(ReadString)
      .Where(name => !string.IsNullOrWhiteSpace(name))
      .Select(name => name!.Trim())
      .ToList();
  }

  private static string? ReadString(JsonNode? node) =>
    node is JsonValue value && value.TryGetValue<string>(out var text) ? text : null;

  private static long ReadBytes(JsonNode? node) {
    if (node is not JsonValue value)
      return 0;
    if (value.TryGetValue<long>(out var n))
      return n;
    if (value.TryGetValue<int>(out var i))
      return i;
    if (value.TryGetValue<double>(out var d))
      return (long)d;
    return 0;
  }

  private readonly record struct ImageRef(string Repository, string? Tag, string? Digest);

  private sealed record CachedImage(
    string Display,
    string OtherNames,
    long SizeBytes,
    IReadOnlyList<ImageRef> Refs);

  private sealed record PodUse(string Label, IReadOnlyList<ImageRef> Refs);
}
