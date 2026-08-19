using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.UI.ViewModels;

public partial class ColumnFilterValueViewModel : ObservableObject {
  private readonly Action _changed;

  public ColumnFilterValueViewModel(string value, bool included, Action changed) {
    Value = value;
    Display = string.IsNullOrEmpty(value) ? "(blank)" : value;
    isIncluded = included;
    _changed = changed;
  }

  public string Value { get; }

  public string Display { get; }

  [ObservableProperty]
  private bool isIncluded;

  partial void OnIsIncludedChanged(bool value) => _changed();
}

public partial class ColumnFilterViewModel : ObservableObject {
  private readonly Action _changed;
  private bool _suppress;

  public ColumnFilterViewModel(string header, Action changed) {
    Header = header;
    Model = new ResourceColumnFilter { Header = header };
    _changed = changed;
  }

  public string Header { get; }

  public ResourceColumnFilter Model { get; }

  public ObservableCollection<ColumnFilterValueViewModel> Values { get; } = [];

  [ObservableProperty]
  private string text = "";

  public bool IsActive => Model.IsActive;

  public IEnumerable<ColumnFilterValueViewModel> VisibleValues {
    get {
      if (string.IsNullOrWhiteSpace(Text))
        return Values;
      return Values.Where(value => value.Display.Contains(Text, StringComparison.OrdinalIgnoreCase));
    }
  }

  public void IncludeOnly(string value) {
    _suppress = true;
    Text = "";
    foreach (var item in Values)
      item.IsIncluded = string.Equals(item.Value, value, StringComparison.Ordinal);
    SyncModel();
    _suppress = false;
    OnPropertyChanged(nameof(VisibleValues));
    OnPropertyChanged(nameof(IsActive));
  }

  public void SelectOnly(string value) {
    IncludeOnly(value);
    _changed();
  }

  public void Restore(SavedColumnFilter saved) {
    _suppress = true;
    text = saved.Text ?? "";
    OnPropertyChanged(nameof(Text));
    Model.Text = text;
    Model.Excluded.Clear();
    foreach (var value in saved.Excluded ?? [])
      Model.Excluded.Add(value);
    _suppress = false;
    OnPropertyChanged(nameof(VisibleValues));
    OnPropertyChanged(nameof(IsActive));
  }

  public SavedColumnFilter Snapshot() =>
    new() {
      Text = Model.Text,
      Excluded = [.. Model.Excluded]
    };

  public void LoadValues(IReadOnlyList<ResourceRow> rows) {
    _suppress = true;
    var excluded = new HashSet<string>(Model.Excluded, StringComparer.Ordinal);
    Values.Clear();
    foreach (var value in ResourceColumnFilter.DistinctValues(rows, Header))
      Values.Add(new ColumnFilterValueViewModel(value, !excluded.Contains(value), OnValueChanged));
    SyncModel();
    _suppress = false;
    OnPropertyChanged(nameof(VisibleValues));
    OnPropertyChanged(nameof(IsActive));
  }

  [RelayCommand]
  private void Clear() {
    Text = "";
    _suppress = true;
    foreach (var value in Values)
      value.IsIncluded = true;
    _suppress = false;
    SyncModel();
    OnPropertyChanged(nameof(IsActive));
    _changed();
  }

  partial void OnTextChanged(string value) {
    SyncModel();
    OnPropertyChanged(nameof(VisibleValues));
    OnPropertyChanged(nameof(IsActive));
    if (!_suppress)
      _changed();
  }

  private void OnValueChanged() {
    if (_suppress)
      return;
    SyncModel();
    OnPropertyChanged(nameof(IsActive));
    _changed();
  }

  private void SyncModel() {
    Model.Text = Text;
    Model.Excluded.Clear();
    foreach (var value in Values) {
      if (!value.IsIncluded)
        Model.Excluded.Add(value.Value);
    }
  }
}
