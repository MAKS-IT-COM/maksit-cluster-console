using System.Globalization;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using MaksIT.ClusterConsole.Client.Cluster;



namespace MaksIT.ClusterConsole.Shared;

/// <summary>
/// Longhorn backup rows, backup-job rows, and the cron schedule those jobs store.
/// </summary>
public static class LonghornBackup {
  public const string LabelPrefix = "recurring-job.longhorn.io/";

  public static readonly IReadOnlyList<CronChoice> MinuteChoices = Minutes();

  public static readonly IReadOnlyList<CronChoice> HourChoices = Hours();

  public static readonly IReadOnlyList<CronChoice> DayChoices = Days();

  public static readonly IReadOnlyList<CronChoice> MonthChoices = Months();

  public static readonly IReadOnlyList<CronChoice> WeekdayChoices = Weekdays();

  public static CronFields Daily { get; } = new("0", "2", "*", "*", "*");

  public static CronFields Hourly { get; } = new("0", "*", "*", "*", "*");

  public static CronFields Weekly { get; } = new("0", "2", "*", "*", "0");

  public static string Column(JsonObject? item, string path) =>
    path switch {
      "longhorn.backup.state" => State(item),
      "longhorn.backup.volume" => VolumeName(item),
      "longhorn.backup.last" => JsonPath.Text(item?["status"]?["lastBackupName"]),
      "longhorn.backup.lastAt" => When(item?["status"]?["lastBackupAt"]),
      "longhorn.backup.created" => Created(item),
      "longhorn.backup.size" => Size(item?["status"]?["size"] ?? item?["status"]?["volumeSize"]),
      "longhorn.backup.target" => JsonPath.Text(item?["spec"]?["backupTargetName"]),
      "longhorn.backup.schedule" => Describe(JsonPath.Text(item?["spec"]?["cron"])),
      "longhorn.backup.retain" => JsonPath.Text(item?["spec"]?["retain"]),
      "longhorn.backup.groups" => Groups(item),
      "longhorn.backup.task" => TaskLabel(item),
      "longhorn.backup.runState" => "",
      "longhorn.backup.lastSuccess" => "",
      _ => ""
    };

  public static IReadOnlyDictionary<string, string> Tips(JsonObject? item) {
    var error = Message(item);

    if (error.Length == 0)
      return new Dictionary<string, string>(StringComparer.Ordinal);

    return new Dictionary<string, string>(StringComparer.Ordinal) {
      ["State"] = error
    };
  }

  public static readonly IReadOnlyList<RecurringTask> Tasks = [
    new("backup", "Backup", "Use Backup to copy a volume into the backup target on this schedule. Restore that copy if the disk or the cluster is gone. Retain is how many copies to keep."),
    new("snapshot", "Snapshot", "Use Snapshot for a local point in time on the volume. It is the fast rollback while the volume is still there. It is not stored in the backup target. Retain is how many snapshots to keep."),
    new("snapshot-delete", "Snapshot Delete", "Use Snapshot Delete to remove local snapshots once a volume has more than Retain. It removes user snapshots and system snapshots past that count."),
    new("snapshot-cleanup", "Snapshot Cleanup", "Use Snapshot Cleanup to purge snapshots Longhorn has already marked removable, including snapshots left by the system. Retain is not used."),
    new("filesystem-trim", "Filesystem Trim", "Use Filesystem Trim to tell the filesystem to release blocks that files no longer use, so Longhorn can reclaim that space. It does not take a snapshot or a backup. Retain is not used."),
    new("system-backup", "System Backup", "Use System Backup to copy the Longhorn configuration to the backup target, so Longhorn can be rebuilt on another cluster. It does not copy application pods. Volume data still comes from volume backups. Volume backup policy chooses whether this job also takes those volume backups.")
  ];

  public static readonly IReadOnlyList<BackupPolicy> Policies = [
    new("if-not-present", "If not present", "Take a volume backup only when that volume does not already have one."),
    new("always", "Always", "Take a volume backup every time this job runs."),
    new("disabled", "Disabled", "Copy only the Longhorn configuration. Do not take volume backups.")
  ];

  public const string ForceHint = "Force create still runs when cleaning up the older copies failed.";

  public static bool IsBackupJob(JsonObject item) {
    var task = JsonPath.Text(item["spec"]?["task"]);

    return task is "backup" or "backup-force-create";
  }

  public static string BaseTask(string stored) =>
    stored switch {
      "backup-force-create" => "backup",
      "snapshot-force-create" => "snapshot",
      _ => stored
    };

  public static bool IsForceTask(string stored) =>
    stored is "backup-force-create" or "snapshot-force-create";

  public static string StoredTask(string task, bool force) {
    if (force && task == "backup")
      return "backup-force-create";

    if (force && task == "snapshot")
      return "snapshot-force-create";

    return task;
  }

  public static RecurringTask? FindTask(string id) {
    foreach (var task in Tasks) {
      if (task.Id == id)
        return task;
    }

    return null;
  }

  public static BackupPolicy? FindPolicy(string id) {
    foreach (var policy in Policies) {
      if (policy.Id == id)
        return policy;
    }

    return null;
  }

  public static bool VolumeHasJob(JsonObject volume, string job) {
    var value = JsonPath.Text(volume["metadata"]?["labels"]?[LabelPrefix + job]);

    if (value.Length == 0)
      return false;

    if (value.Equals("disabled", StringComparison.OrdinalIgnoreCase) || value.Equals("false", StringComparison.OrdinalIgnoreCase))
      return false;

    return true;
  }

  public static string LabelPatch(string? clearJob, string? enableJob) {
    var labels = new List<string>();

    if (!string.IsNullOrWhiteSpace(clearJob) && !string.Equals(clearJob, enableJob, StringComparison.Ordinal))
      labels.Add("\"" + LabelPrefix + clearJob + "\":null");

    if (!string.IsNullOrWhiteSpace(enableJob))
      labels.Add("\"" + LabelPrefix + enableJob + "\":\"enabled\"");

    return "{\"metadata\":{\"labels\":{" + string.Join(',', labels) + "}}}";
  }

  public static JsonObject JobDocument(
    string name,
    string ns,
    string cron,
    int retain,
    string task,
    IReadOnlyList<string> groups,
    int concurrency,
    IReadOnlyDictionary<string, string>? labels = null,
    IReadOnlyDictionary<string, string>? parameters = null) {
    var spec = new JsonObject {
      ["name"] = name,
      ["task"] = task,
      ["cron"] = cron,
      ["retain"] = retain,
      ["concurrency"] = concurrency < 1 ? 1 : concurrency
    };

    if (groups.Count > 0) {
      var list = new JsonArray();

      foreach (var group in groups)
        list.Add(group);

      spec["groups"] = list;
    }

    if (labels is { Count: > 0 }) {
      var map = new JsonObject();

      foreach (var (key, value) in labels)
        map[key] = value;

      spec["labels"] = map;
    }

    if (parameters is { Count: > 0 }) {
      var map = new JsonObject();

      foreach (var (key, value) in parameters)
        map[key] = value;

      spec["parameters"] = map;
    }

    return new JsonObject {
      ["apiVersion"] = "longhorn.io/v1beta2",
      ["kind"] = "RecurringJob",
      ["metadata"] = new JsonObject {
        ["name"] = name,
        ["namespace"] = ns
      },
      ["spec"] = spec
    };
  }

  public static IReadOnlyList<string> GroupsOf(JsonObject item) {
    if (item["spec"]?["groups"] is not JsonArray groups)
      return [];

    var names = new List<string>();

    foreach (var group in groups) {
      var name = JsonPath.Text(group);

      if (name.Length > 0)
        names.Add(name);
    }

    return names;
  }

  public static IReadOnlyDictionary<string, RecurringRun> Runs(
    IReadOnlyList<JsonObject> cronJobs,
    IReadOnlyList<JsonObject> jobs) {
    var runs = new Dictionary<string, RunAcc>(StringComparer.Ordinal);

    foreach (var cron in cronJobs)
      NoteSuccess(runs, JsonPath.Namespace(cron), JsonPath.Name(cron), JsonPath.Text(cron["status"]?["lastSuccessfulTime"]));

    foreach (var job in jobs) {
      var owner = CronJobOwner(job);

      if (owner.Length == 0)
        continue;

      var acc = Account(runs, JsonPath.Namespace(job), owner);

      if (InProgress(job)) {
        acc.Active++;

        continue;
      }

      if (Succeeded(job)) {
        NoteSuccess(runs, JsonPath.Namespace(job), owner, JsonPath.Text(job["status"]?["completionTime"]));

        continue;
      }

      if (!Failed(job) || !JsonPath.TryTimestamp(FinishedAt(job), out var when))
        continue;

      if (acc.HasFailure && when <= acc.FailureAt)
        continue;

      acc.HasFailure = true;
      acc.FailureAt = when;
      acc.Failure = FailureMessage(job);
    }

    return runs.ToDictionary(pair => pair.Key, pair => Describe(pair.Value), StringComparer.Ordinal);
  }

  public static string RunKey(string? ns, string name) =>
    (ns ?? "") + "/" + name;

  private static void NoteSuccess(Dictionary<string, RunAcc> runs, string? ns, string name, string timestamp) {
    if (name.Length == 0 || !JsonPath.TryTimestamp(timestamp, out var when))
      return;

    var acc = Account(runs, ns, name);

    if (acc.HasSuccess && when <= acc.LastSuccess)
      return;

    acc.HasSuccess = true;
    acc.LastSuccess = when;
  }

  private static RunAcc Account(Dictionary<string, RunAcc> runs, string? ns, string name) {
    var key = RunKey(ns, name);

    if (runs.TryGetValue(key, out var acc))
      return acc;

    acc = new RunAcc();
    runs[key] = acc;

    return acc;
  }

  private static RecurringRun Describe(RunAcc acc) {
    var last = acc.HasSuccess
      ? acc.LastSuccess.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z"
      : "";

    if (acc.Active > 0) {
      var detail = acc.Active == 1 ? "A run is in progress." : acc.Active + " runs are in progress.";

      return new RecurringRun("Running", detail, last);
    }

    if (acc.HasFailure && (!acc.HasSuccess || acc.FailureAt > acc.LastSuccess))
      return new RecurringRun("Failed", acc.Failure.Length > 0 ? acc.Failure : "The latest run failed.", last);

    return new RecurringRun("Idle", "", last);
  }

  private static bool InProgress(JsonObject job) =>
    Count(job["status"]?["active"]) > 0 || (!Succeeded(job) && !Failed(job));

  private static bool Succeeded(JsonObject job) =>
    Count(job["status"]?["succeeded"]) > 0;

  private static bool Failed(JsonObject job) {
    if (job["status"]?["conditions"] is not JsonArray conditions)
      return false;

    foreach (var condition in conditions) {
      if (condition is not JsonObject item)
        continue;

      if (JsonPath.Text(item["type"]).Equals("Failed", StringComparison.Ordinal) && JsonPath.Text(item["status"]).Equals("True", StringComparison.Ordinal))
        return true;
    }

    return false;
  }

  private static string FailureMessage(JsonObject job) {
    if (job["status"]?["conditions"] is not JsonArray conditions)
      return "The latest run failed.";

    foreach (var condition in conditions) {
      if (condition is not JsonObject item)
        continue;

      if (!JsonPath.Text(item["type"]).Equals("Failed", StringComparison.Ordinal))
        continue;

      var message = JsonPath.Text(item["message"]);

      if (message.Length > 0)
        return message;
    }

    return "The latest run failed.";
  }

  private static string FinishedAt(JsonObject job) {
    var completion = JsonPath.Text(job["status"]?["completionTime"]);

    if (completion.Length > 0)
      return completion;

    var start = JsonPath.Text(job["status"]?["startTime"]);

    if (start.Length > 0)
      return start;

    return JsonPath.Text(job["metadata"]?["creationTimestamp"]);
  }

  private static int Count(JsonNode? node) {
    if (node is JsonValue value) {
      if (value.TryGetValue<int>(out var count))
        return count;

      if (value.TryGetValue<long>(out var wide))
        return wide > int.MaxValue ? int.MaxValue : (int)wide;
    }

    return int.TryParse(JsonPath.Text(node), NumberStyles.Integer, CultureInfo.InvariantCulture, out var parsed)
      ? parsed
      : 0;
  }

  private sealed class RunAcc {
    public DateTimeOffset LastSuccess;
    public bool HasSuccess;
    public int Active;
    public DateTimeOffset FailureAt;
    public bool HasFailure;
    public string Failure = "";
  }

  private static string CronJobOwner(JsonObject job) {
    if (job["metadata"]?["ownerReferences"] is not JsonArray owners)
      return "";

    foreach (var owner in owners) {
      if (owner is not JsonObject item)
        continue;

      if (JsonPath.Text(item["kind"]).Equals("CronJob", StringComparison.Ordinal))
        return JsonPath.Text(item["name"]);
    }

    return "";
  }

  public static IReadOnlyDictionary<string, string> MapOf(JsonObject item, string field) {
    if (item["spec"]?[field] is not JsonObject map)
      return new Dictionary<string, string>(StringComparer.Ordinal);

    var values = new Dictionary<string, string>(StringComparer.Ordinal);

    foreach (var pair in map) {
      var text = JsonPath.Text(pair.Value);

      if (pair.Key.Length > 0 && text.Length > 0)
        values[pair.Key] = text;
    }

    return values;
  }

  public static int ConcurrencyOf(JsonObject item) {
    var text = JsonPath.Text(item["spec"]?["concurrency"]);

    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
      return value;

    return 1;
  }

  public static int RetainOf(JsonObject item) {
    var text = JsonPath.Text(item["spec"]?["retain"]);

    if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var value) && value > 0)
      return value;

    return 7;
  }

  public static bool IsDnsLabel(string name) =>
    name.Length is >= 1 and <= 63 && Regex.IsMatch(name, "^[a-z0-9]([-a-z0-9]*[a-z0-9])?$");

  public static bool IsFiveField(string cron) =>
    TrySplit(cron, out _, out _, out _, out _, out _);

  public static bool TryFields(string cron, out CronFields fields) {
    fields = default;

    if (!TrySplit(cron, out var minute, out var hour, out var day, out var month, out var weekday))
      return false;

    if (!Has(MinuteChoices, minute) || !Has(HourChoices, hour) || !Has(DayChoices, day) || !Has(MonthChoices, month) || !Has(WeekdayChoices, weekday))
      return false;

    fields = new CronFields(minute, hour, day, month, weekday);

    return true;
  }

  public static string Describe(string cron) {
    if (!TrySplit(cron, out var minute, out var hour, out var day, out var month, out var weekday))
      return cron.Trim();

    var text = minute + " " + hour + " " + day + " " + month + " " + weekday;

    if (!IsSimple(minute) || !IsSimple(hour) || !IsSimple(day) || !IsSimple(month) || !IsSimple(weekday))
      return text;

    if (minute.StartsWith("*/", StringComparison.Ordinal) && hour == "*" && day == "*" && month == "*" && weekday == "*")
      return "Every " + minute[2..] + " minutes";

    if (hour.StartsWith("*/", StringComparison.Ordinal) && IsNumber(minute) && day == "*" && month == "*" && weekday == "*") {
      var at = minute == "0" ? "" : " at minute " + minute;

      return "Every " + hour[2..] + " hours" + at;
    }

    if (minute == "0" && hour == "*" && day == "*" && month == "*" && weekday == "*")
      return "Every hour";

    if (IsNumber(minute) && hour == "*" && day == "*" && month == "*" && weekday == "*")
      return "Every hour at minute " + minute;

    if (IsNumber(minute) && IsNumber(hour) && day == "*" && month == "*" && weekday == "*")
      return "Every day at " + Clock(hour, minute);

    if (IsNumber(minute) && IsNumber(hour) && day == "*" && month == "*" && WeekdayName(weekday) is { } weekdayName)
      return "Every " + weekdayName + " at " + Clock(hour, minute);

    if (IsNumber(minute) && IsNumber(hour) && IsNumber(day) && month == "*" && weekday == "*")
      return "At " + Clock(hour, minute) + " on day " + day + " of every month";

    if (IsNumber(minute) && IsNumber(hour) && day == "*" && MonthName(month) is { } monthName && weekday == "*")
      return "At " + Clock(hour, minute) + " every day in " + monthName;

    return text;
  }

  public static CronChoice? Find(IReadOnlyList<CronChoice> choices, string token) {
    foreach (var choice in choices) {
      if (choice.Token == token)
        return choice;
    }

    return null;
  }

  private static string State(JsonObject? item) {
    if (JsonPath.Text(item?["metadata"]?["deletionTimestamp"]).Length > 0)
      return "Terminating";

    if (Message(item).Length > 0)
      return "Error";

    return "Ready";
  }

  private static string VolumeName(JsonObject? item) {
    var named = JsonPath.Text(item?["spec"]?["volumeName"]);

    if (named.Length > 0)
      return named;

    return JsonPath.Text(item?["status"]?["volumeName"]);
  }

  private static string Message(JsonObject? item) {
    if (item?["status"]?["messages"] is not JsonObject messages)
      return JsonPath.Text(item?["status"]?["error"]);

    var parts = new List<string>();

    foreach (var pair in messages) {
      var text = JsonPath.Text(pair.Value);

      if (text.Length > 0)
        parts.Add(text);
    }

    return string.Join("; ", parts);
  }

  private static string Created(JsonObject? item) {
    var created = When(item?["status"]?["createdAt"]);

    if (created.Length > 0)
      return created;

    created = When(item?["status"]?["backupCreatedAt"]);

    if (created.Length > 0)
      return created;

    return When(item?["status"]?["snapshotCreatedAt"]);
  }

  private static string When(JsonNode? node) {
    var text = JsonPath.Text(node);

    if (!JsonPath.TryTimestamp(text, out var value))
      return "";

    return value.UtcDateTime.ToString("yyyy-MM-dd HH:mm", CultureInfo.InvariantCulture) + "Z";
  }

  private static string Size(JsonNode? node) {
    var text = JsonPath.Text(node);

    if (text.Length == 0)
      return "";

    if (long.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var bytes) && bytes > 0)
      return KubeQuantity.FormatBytesCompact(bytes);

    var parsed = KubeQuantity.ToBytes(text);

    return parsed > 0 ? KubeQuantity.FormatBytesCompact(parsed) : text;
  }

  private static string TaskLabel(JsonObject? item) {
    var stored = JsonPath.Text(item?["spec"]?["task"]);
    var task = FindTask(BaseTask(stored));
    var label = task?.Label ?? stored;

    return IsForceTask(stored) ? label + " (force)" : label;
  }

  private static string Groups(JsonObject? item) =>
    item is null ? "" : string.Join(", ", GroupsOf(item));

  private static bool TrySplit(string cron, out string minute, out string hour, out string day, out string month, out string weekday) {
    minute = "";
    hour = "";
    day = "";
    month = "";
    weekday = "";
    var parts = cron.Trim().Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries);

    if (parts.Length != 5)
      return false;

    foreach (var part in parts) {
      if (!Regex.IsMatch(part, "^[A-Za-z0-9*,/-]+$"))
        return false;
    }

    minute = parts[0];
    hour = parts[1];
    day = parts[2];
    month = parts[3];
    weekday = parts[4];

    return true;
  }

  private static bool Has(IReadOnlyList<CronChoice> choices, string token) =>
    Find(choices, token) is not null;

  private static bool IsSimple(string token) =>
    token == "*" || IsNumber(token) || (token.StartsWith("*/", StringComparison.Ordinal) && IsNumber(token[2..]));

  private static bool IsNumber(string token) =>
    token.Length > 0 && token.All(char.IsAsciiDigit);

  private static string Clock(string hour, string minute) =>
    int.Parse(hour, CultureInfo.InvariantCulture).ToString("00", CultureInfo.InvariantCulture)
    + ":"
    + int.Parse(minute, CultureInfo.InvariantCulture).ToString("00", CultureInfo.InvariantCulture);

  private static string? WeekdayName(string token) =>
    token switch {
      "0" or "7" => "Sunday",
      "1" => "Monday",
      "2" => "Tuesday",
      "3" => "Wednesday",
      "4" => "Thursday",
      "5" => "Friday",
      "6" => "Saturday",
      _ => null
    };

  private static string? MonthName(string token) =>
    token switch {
      "1" => "January",
      "2" => "February",
      "3" => "March",
      "4" => "April",
      "5" => "May",
      "6" => "June",
      "7" => "July",
      "8" => "August",
      "9" => "September",
      "10" => "October",
      "11" => "November",
      "12" => "December",
      _ => null
    };

  private static CronChoice[] Minutes() {
    var choices = new List<CronChoice> { new("*", "Every") };

    for (var minute = 0; minute < 60; minute++)
      choices.Add(new(minute.ToString(CultureInfo.InvariantCulture), minute.ToString("00", CultureInfo.InvariantCulture)));

    foreach (var step in new[] { 5, 10, 15, 20, 30 })
      choices.Add(new("*/" + step.ToString(CultureInfo.InvariantCulture), "Every " + step.ToString(CultureInfo.InvariantCulture)));

    return choices.ToArray();
  }

  private static CronChoice[] Hours() {
    var choices = new List<CronChoice> { new("*", "Every") };

    for (var hour = 0; hour < 24; hour++)
      choices.Add(new(hour.ToString(CultureInfo.InvariantCulture), hour.ToString("00", CultureInfo.InvariantCulture)));

    foreach (var step in new[] { 2, 3, 4, 6, 8, 12 })
      choices.Add(new("*/" + step.ToString(CultureInfo.InvariantCulture), "Every " + step.ToString(CultureInfo.InvariantCulture)));

    return choices.ToArray();
  }

  private static CronChoice[] Days() {
    var choices = new List<CronChoice> { new("*", "Every") };

    for (var day = 1; day <= 31; day++)
      choices.Add(new(day.ToString(CultureInfo.InvariantCulture), day.ToString(CultureInfo.InvariantCulture)));

    return choices.ToArray();
  }

  private static CronChoice[] Months() {
    var choices = new List<CronChoice> { new("*", "Every") };
    string[] names = ["January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December"];

    for (var month = 1; month <= 12; month++)
      choices.Add(new(month.ToString(CultureInfo.InvariantCulture), names[month - 1]));

    return choices.ToArray();
  }

  private static CronChoice[] Weekdays() => [
    new("*", "Every"),
    new("0", "Sunday"),
    new("1", "Monday"),
    new("2", "Tuesday"),
    new("3", "Wednesday"),
    new("4", "Thursday"),
    new("5", "Friday"),
    new("6", "Saturday")
  ];
}

public readonly record struct RecurringRun(string State, string Detail, string LastSuccess);

public sealed record CronChoice(string Token, string Label);

public readonly record struct CronFields(string Minute, string Hour, string Day, string Month, string Weekday) {
  public string Text =>
    Minute + " " + Hour + " " + Day + " " + Month + " " + Weekday;
}

public sealed record RecurringTask(string Id, string Label, string Hint) {
  public bool AllowsForce => Id is "backup" or "snapshot";

  public bool UsesRetain => Id is not "snapshot-cleanup" and not "filesystem-trim";

  public bool UsesVolumes => Id != "system-backup";

  public bool UsesConcurrency => Id != "system-backup";

  public bool UsesBackupPolicy => Id == "system-backup";

  public bool UsesFullBackupInterval => Id == "backup";
}

public sealed record BackupPolicy(string Id, string Label, string Hint);
