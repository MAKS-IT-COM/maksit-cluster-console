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
  public void Shipped_notes_keep_older_work_out_of_the_current_version() {
    var notes = ReleaseNotes.AddedSince(ReleaseNotes.Text(), seenVersion: null, currentVersion: "0.8.8");

    var note = Assert.Single(notes);
    Assert.Equal("0.8.8", note.Version);
    Assert.Contains(note.Added, line => line.Contains("ControllerRevision", StringComparison.Ordinal));
    Assert.DoesNotContain(note.Added, line => line.Contains("Analyze issues", StringComparison.Ordinal));
    Assert.DoesNotContain(note.Added, line => line.Contains("drain dialog", StringComparison.Ordinal));
    Assert.DoesNotContain(note.Added, line => line.Contains("Helm Charts", StringComparison.Ordinal));
    Assert.DoesNotContain(note.Added, line => line.Contains("footer", StringComparison.OrdinalIgnoreCase));
  }

  [Fact]
  public void Version_bullets_skip_a_technical_heading() {
    const string markdown = """
      ## [0.8.6] - 2026-10-03

      - The drain dialog can show a short AI note.

      ### Changed

      - The window footer is a separate control.
      """;

    var notes = ReleaseNotes.AddedSince(markdown, seenVersion: null, currentVersion: "0.8.6");

    var note = Assert.Single(notes);
    Assert.Equal(["The drain dialog can show a short AI note."], note.Added);
  }
}
