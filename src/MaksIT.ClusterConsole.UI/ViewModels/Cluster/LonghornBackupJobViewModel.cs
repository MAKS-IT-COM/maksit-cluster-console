using System.Text.Json.Nodes;
using CommunityToolkit.Mvvm.Input;
using CommunityToolkit.Mvvm.ComponentModel;
using MaksIT.ClusterConsole.Shared;



namespace MaksIT.ClusterConsole.UI.ViewModels.Cluster;

public partial class LonghornBackupVolumeChoice : ObservableObject {
  public LonghornBackupVolumeChoice(JsonObject document, bool preselected) {
    Document = document;
    var row = ResourceRow.From(document, ResourceCatalog.Find(ResourceCatalog.LonghornVolumesId)!);
    Name = row.Cell("Name");
    Namespace = JsonPath.Namespace(document) ?? "longhorn-system";
    State = row.Cell("State");
    Size = row.Cell("Size");
    ActualSize = row.Cell("Actual Size");
    Created = row.Cell("Created");
    DataEngine = row.Cell("Data Engine");
    Pv = row.Cell("PV/PVC");
    WorkloadNamespace = row.Cell("Namespace");
    Claim = row.Cell("Volume");
    Node = row.Cell("Node");
    Schedule = row.Cell("Schedule");
    LastBackupAt = row.Cell("Last Backup At");
    StateTip = row.CellTip("State");
    PvTip = row.CellTip("PV/PVC");
    NodeTip = row.CellTip("Node");
    Preselected = preselected;
    Selected = preselected;
  }

  public JsonObject Document { get; }

  public string Name { get; }

  public string Namespace { get; }

  public string State { get; }

  public string Size { get; }

  public string ActualSize { get; }

  public string Created { get; }

  public string DataEngine { get; }

  public string Pv { get; }

  public string WorkloadNamespace { get; }

  public string Claim { get; }

  public string Node { get; }

  public string Schedule { get; }

  public string LastBackupAt { get; }

  public string StateTip { get; }

  public string PvTip { get; }

  public string NodeTip { get; }

  public bool Preselected { get; }

  [ObservableProperty]
  private bool selected;
}

public partial class LonghornBackupJobViewModel : ObservableObject {
  public const string NewJob = "New job";

  private bool _fromFields;
  private bool _fromText;
  private bool _loadingJob;
  private string _originalName = "";

  public LonghornBackupJobViewModel(
    bool chooseJob,
    string? jobName,
    string task,
    string cron,
    int retain,
    IReadOnlyList<string> groups,
    int concurrency,
    IReadOnlyDictionary<string, string> labels,
    IReadOnlyDictionary<string, string> parameters,
    string ns,
    IReadOnlyDictionary<string, JsonObject> jobs,
    IReadOnlyList<LonghornBackupVolumeChoice> volumes) {
    ChooseJob = chooseJob;
    Namespace = ns;
    Jobs = jobs;
    Volumes = volumes;
    Labels = labels;
    _originalName = jobName ?? "";
    JobChoices = chooseJob
      ? [NewJob, .. jobs.Where(pair => LonghornBackup.IsBackupJob(pair.Value)).Select(pair => pair.Key).Order(StringComparer.Ordinal)]
      : [];
    _loadingJob = true;
    SelectedJob = chooseJob && string.IsNullOrEmpty(jobName) ? NewJob : jobName;
    Name = jobName ?? "";
    ApplyStored(task, groups, concurrency, retain, parameters);
    CronText = string.IsNullOrWhiteSpace(cron) ? LonghornBackup.Daily.Text : cron;
    ApplyFields(CronText);
    Sentence = LonghornBackup.Describe(CronText);
    _loadingJob = false;
  }

  public bool ChooseJob { get; }

  public string Namespace { get; }

  public IReadOnlyDictionary<string, JsonObject> Jobs { get; }

  public IReadOnlyList<LonghornBackupVolumeChoice> Volumes { get; }

  public IReadOnlyDictionary<string, string> Labels { get; private set; }

  public IReadOnlyList<string> JobChoices { get; }

  public IReadOnlyList<RecurringTask> Tasks => LonghornBackup.Tasks;

  public IReadOnlyList<BackupPolicy> Policies => LonghornBackup.Policies;

  public IReadOnlyList<CronChoice> MinuteChoices => LonghornBackup.MinuteChoices;

  public IReadOnlyList<CronChoice> HourChoices => LonghornBackup.HourChoices;

  public IReadOnlyList<CronChoice> DayChoices => LonghornBackup.DayChoices;

  public IReadOnlyList<CronChoice> MonthChoices => LonghornBackup.MonthChoices;

  public IReadOnlyList<CronChoice> WeekdayChoices => LonghornBackup.WeekdayChoices;

  public IReadOnlyList<string> Groups {
    get {
      var names = new List<string>();

      if (DefaultGroup)
        names.Add("default");

      foreach (var part in GroupsText.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)) {
        if (!names.Contains(part, StringComparer.Ordinal))
          names.Add(part);
      }

      return names;
    }
  }

  public bool ShowForce => SelectedTask?.AllowsForce == true;

  public bool ShowVolumes => SelectedTask?.UsesVolumes != false;

  public bool ShowConcurrency => SelectedTask?.UsesConcurrency != false;

  public bool ShowPolicy => SelectedTask?.UsesBackupPolicy == true;

  public string PolicyHint =>
    ShowPolicy ? SelectedPolicy?.Hint ?? "" : "";

  public bool ShowFullBackup => SelectedTask?.UsesFullBackupInterval == true;

  public bool UsesRetain => SelectedTask?.UsesRetain != false;

  public bool AssignsVolumes => ShowVolumes;

  public string Heading =>
    ChooseJob ? "Backup job" : "Recurring job";

  public string TaskHint {
    get {
      if (SelectedTask is null)
        return "";

      return ForceCreate && SelectedTask.AllowsForce
        ? SelectedTask.Hint + " " + LonghornBackup.ForceHint
        : SelectedTask.Hint;
    }
  }

  public IReadOnlyDictionary<string, string> Parameters() {
    var map = new Dictionary<string, string>(StringComparer.Ordinal);

    if (ShowPolicy && SelectedPolicy is not null)
      map["volume-backup-policy"] = SelectedPolicy.Id;

    if (ShowFullBackup && FullBackupInterval > 0)
      map["full-backup-interval"] = FullBackupInterval.ToString(System.Globalization.CultureInfo.InvariantCulture);

    return map;
  }

  public string OriginalName => _originalName;

  public string AcceptLabel =>
    ChooseJob ? "Schedule" : "Save";

  [ObservableProperty]
  private string? selectedJob;

  [ObservableProperty]
  private string name = "";

  [ObservableProperty]
  private string cronText = "";

  [ObservableProperty]
  private string sentence = "";

  [ObservableProperty]
  private int retain = 7;

  [ObservableProperty]
  private int concurrency = 1;

  [ObservableProperty]
  private bool forceCreate;

  [ObservableProperty]
  private bool defaultGroup;

  [ObservableProperty]
  private string groupsText = "";

  [ObservableProperty]
  private int fullBackupInterval;

  [ObservableProperty]
  private RecurringTask? selectedTask;

  [ObservableProperty]
  private BackupPolicy? selectedPolicy;

  [ObservableProperty]
  private string error = "";

  [ObservableProperty]
  private CronChoice? selectedMinute;

  [ObservableProperty]
  private CronChoice? selectedHour;

  [ObservableProperty]
  private CronChoice? selectedDay;

  [ObservableProperty]
  private CronChoice? selectedMonth;

  [ObservableProperty]
  private CronChoice? selectedWeekday;

  public string? Validate() {
    var job = Name.Trim();

    if (!LonghornBackup.IsDnsLabel(job))
      return "Job name must be a lowercase DNS label.";

    var original = OriginalName;

    if (!string.Equals(job, original, StringComparison.Ordinal) && Jobs.ContainsKey(job))
      return "A recurring job with that name already exists.";

    if (!LonghornBackup.IsFiveField(CronText))
      return "Schedule must be five cron fields.";

    if (UsesRetain && Retain < 1)
      return "Keep at least one.";

    return null;
  }

  public string JobNamespace() {
    if (!string.IsNullOrWhiteSpace(Namespace))
      return Namespace;

    var selected = Volumes.FirstOrDefault(volume => volume.Selected);

    return selected?.Namespace ?? "longhorn-system";
  }

  partial void OnSelectedJobChanged(string? value) {
    if (_loadingJob || !ChooseJob || value is null)
      return;

    if (value == NewJob) {
      _originalName = "";
      Name = "";
      Labels = new Dictionary<string, string>(StringComparer.Ordinal);
      ApplyStored("backup", [], 1, 7, new Dictionary<string, string>());
      SetCron(LonghornBackup.Daily.Text);

      foreach (var volume in Volumes)
        volume.Selected = volume.Preselected;

      return;
    }

    if (!Jobs.TryGetValue(value, out var job))
      return;

    Name = value;
    _originalName = value;
    Labels = LonghornBackup.MapOf(job, "labels");
    ApplyStored(
      JsonPath.Text(job["spec"]?["task"]),
      LonghornBackup.GroupsOf(job),
      LonghornBackup.ConcurrencyOf(job),
      LonghornBackup.RetainOf(job),
      LonghornBackup.MapOf(job, "parameters"));
    SetCron(JsonPath.Text(job["spec"]?["cron"]));

    foreach (var volume in Volumes)
      volume.Selected = volume.Preselected || LonghornBackup.VolumeHasJob(volume.Document, value);
  }

  partial void OnCronTextChanged(string value) {
    if (_fromFields)
      return;

    Sentence = LonghornBackup.Describe(value);

    if (!LonghornBackup.TryFields(value, out var fields)) {
      _fromText = true;
      SelectedMinute = null;
      SelectedHour = null;
      SelectedDay = null;
      SelectedMonth = null;
      SelectedWeekday = null;
      _fromText = false;

      return;
    }

    _fromText = true;
    SelectedMinute = LonghornBackup.Find(MinuteChoices, fields.Minute);
    SelectedHour = LonghornBackup.Find(HourChoices, fields.Hour);
    SelectedDay = LonghornBackup.Find(DayChoices, fields.Day);
    SelectedMonth = LonghornBackup.Find(MonthChoices, fields.Month);
    SelectedWeekday = LonghornBackup.Find(WeekdayChoices, fields.Weekday);
    _fromText = false;
  }

  partial void OnSelectedMinuteChanged(CronChoice? value) =>
    RebuildFromFields();

  partial void OnSelectedHourChanged(CronChoice? value) =>
    RebuildFromFields();

  partial void OnSelectedDayChanged(CronChoice? value) =>
    RebuildFromFields();

  partial void OnSelectedMonthChanged(CronChoice? value) =>
    RebuildFromFields();

  partial void OnSelectedWeekdayChanged(CronChoice? value) =>
    RebuildFromFields();

  [RelayCommand]
  private void UseHourly() =>
    SetCron(LonghornBackup.Hourly.Text);

  [RelayCommand]
  private void UseDaily() =>
    SetCron(LonghornBackup.Daily.Text);

  [RelayCommand]
  private void UseWeekly() =>
    SetCron(LonghornBackup.Weekly.Text);

  private void ApplyStored(
    string task,
    IReadOnlyList<string> groups,
    int concurrency,
    int retain,
    IReadOnlyDictionary<string, string> parameters) {
    ForceCreate = LonghornBackup.IsForceTask(task);
    var id = LonghornBackup.BaseTask(string.IsNullOrWhiteSpace(task) ? "backup" : task);
    SelectedTask = LonghornBackup.FindTask(id) ?? LonghornBackup.Tasks[0];
    DefaultGroup = groups.Contains("default", StringComparer.Ordinal);
    GroupsText = string.Join(", ", groups.Where(group => !string.Equals(group, "default", StringComparison.Ordinal)));
    Concurrency = concurrency < 1 ? 1 : concurrency;
    Retain = SelectedTask?.UsesRetain == false ? 0 : retain < 1 ? 7 : retain;
    var policy = parameters.TryGetValue("volume-backup-policy", out var policyId) ? policyId : "if-not-present";
    SelectedPolicy = LonghornBackup.FindPolicy(policy) ?? LonghornBackup.Policies[0];
    FullBackupInterval = parameters.TryGetValue("full-backup-interval", out var interval)
      && int.TryParse(interval, System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out var parsed)
      && parsed > 0
      ? parsed
      : 0;
  }

  partial void OnSelectedTaskChanged(RecurringTask? value) {
    if (!_loadingJob && value?.AllowsForce != true)
      ForceCreate = false;

    OnPropertyChanged(nameof(ShowForce));
    OnPropertyChanged(nameof(ShowVolumes));
    OnPropertyChanged(nameof(ShowConcurrency));
    OnPropertyChanged(nameof(ShowPolicy));
    OnPropertyChanged(nameof(PolicyHint));
    OnPropertyChanged(nameof(ShowFullBackup));
    OnPropertyChanged(nameof(UsesRetain));
    OnPropertyChanged(nameof(AssignsVolumes));
    OnPropertyChanged(nameof(TaskHint));
  }

  partial void OnForceCreateChanged(bool value) =>
    OnPropertyChanged(nameof(TaskHint));

  partial void OnSelectedPolicyChanged(BackupPolicy? value) =>
    OnPropertyChanged(nameof(PolicyHint));

  private void SetCron(string cron) {
    if (string.IsNullOrWhiteSpace(cron))
      cron = LonghornBackup.Daily.Text;

    CronText = cron;
    ApplyFields(cron);
    Sentence = LonghornBackup.Describe(cron);
  }

  private void ApplyFields(string cron) {
    if (!LonghornBackup.TryFields(cron, out var fields))
      return;

    _fromText = true;
    SelectedMinute = LonghornBackup.Find(MinuteChoices, fields.Minute);
    SelectedHour = LonghornBackup.Find(HourChoices, fields.Hour);
    SelectedDay = LonghornBackup.Find(DayChoices, fields.Day);
    SelectedMonth = LonghornBackup.Find(MonthChoices, fields.Month);
    SelectedWeekday = LonghornBackup.Find(WeekdayChoices, fields.Weekday);
    _fromText = false;
  }

  private void RebuildFromFields() {
    if (_fromText || _loadingJob)
      return;

    if (SelectedMinute is null || SelectedHour is null || SelectedDay is null || SelectedMonth is null || SelectedWeekday is null)
      return;

    var text = new CronFields(
      SelectedMinute.Token,
      SelectedHour.Token,
      SelectedDay.Token,
      SelectedMonth.Token,
      SelectedWeekday.Token).Text;
    _fromFields = true;
    CronText = text;
    Sentence = LonghornBackup.Describe(text);
    _fromFields = false;
  }
}
