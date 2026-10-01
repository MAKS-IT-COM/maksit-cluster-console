using MaksIT.ClusterConsole.Shared;


namespace MaksIT.ClusterConsole.Tests;

public class ReleaseNotesTests {
  private const string Sample = """
    ## [Unreleased]

    ### Added

    - Helm Charts lists installed chart versions.

    ## [0.8.4] - 2026-09-30

    ### Added

    - Flathub submission documents are in packaging.

    ### Changed

    - Client code is grouped by area.

    ## [0.8.3] - 2026-09-29

    ### Added

    - Pod Terminal opens a live shell.
    """;

  [Fact]
  public void Current_version_hides_unreleased_and_older_notes() {
    var notes = ReleaseNotes.AddedSince(Sample, seenVersion: null, currentVersion: "0.8.4");

    var note = Assert.Single(notes);
    Assert.Equal("0.8.4", note.Version);
    Assert.Contains("Flathub submission documents are in packaging.", note.Added);
    Assert.DoesNotContain(note.Added, line => line.Contains("Helm", StringComparison.Ordinal));
    Assert.DoesNotContain(note.Added, line => line.Contains("Terminal", StringComparison.Ordinal));
  }

  [Fact]
  public void Skipped_versions_are_listed_newest_first() {
    var notes = ReleaseNotes.AddedSince(Sample, seenVersion: "0.8.2", currentVersion: "0.8.4");

    Assert.Equal(["0.8.4", "0.8.3"], notes.Select(note => note.Version));
  }

  [Fact]
  public void Same_or_newer_seen_version_has_nothing_to_show() {
    Assert.Empty(ReleaseNotes.AddedSince(Sample, "0.8.4", "0.8.4"));
    Assert.Empty(ReleaseNotes.AddedSince(Sample, "0.9.0", "0.8.4"));
  }

  [Fact]
  public void Shipped_changelog_keeps_unreleased_work_out_of_the_current_version() {
    var notes = ReleaseNotes.AddedSince(ReleaseNotes.Text(), seenVersion: null, currentVersion: "0.8.5");

    var note = Assert.Single(notes);
    Assert.Equal("0.8.5", note.Version);
    Assert.Contains(note.Added, line => line.Contains("Helm Charts", StringComparison.Ordinal));
    Assert.DoesNotContain(note.Added, line => line.Contains("Flathub submission", StringComparison.Ordinal));
  }
}
