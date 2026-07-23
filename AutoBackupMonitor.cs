using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using Rhino;
using Rhino.FileIO;
using Rhino.Input;
using Rhino.Input.Custom;

namespace vAutoBackup;

/// <summary>
/// Core logic for periodic and on-demand backup of the active Rhino document.
/// Rhino document access and all public-facing state are confined to the UI thread.
/// File verification, finalization, and retention cleanup run in the background.
/// </summary>
internal static class AutoBackupMonitor
{
  // ---------------------------------------------------------------------------
  // State
  // ---------------------------------------------------------------------------

  private static bool _running;
  private static bool _executing; // re-entrancy guard for the idle handler
  private static EventHandler? _idleHandler;
  private static EventHandler? _completionIdleHandler;
  private static DateTime _nextRun = DateTime.MaxValue;
  private static uint _lastActiveDocSerial;

  // Background file work posts its results here. A dedicated Rhino Idle handler
  // drains the queue so state changes and command-line output stay on the UI thread.
  private static readonly ConcurrentQueue<Action> _uiCompletions = new();

  // Per-document change tokens: key = "runtimeSerial|path"
  private static readonly Dictionary<string, (bool Modified, uint UndoSerial)> _changeTokens = new();

  // Stable names for unsaved documents, keyed by RuntimeSerialNumber
  private static readonly Dictionary<uint, string> _unsavedNames = new();

  private readonly record struct BackupResult(
    bool Success,
    string Message,
    bool VerboseOnly,
    int DeletedCount);

  // ---------------------------------------------------------------------------
  // Public API
  // ---------------------------------------------------------------------------

  internal static bool IsRunning => _running;
  internal static DateTime NextRun => _nextRun;

  /// <summary>
  /// Starts the periodic backup timer with the current persisted settings.
  /// Any previously running timer is stopped first (single-instance guarantee).
  /// </summary>
  internal static void Start(bool printMessage = true)
  {
    var settings = AutoBackupSettings.Current;

    DetachIdleHandler();

    // Seed baseline for the current document so the first tick after Start
    // does not create a redundant backup when nothing has changed.
    if (settings.SkipIfUnchanged)
      SeedDocBaseline(RhinoDoc.ActiveDoc, settings);

    _lastActiveDocSerial = 0; // force document-switch check on first idle tick
    _nextRun = DateTime.Now + IntervalSpan(settings);
    _running = true;

    _idleHandler = OnIdle;
    RhinoApp.Idle += _idleHandler;

    if (printMessage)
      RhinoApp.WriteLine($"AutoBackup started: every {settings.IntervalMinutes:G} minute(s).");
  }

  /// <summary>Stops the periodic timer.</summary>
  internal static void Stop(bool silent = false)
  {
    var wasRunning = _running;
    DetachIdleHandler();

    if (!silent)
      RhinoApp.WriteLine(wasRunning ? "AutoBackup stopped." : "AutoBackup was not running.");
  }

  /// <summary>Prints status to the Rhino command line.</summary>
  internal static void PrintStatus()
  {
    var settings = AutoBackupSettings.Current;
    if (!_running)
    {
      RhinoApp.WriteLine(
        $"AutoBackup status: stopped | cleanup={settings.EnableCleanup} (keep {settings.KeepLast}) | verbose={settings.VerboseLogging}");
      return;
    }

    RhinoApp.WriteLine(
      $"AutoBackup status: running (every {settings.IntervalMinutes:G} min, " +
      $"next in {FormatRemaining(_nextRun)}, at {_nextRun:HH:mm:ss}) | " +
      $"cleanup={settings.EnableCleanup} (keep {settings.KeepLast}) | verbose={settings.VerboseLogging}");
  }

  /// <summary>
  /// Starts an immediate backup of the active document, bypassing the change-skip filter.
  /// Returns true when the document write succeeds and background verification starts.
  /// </summary>
  internal static bool BackupNow()
  {
    if (_executing)
    {
      RhinoApp.WriteLine(WithNextRun("AutoBackup: a backup is already in progress."));
      return false;
    }

    var doc = RhinoDoc.ActiveDoc;
    if (doc is null)
    {
      RhinoApp.WriteLine(WithNextRun("AutoBackup: backup skipped (no active document)."));
      return false;
    }

    var settings = AutoBackupSettings.Current;
    _executing = true;
    try
    {
      return BeginBackupDocument(doc, settings, forceBackup: true, result =>
      {
        _executing = false;
        ReportResult(result, settings);
      });
    }
    catch (Exception ex)
    {
      _executing = false;
      RhinoApp.WriteLine(WithNextRun($"AutoBackup error: {CompactDiagnostic(ex.Message)}"));
      return false;
    }
  }

  /// <summary>
  /// Opens the options loop, persists changes, and updates the running timer if active.
  /// Returns true when the user saves; false on cancel.
  /// </summary>
  internal static bool ConfigureOptions()
  {
    var s = AutoBackupSettings.Current;

    var backupRoot = s.BackupRoot;
    var intervalMinutes = s.IntervalMinutes;
    var enableCleanup = s.EnableCleanup;
    var keepLast = s.KeepLast;
    var skipIfUnchanged = s.SkipIfUnchanged;
    var verboseLogging = s.VerboseLogging;
    var autoStart = s.AutoStart;

    while (true)
    {
      var go = new GetOption();
      go.SetCommandPrompt("AutoBackup options");
      go.AcceptNothing(true);
      go.SetCommandPromptDefault("Enter to save");

      var cleanupToggle = new OptionToggle(enableCleanup, "Off", "On");
      var verboseToggle = new OptionToggle(verboseLogging, "Off", "On");
      var skipToggle = new OptionToggle(skipIfUnchanged, "Off", "On");
      var autoStartToggle = new OptionToggle(autoStart, "Off", "On");
      var keepLastOpt = new OptionInteger(keepLast, true, 1);

      // Show current values as second argument per preference rule.
      int backupRootIdx = go.AddOption("BackupRoot", backupRoot);
      int intervalIdx = go.AddOption("IntervalMin",
        intervalMinutes.ToString("G", CultureInfo.InvariantCulture));
      go.AddOptionToggle("Cleanup", ref cleanupToggle);
      go.AddOptionInteger("KeepLast", ref keepLastOpt);
      go.AddOptionToggle("SkipUnchanged", ref skipToggle);
      go.AddOptionToggle("Verbose", ref verboseToggle);
      go.AddOptionToggle("AutoStart", ref autoStartToggle);

      var res = go.Get();

      // Capture toggle / integer values from this pass before any early return.
      enableCleanup = cleanupToggle.CurrentValue;
      verboseLogging = verboseToggle.CurrentValue;
      skipIfUnchanged = skipToggle.CurrentValue;
      autoStart = autoStartToggle.CurrentValue;
      keepLast = keepLastOpt.CurrentValue;

      if (res == GetResult.Cancel)
        return false;
      if (res == GetResult.Nothing)
        break;
      if (res != GetResult.Option)
        continue;

      var optIdx = go.Option().Index;

      if (optIdx == backupRootIdx)
      {
        var gs = new GetString();
        gs.SetCommandPrompt($"Backup root (current: {backupRoot})");
        gs.SetDefaultString(backupRoot);
        if (gs.Get() == GetResult.String)
        {
          var v = gs.StringResult()?.Trim();
          if (!string.IsNullOrEmpty(v))
            backupRoot = v;
        }
        continue;
      }

      if (optIdx == intervalIdx)
      {
        var gs = new GetString();
        gs.SetCommandPrompt($"Backup interval in minutes (current: {intervalMinutes:G})");
        gs.SetDefaultString(intervalMinutes.ToString("G", CultureInfo.InvariantCulture));
        if (gs.Get() == GetResult.String)
        {
          var input = gs.StringResult()?.Trim();
          if (!string.IsNullOrEmpty(input) &&
              double.TryParse(input, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) &&
              parsed > 0)
          {
            intervalMinutes = parsed;
          }
        }
        continue;
      }

      // Toggle / integer options are value-only; just loop back.
    }

    // Validate backup root.
    if (string.IsNullOrWhiteSpace(backupRoot))
    {
      RhinoApp.WriteLine("AutoBackup: backup root cannot be empty. Keeping previous value.");
      backupRoot = s.BackupRoot;
    }

    // Persist.
    s.BackupRoot = backupRoot;
    s.IntervalMinutes = intervalMinutes;
    s.EnableCleanup = enableCleanup;
    s.KeepLast = keepLast;
    s.SkipIfUnchanged = skipIfUnchanged;
    s.VerboseLogging = verboseLogging;
    s.AutoStart = autoStart;
    s.Save();

    // Adjust running timer to the new interval.
    if (_running)
      _nextRun = DateTime.Now + IntervalSpan(s);

    RhinoApp.WriteLine(WithNextRun(
      $"AutoBackup options saved: root='{backupRoot}', interval={intervalMinutes:G} min, " +
      $"cleanup={enableCleanup} (keep {keepLast}), skipUnchanged={skipIfUnchanged}, " +
      $"verbose={verboseLogging}, autoStart={autoStart}"));

    return true;
  }

  // ---------------------------------------------------------------------------
  // Idle handler
  // ---------------------------------------------------------------------------

  private static void OnIdle(object? sender, EventArgs e)
  {
    if (!_running || _executing)
      return;

    var doc = RhinoDoc.ActiveDoc;
    if (doc is null)
      return;

    var settings = AutoBackupSettings.Current;
    var docSerial = doc.RuntimeSerialNumber;

    // Handle active-document switch: reset timer and seed baseline.
    if (_lastActiveDocSerial != docSerial)
    {
      _lastActiveDocSerial = docSerial;
      _nextRun = DateTime.Now + IntervalSpan(settings);

      if (settings.SkipIfUnchanged)
        SeedDocBaseline(doc, settings);

      Log($"AutoBackup: document changed to {DocLabel(doc)}. Timer reset; next backup at {_nextRun:HH:mm:ss}.",
        settings, verboseOnly: true);
      return;
    }

    if (DateTime.Now < _nextRun)
      return;

    _executing = true;
    try
    {
      BeginBackupDocument(doc, settings, forceBackup: false,
        result => CompletePeriodicBackup(result, settings));
    }
    catch (Exception ex)
    {
      CompletePeriodicBackup(
        new BackupResult(
          false,
          $"AutoBackup periodic error: {CompactDiagnostic(ex.Message)}",
          false,
          0),
        settings);
    }
  }

  private static void CompletePeriodicBackup(BackupResult result, AutoBackupSettings settings)
  {
    _executing = false;
    if (_running)
      _nextRun = DateTime.Now + IntervalSpan(AutoBackupSettings.Current);
    ReportResult(result, settings);
  }

  // ---------------------------------------------------------------------------
  // Backup execution
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Writes the live Rhino document on the UI thread, then starts file-only work
  /// on a background thread. The completion callback always runs on the UI thread.
  /// </summary>
  private static bool BeginBackupDocument(
    RhinoDoc doc,
    AutoBackupSettings settings,
    bool forceBackup,
    Action<BackupResult> completed)
  {
    var docKey = DocKey(doc);
    var currentToken = GetChangeToken(doc);

    if (!forceBackup && settings.SkipIfUnchanged)
    {
      if (!_changeTokens.TryGetValue(docKey, out var lastToken))
      {
        // First time we see this document: establish baseline and skip.
        // Avoids creating a backup immediately after a doc is opened/imported.
        _changeTokens[docKey] = currentToken;
        completed(new BackupResult(
          false,
          $"AutoBackup: initial baseline captured for {DocLabel(doc)}. Waiting for changes.",
          false,
          0));
        return false;
      }

      if (lastToken == currentToken)
      {
        completed(new BackupResult(
          false,
          $"AutoBackup: no changes since last backup for {DocLabel(doc)}. Skipping.",
          false,
          0));
        return false;
      }
    }

    if (!Directory.Exists(settings.BackupRoot))
      Directory.CreateDirectory(settings.BackupRoot);

    string? unsavedName = string.IsNullOrEmpty(doc.Path) ? GetOrCreateUnsavedName(doc) : null;
    var backupPath = BuildBackupPath(doc.Path, settings.BackupRoot, unsavedName);
    var backupDir = Path.GetDirectoryName(backupPath);
    if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
      Directory.CreateDirectory(backupDir);
    var partialPath = Path.Combine(
      backupDir ?? settings.BackupRoot,
      $".vAutoBackup-{Guid.NewGuid():N}.partial.3dm");

    var writeOptions = new FileWriteOptions
    {
      UpdateDocumentPath = false,
      SuppressAllInput = true,
      SuppressDialogBoxes = true,
      WriteSelectedObjectsOnly = false
    };

    if (!doc.Write3dmFile(partialPath, writeOptions))
    {
      TryDeletePartial(partialPath);
      completed(new BackupResult(
        false,
        $"AutoBackup: backup failed: {backupPath} | doc={DocLabel(doc)} | modified={doc.Modified}",
        false,
        0));
      return false;
    }

    // Capture Rhino state while still on the UI thread. It becomes the new
    // baseline only if the temporary archive passes background verification.
    var postWriteToken = GetChangeToken(doc);
    var enableCleanup = settings.EnableCleanup;
    var keepLast = settings.KeepLast;
    EnsureCompletionIdleHandler();

    Log(
      $"AutoBackup: document written; verifying backup in background: {backupPath}",
      settings,
      verboseOnly: true);
    vAutoBackupPlugIn.TryLog(
      $"Temporary backup written. Partial={partialPath} Final={backupPath}");

    _ = Task.Run(() =>
    {
      BackupResult result;
      try
      {
        result = FinalizeBackup(partialPath, backupPath, enableCleanup, keepLast);
      }
      catch (Exception ex)
      {
        TryDeletePartial(partialPath);
        result = new BackupResult(
          false,
          $"AutoBackup: backup verification failed: {backupPath} | {CompactDiagnostic(ex.Message)}",
          false,
          0);
      }

      vAutoBackupPlugIn.TryLog(result.Message);
      _uiCompletions.Enqueue(() =>
      {
        if (result.Success)
          _changeTokens[docKey] = postWriteToken;
        completed(result);
      });
    });

    return true;
  }

  private static void EnsureCompletionIdleHandler()
  {
    if (_completionIdleHandler is not null)
      return;

    _completionIdleHandler = (_, _) =>
    {
      while (_uiCompletions.TryDequeue(out var completion))
      {
        try
        {
          completion();
        }
        catch (Exception ex)
        {
          _executing = false;
          var message = $"AutoBackup completion error: {CompactDiagnostic(ex.Message)}";
          vAutoBackupPlugIn.TryLog(message);
          RhinoApp.WriteLine(WithNextRun(message));
        }
      }
    };

    RhinoApp.Idle += _completionIdleHandler;
  }

  /// <summary>
  /// Reopens and validates the completed temporary archive before atomically
  /// promoting it to the user-visible backup path.
  /// </summary>
  private static BackupResult FinalizeBackup(
    string partialPath,
    string backupPath,
    bool enableCleanup,
    int keepLast)
  {
    try
    {
      WaitForCompletedArchive(partialPath);

      using (var model = File3dm.ReadWithLog(partialPath, out var readLog))
      {
        if (model is null)
          throw new InvalidDataException(
            $"Rhino could not reopen the temporary archive. {CompactDiagnostic(readLog)}".Trim());

        if (!string.IsNullOrWhiteSpace(readLog))
          throw new InvalidDataException(
            $"Rhino reported archive read errors. {CompactDiagnostic(readLog)}".Trim());

        if (model.ArchiveVersion <= 0)
          throw new InvalidDataException("the reopened archive has no valid 3DM version");
      }

      // Source and destination share a directory, so promotion cannot expose a
      // partially copied final file. Existing same-second backup names are replaced.
      File.Move(partialPath, backupPath, overwrite: true);

      var deleted = 0;
      if (enableCleanup && keepLast > 0)
        deleted = CleanupOldBackups(backupPath, keepLast);

      return new BackupResult(
        true,
        $"AutoBackup: backup created and verified: {backupPath}",
        false,
        deleted);
    }
    catch (Exception ex)
    {
      TryDeletePartial(partialPath);
      return new BackupResult(
        false,
        $"AutoBackup: backup verification failed: {backupPath} | {CompactDiagnostic(ex.Message)}",
        false,
        0);
    }
  }

  /// <summary>
  /// Waits until Rhino's writer has released the temporary file, then requests
  /// an OS disk flush before archive parsing begins.
  /// </summary>
  private static void WaitForCompletedArchive(string partialPath)
  {
    Exception? lastError = null;
    for (var attempt = 0; attempt < 100; attempt++)
    {
      try
      {
        using var stream = new FileStream(
          partialPath,
          FileMode.Open,
          FileAccess.ReadWrite,
          FileShare.None);

        if (stream.Length <= 0)
          throw new InvalidDataException("the temporary archive is empty");

        stream.Flush(flushToDisk: true);
        return;
      }
      catch (IOException ex)
      {
        lastError = ex;
      }
      catch (UnauthorizedAccessException ex)
      {
        lastError = ex;
      }

      Thread.Sleep(100);
    }

    throw new IOException(
      "the temporary archive did not become exclusively accessible within 10 seconds",
      lastError);
  }

  // ---------------------------------------------------------------------------
  // Change token
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Returns a lightweight token that changes when the document's edit state changes.
  /// Mirrors the Python version: (doc.Modified, doc.NextUndoRecordSerialNumber).
  /// Falls back to (Modified, 0) if Rhino cannot supply the undo serial.
  /// </summary>
  private static (bool Modified, uint UndoSerial) GetChangeToken(RhinoDoc doc)
  {
    try { return (doc.Modified, doc.NextUndoRecordSerialNumber); }
    catch { return (doc.Modified, 0); }
  }

  private static void SeedDocBaseline(RhinoDoc? doc, AutoBackupSettings settings)
  {
    if (doc is null)
      return;
    _changeTokens[DocKey(doc)] = GetChangeToken(doc);
    Log($"AutoBackup: baseline seeded for {DocLabel(doc)}.", settings, verboseOnly: true);
  }

  // ---------------------------------------------------------------------------
  // Backup path builder
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Builds the destination backup path.
  /// <para>Saved:   {backupRoot}\{Drive}\{RelDir}\{filename.3dm}_{YYYYMMDDHHMMSS}.3dm</para>
  /// <para>Unsaved: {backupRoot}\{name}_{YYYYMMDDHHMMSS}.3dm</para>
  /// </summary>
  private static string BuildBackupPath(string? sourcePath, string backupRoot, string? unsavedName)
  {
    var timestamp = DateTime.Now.ToString("yyyyMMddHHmmss");

    if (!string.IsNullOrEmpty(unsavedName))
    {
      var baseName = Path.GetFileNameWithoutExtension(unsavedName);
      if (string.IsNullOrEmpty(baseName))
        baseName = "Untitled";
      return Path.Combine(backupRoot, $"{baseName}_{timestamp}.3dm");
    }

    var fullSource = Path.GetFullPath(sourcePath!);
    var root = Path.GetPathRoot(fullSource) ?? "";
    var driveFolder = root.Replace(":", "").Replace("\\", "").Replace("/", "").ToUpperInvariant();
    if (string.IsNullOrEmpty(driveFolder))
      driveFolder = "NO_DRIVE";

    var relative = fullSource[root.Length..]; // strip drive root prefix
    var sourceDir = Path.GetDirectoryName(relative) ?? "";
    var sourceName = Path.GetFileName(fullSource);
    var destDir = Path.Combine(backupRoot, driveFolder, sourceDir);
    return Path.Combine(destDir, $"{sourceName}_{timestamp}.3dm");
  }

  // ---------------------------------------------------------------------------
  // Unsaved document naming
  // ---------------------------------------------------------------------------

  private static string GetOrCreateUnsavedName(RhinoDoc doc)
  {
    var serial = doc.RuntimeSerialNumber;
    if (_unsavedNames.TryGetValue(serial, out var cached))
      return cached;

    var name = GetDocName(doc);
    if (string.IsNullOrEmpty(name))
      name = $"Untitled_{DateTime.Now:yyyyMMddHHmmss}";

    _unsavedNames[serial] = name;
    return name;
  }

  // ---------------------------------------------------------------------------
  // Document name resolution
  // ---------------------------------------------------------------------------

  private static string GetDocName(RhinoDoc doc)
  {
    var candidates = new List<string>
    {
      doc.Name ?? "",
      GetNameFromWindowTitle()
    };

    // For unsaved imported docs, try the import history as a best-effort fallback.
    if (LooksUntitled(doc.Name) && string.IsNullOrEmpty(doc.Path))
      candidates.Add(GetNameFromImportHistory());

    foreach (var candidate in candidates)
    {
      var name = Path.GetFileName(candidate.Trim());
      if (!string.IsNullOrEmpty(name) && !LooksUntitled(name))
        return name;
    }

    return string.Empty;
  }

  private static bool LooksUntitled(string? name)
  {
    var text = (name ?? "").Trim().ToLowerInvariant();
    return string.IsNullOrEmpty(text) || text.StartsWith("untitled");
  }

  private static string GetNameFromWindowTitle()
  {
    try
    {
      var title = (Process.GetCurrentProcess().MainWindowTitle ?? "").Trim();
      if (string.IsNullOrEmpty(title))
        return string.Empty;

      // Only use the title when the document-name separator is present.
      // Without it, the title is just the app name (e.g. "Rhino 8 Commercial").
      var found = false;
      foreach (var marker in new[] { " - Rhinoceros", " - Rhino" })
      {
        var idx = title.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (idx >= 0)
        {
          title = title[..idx].Trim();
          found = true;
          break;
        }
      }

      if (!found)
        return string.Empty;

      title = title.TrimStart('*', ' ').Trim();
      return Path.GetFileName(title);
    }
    catch { return string.Empty; }
  }

  private static string GetNameFromImportHistory()
  {
    try
    {
      var history = RhinoApp.CommandHistoryWindowText ?? "";
      if (string.IsNullOrEmpty(history))
        return string.Empty;

      var matches = Regex.Matches(history,
        @"Successfully\s+read\s+file\s+""([^""]+)""",
        RegexOptions.IgnoreCase);

      if (matches.Count == 0)
        return string.Empty;

      var path = matches[^1].Groups[1].Value.Trim();
      if (string.IsNullOrEmpty(path))
        return string.Empty;

      var ext = Path.GetExtension(path).ToLowerInvariant();
      if (!new[] { ".dxf", ".dwg", ".dgn", ".ai", ".pdf" }.Contains(ext))
        return string.Empty;

      return Path.GetFileNameWithoutExtension(path) ?? string.Empty;
    }
    catch { return string.Empty; }
  }

  // ---------------------------------------------------------------------------
  // Cleanup
  // ---------------------------------------------------------------------------

  /// <summary>
  /// Deletes older timestamped backups for the same source file, keeping
  /// only the newest <paramref name="keepLast"/> entries.
  /// </summary>
  private static int CleanupOldBackups(string backupPath, int keepLast)
  {
    if (keepLast <= 0)
      return 0;

    try
    {
      var backupDir = Path.GetDirectoryName(backupPath) ?? string.Empty;
      var backupFile = Path.GetFileName(backupPath);
      var underscoreIdx = backupFile.LastIndexOf('_');
      if (underscoreIdx < 0)
        return 0;

      // Prefix = everything up to and including the last underscore before the timestamp.
      var prefix = backupFile[..(underscoreIdx + 1)];

      if (!Directory.Exists(backupDir))
        return 0;

      var matching = Directory.GetFiles(backupDir)
        .Where(f =>
        {
          var n = Path.GetFileName(f);
          return n.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)
              && n.EndsWith(".3dm", StringComparison.OrdinalIgnoreCase);
        })
        .OrderByDescending(f => Path.GetFileName(f)) // lexicographic = chronological (yyyyMMddHHmmss)
        .ToList();

      var deleted = 0;
      foreach (var old in matching.Skip(keepLast))
      {
        try { File.Delete(old); deleted++; }
        catch { }
      }

      return deleted;
    }
    catch { return 0; }
  }

  // ---------------------------------------------------------------------------
  // Helpers
  // ---------------------------------------------------------------------------

  private static void TryDeletePartial(string partialPath)
  {
    try
    {
      if (File.Exists(partialPath))
        File.Delete(partialPath);
    }
    catch { }
  }

  private static string CompactDiagnostic(string? message)
  {
    if (string.IsNullOrWhiteSpace(message))
      return "unknown error";
    return Regex.Replace(message, @"\s+", " ").Trim();
  }

  private static void DetachIdleHandler()
  {
    if (_idleHandler is not null)
    {
      try { RhinoApp.Idle -= _idleHandler; } catch { }
      _idleHandler = null;
    }
    _running = false;
  }

  private static TimeSpan IntervalSpan(AutoBackupSettings settings)
    => TimeSpan.FromMinutes(Math.Max(0.1, settings.IntervalMinutes));

  private static string DocKey(RhinoDoc doc)
    => $"{doc.RuntimeSerialNumber}|{doc.Path ?? "UNSAVED"}";

  private static string DocLabel(RhinoDoc doc)
  {
    if (!string.IsNullOrEmpty(doc.Path))
      return doc.Path;
    var name = GetDocName(doc);
    return string.IsNullOrEmpty(name) ? $"Untitled({doc.RuntimeSerialNumber})" : name;
  }

  private static string FormatRemaining(DateTime next)
  {
    var remaining = (int)Math.Max(0, (next - DateTime.Now).TotalSeconds);
    var h = remaining / 3600;
    var m = (remaining % 3600) / 60;
    var s = remaining % 60;
    var parts = new List<string>();
    if (h > 0) parts.Add($"{h} hour(s)");
    if (m > 0) parts.Add($"{m} minute(s)");
    if (s > 0 || parts.Count == 0) parts.Add($"{s} second(s)");
    return string.Join(" ", parts);
  }

  private static void Log(string message, AutoBackupSettings settings, bool verboseOnly)
  {
    if (verboseOnly && !settings.VerboseLogging)
      return;
    RhinoApp.WriteLine(message);
  }

  private static bool ReportResult(BackupResult result, AutoBackupSettings settings)
  {
    Log(WithNextRun(result.Message), settings, result.VerboseOnly);
    if (result.DeletedCount > 0)
      Log($"AutoBackup: cleanup removed {result.DeletedCount} old backup(s).", settings, verboseOnly: true);
    return result.Success;
  }

  private static string WithNextRun(string message)
  {
    if (!_running || _nextRun == DateTime.MaxValue)
      return message;

    var separator = message.EndsWith('.') || message.EndsWith('!') || message.EndsWith('?')
      ? " "
      : ". ";
    return $"{message}{separator}Next autosave scheduled at {_nextRun:HH:mm:ss}.";
  }
}
