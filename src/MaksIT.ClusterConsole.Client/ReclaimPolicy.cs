using System.Text.Json.Nodes;


namespace MaksIT.ClusterConsole.Client;

public static class ReclaimPolicy {
  public const string Delete = "Delete";

  public const string Retain = "Retain";

  public static IReadOnlyList<string> Choices { get; } = [Delete, Retain];

  public static string? Normalize(string? policy) {
    if (string.IsNullOrWhiteSpace(policy))
      return null;
    if (policy.Trim().Equals(Delete, StringComparison.OrdinalIgnoreCase))
      return Delete;
    if (policy.Trim().Equals(Retain, StringComparison.OrdinalIgnoreCase))
      return Retain;
    return null;
  }

  public static bool Same(string? left, string? right) {
    var a = Normalize(left);
    var b = Normalize(right);
    return a is not null && a == b;
  }

  public const string DefaultClassAnnotation = "storageclass.kubernetes.io/is-default-class";

  public const string BetaDefaultClassAnnotation = "storageclass.beta.kubernetes.io/is-default-class";

  public static string ClassPolicy(JsonObject storageClass) {
    var text = Text(storageClass["reclaimPolicy"]).Trim();
    return text.Length == 0 ? Delete : text;
  }

  public static bool ClassUsesDelete(JsonObject storageClass) =>
    ClassPolicy(storageClass).Equals(Delete, StringComparison.OrdinalIgnoreCase);

  public static bool IsDefaultClass(JsonObject storageClass) {
    var annotations = storageClass["metadata"]?["annotations"] as JsonObject;
    return IsTrue(annotations, DefaultClassAnnotation) || IsTrue(annotations, BetaDefaultClassAnnotation);
  }

  public static string VolumePolicy(JsonObject volume) {
    var text = Text(volume["spec"]?["persistentVolumeReclaimPolicy"]).Trim();
    return text.Length == 0 ? Retain : text;
  }

  public static string Name(JsonObject document) =>
    Text(document["metadata"]?["name"]).Trim();

  public static string VolumeStorageClass(JsonObject volume) {
    var spec = Text(volume["spec"]?["storageClassName"]).Trim();
    if (spec.Length > 0)
      return spec;

    var annotations = volume["metadata"]?["annotations"] as JsonObject;
    var current = Text(annotations?["volume.kubernetes.io/storage-class"]).Trim();
    if (current.Length > 0)
      return current;

    return Text(annotations?["volume.beta.kubernetes.io/storage-class"]).Trim();
  }

  public static StorageReclaimPreview PreviewClass(JsonObject storageClass, IEnumerable<JsonObject> volumes, string target = Retain) {
    var name = Name(storageClass);
    var policy = Normalize(target) ?? Retain;
    var rows = volumes
      .Where(volume => string.Equals(VolumeStorageClass(volume), name, StringComparison.Ordinal))
      .Select(volume => ToVolume(volume, !Same(VolumePolicy(volume), policy)))
      .OrderBy(volume => volume.Name, StringComparer.Ordinal)
      .ToList();
    var classPolicy = ClassPolicy(storageClass);
    return new StorageReclaimPreview(name, classPolicy, !Same(classPolicy, policy), IsDefaultClass(storageClass), rows);
  }

  public static StorageReclaimPreview PreviewVolumes(IEnumerable<JsonObject> volumes, IReadOnlyList<string> names, string target = Retain) {
    var byName = new Dictionary<string, JsonObject>(StringComparer.Ordinal);
    foreach (var volume in volumes) {
      var name = Name(volume);
      if (name.Length > 0)
        byName[name] = volume;
    }

    var policy = Normalize(target) ?? Retain;
    var rows = new List<ReclaimVolume>();
    foreach (var name in names.Select(item => item.Trim()).Where(item => item.Length > 0).Distinct(StringComparer.Ordinal).OrderBy(item => item, StringComparer.Ordinal)) {
      if (!byName.TryGetValue(name, out var volume)) {
        rows.Add(new ReclaimVolume(name, "Missing", "", "", false));
        continue;
      }

      rows.Add(ToVolume(volume, !Same(VolumePolicy(volume), policy)));
    }

    return new StorageReclaimPreview(null, "", false, false, rows);
  }

  public static IReadOnlyList<JsonObject> VolumesForClass(IEnumerable<JsonObject> volumes, string storageClassName, string policy) =>
    volumes
      .Where(volume => string.Equals(VolumeStorageClass(volume), storageClassName, StringComparison.Ordinal))
      .Where(volume => !Same(VolumePolicy(volume), policy))
      .OrderBy(volume => Name(volume), StringComparer.Ordinal)
      .ToList();

  public static IReadOnlyList<JsonObject> VolumesToUpdate(IEnumerable<JsonObject> volumes, IReadOnlyList<string> names, string policy) {
    var wanted = names
      .Select(item => item.Trim())
      .Where(item => item.Length > 0)
      .ToHashSet(StringComparer.Ordinal);
    return volumes
      .Where(volume => wanted.Contains(Name(volume)))
      .Where(volume => !Same(VolumePolicy(volume), policy))
      .OrderBy(volume => Name(volume), StringComparer.Ordinal)
      .ToList();
  }

  public static JsonObject Replacement(JsonObject storageClass, string policy = Retain) {
    var clone = JsonNode.Parse(storageClass.ToJsonString()) as JsonObject ?? [];
    clone.Remove("status");
    if (string.IsNullOrWhiteSpace(Text(clone["apiVersion"])))
      clone["apiVersion"] = "storage.k8s.io/v1";
    if (string.IsNullOrWhiteSpace(Text(clone["kind"])))
      clone["kind"] = "StorageClass";

    clone["reclaimPolicy"] = Normalize(policy) ?? Retain;
    if (clone["metadata"] is not JsonObject meta) {
      meta = new JsonObject();
      clone["metadata"] = meta;
    }

    foreach (var field in new[] {
      "resourceVersion", "uid", "generation", "creationTimestamp", "deletionTimestamp",
      "deletionGracePeriodSeconds", "selfLink", "managedFields", "finalizers", "ownerReferences", "namespace"
    })
      meta.Remove(field);

    if (meta["annotations"] is JsonObject annotations) {
      annotations.Remove("kubectl.kubernetes.io/last-applied-configuration");
      if (annotations.Count == 0)
        meta.Remove("annotations");
    }

    return clone;
  }

  private static ReclaimVolume ToVolume(JsonObject volume, bool willChange) =>
    new(Name(volume), Phase(volume), VolumePolicy(volume), Claim(volume), willChange);

  private static string Phase(JsonObject volume) {
    var phase = Text(volume["status"]?["phase"]).Trim();
    return phase.Length == 0 ? "Unknown" : phase;
  }

  private static string Claim(JsonObject volume) {
    var claim = volume["spec"]?["claimRef"] as JsonObject;
    var name = Text(claim?["name"]).Trim();
    if (name.Length == 0)
      return "";

    var ns = Text(claim?["namespace"]).Trim();
    return ns.Length == 0 ? name : $"{ns}/{name}";
  }

  private static bool IsTrue(JsonObject? annotations, string key) =>
    Text(annotations?[key]).Trim().Equals("true", StringComparison.OrdinalIgnoreCase);

  private static string Text(JsonNode? node) {
    if (node is JsonValue value)
      return value.TryGetValue<string>(out var text) ? text ?? "" : value.ToString() ?? "";

    return node?.ToString() ?? "";
  }
}

public sealed record ReclaimVolume(string Name, string Phase, string Policy, string Claim, bool WillChange) {
  public string Detail {
    get {
      if (Phase == "Missing")
        return "Not found";

      var claim = string.IsNullOrEmpty(Claim) ? "" : "  " + Claim;
      var note = WillChange ? "" : "  (already " + Policy + ")";
      return $"{Phase}  {Policy}{claim}{note}";
    }
  }
}

public sealed record StorageReclaimPreview(
  string? StorageClassName,
  string ClassPolicy,
  bool ClassNeedsChange,
  bool IsDefaultClass,
  IReadOnlyList<ReclaimVolume> Volumes);

public sealed record StorageReclaimOutcome(
  int VolumesPatched,
  string Policy,
  bool ClassRecreated,
  bool ClassUnchanged,
  JsonObject? RecoveryDocument,
  IReadOnlyList<string> Errors) {
  public string Summary {
    get {
      var parts = new List<string>();
      if (VolumesPatched == 1)
        parts.Add($"Set {Policy} on 1 volume.");
      else if (VolumesPatched > 1)
        parts.Add($"Set {Policy} on {VolumesPatched} volumes.");
      if (ClassRecreated)
        parts.Add($"Storage class recreated with {Policy}.");
      else if (ClassUnchanged)
        parts.Add($"Storage class already uses {Policy}.");
      if (parts.Count == 0 && Errors.Count == 0)
        parts.Add("Nothing to change.");
      parts.AddRange(Errors);
      return string.Join(" ", parts);
    }
  }
}
