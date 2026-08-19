using System.Text.Unicode;


namespace MaksIT.ClusterConsole.Shared;

public static class VolumeText {
  public const int MaxEditBytes = 1_048_576;

  public static bool CanEdit(byte[]? data) =>
    data is not null && data.Length <= MaxEditBytes && IsText(data);

  public static bool IsText(ReadOnlySpan<byte> data) {
    if (data.IsEmpty)
      return true;
    if (data.IndexOf((byte)0) >= 0)
      return false;
    if (!Utf8.IsValid(data))
      return false;

    var control = 0;
    foreach (var b in data) {
      if (b < 32 && b is not 9 and not 10 and not 13)
        control++;
    }

    return control * 20 <= data.Length;
  }
}
