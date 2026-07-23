using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace vAutoBackup;

internal enum AutoBackupLogLevel
{
  Errors = 0,
  Info = 1,
  Verbose = 2
}

/// <summary>
/// Typed settings for vAutoBackup, persisted to vAutoBackup.config.json next to the assembly.
/// </summary>
internal sealed class AutoBackupSettings
{
  private const string ConfigFileName = "vAutoBackup.config.json";
  private static readonly object Sync = new();

  internal const string DefaultBackupRoot = @"D:\Backup\Rhino";
  internal const double DefaultIntervalMinutes = 10.0;
  internal const bool DefaultEnableCleanup = true;
  internal const int DefaultKeepLast = 100;
  internal const bool DefaultSkipIfUnchanged = true;
  internal const AutoBackupLogLevel DefaultLogLevel = AutoBackupLogLevel.Info;
  internal const bool DefaultAutoStart = true;

  public string BackupRoot { get; set; } = DefaultBackupRoot;
  public double IntervalMinutes { get; set; } = DefaultIntervalMinutes;
  public bool EnableCleanup { get; set; } = DefaultEnableCleanup;
  public int KeepLast { get; set; } = DefaultKeepLast;
  public bool SkipIfUnchanged { get; set; } = DefaultSkipIfUnchanged;
  public AutoBackupLogLevel LogLevel { get; set; } = DefaultLogLevel;
  /// <summary>When true the backup timer is started automatically when the plug-in loads.</summary>
  public bool AutoStart { get; set; } = DefaultAutoStart;

  private static AutoBackupSettings? _current;

  /// <summary>Gets the currently loaded settings instance (loaded once on first access).</summary>
  internal static AutoBackupSettings Current
  {
    get
    {
      lock (Sync)
      {
        return _current ??= Load();
      }
    }
  }

  /// <summary>Saves the current state of this instance to disk and refreshes the shared <see cref="Current"/> reference.</summary>
  internal bool Save()
  {
    lock (Sync)
    {
      try
      {
        var path = GetConfigPath();
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir))
          Directory.CreateDirectory(dir);

        var root = new JsonObject
        {
          ["backupRoot"] = BackupRoot,
          ["intervalMinutes"] = IntervalMinutes,
          ["enableCleanup"] = EnableCleanup,
          ["keepLast"] = KeepLast,
          ["skipIfUnchanged"] = SkipIfUnchanged,
          ["logLevel"] = LogLevel.ToString(),
          ["autoStart"] = AutoStart
        };

        var opts = new JsonSerializerOptions { WriteIndented = true };
        var tmp = path + ".tmp";
        File.WriteAllText(tmp, root.ToJsonString(opts));
        File.Copy(tmp, path, overwrite: true);
        try { File.Delete(tmp); } catch { }

        // Refresh singleton so subsequent reads see the new values.
        _current = this;
        return true;
      }
      catch
      {
        return false;
      }
    }
  }

  // ---------------------------------------------------------------------------

  private static AutoBackupSettings Load()
  {
    var s = new AutoBackupSettings();
    try
    {
      var path = GetConfigPath();
      if (!File.Exists(path))
        return s;

      var json = File.ReadAllText(path);
      if (string.IsNullOrWhiteSpace(json))
        return s;

      var root = JsonNode.Parse(json, null, new JsonDocumentOptions
      {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip
      }) as JsonObject;

      if (root is null)
        return s;

      if (TryGetString(root, "backupRoot", out var br)) s.BackupRoot = br;
      if (TryGetDouble(root, "intervalMinutes", out var im)) s.IntervalMinutes = im;
      if (TryGetBool(root, "enableCleanup", out var ec)) s.EnableCleanup = ec;
      if (TryGetInt(root, "keepLast", out var kl)) s.KeepLast = kl;
      if (TryGetBool(root, "skipIfUnchanged", out var su)) s.SkipIfUnchanged = su;
      if (TryGetLogLevel(root, "logLevel", out var level))
        s.LogLevel = level;
      else if (TryGetBool(root, "verboseLogging", out var legacyVerbose))
        s.LogLevel = legacyVerbose ? AutoBackupLogLevel.Verbose : AutoBackupLogLevel.Info;
      if (TryGetBool(root, "autoStart", out var as_)) s.AutoStart = as_;
    }
    catch { }
    return s;
  }

  private static string GetConfigPath()
  {
    var pluginDir = Path.GetDirectoryName(typeof(AutoBackupSettings).Assembly.Location) ?? ".";
    return Path.Combine(pluginDir, ConfigFileName);
  }

  private static bool TryGetString(JsonObject? obj, string key, out string value)
  {
    value = string.Empty;
    try
    {
      if (obj?[key] is JsonValue jv && jv.TryGetValue<string>(out var s) && !string.IsNullOrWhiteSpace(s))
      {
        value = s.Trim();
        return true;
      }
    }
    catch { }
    return false;
  }

  private static bool TryGetBool(JsonObject? obj, string key, out bool value)
  {
    value = false;
    try
    {
      if (obj?[key] is JsonValue jv)
      {
        if (jv.TryGetValue<bool>(out var b)) { value = b; return true; }
        if (jv.TryGetValue<int>(out var i)) { value = i != 0; return true; }
      }
    }
    catch { }
    return false;
  }

  private static bool TryGetLogLevel(JsonObject? obj, string key, out AutoBackupLogLevel value)
  {
    value = DefaultLogLevel;
    try
    {
      if (obj?[key] is JsonValue jv &&
          jv.TryGetValue<string>(out var text) &&
          Enum.TryParse(text, ignoreCase: true, out AutoBackupLogLevel parsed) &&
          Enum.IsDefined(parsed))
      {
        value = parsed;
        return true;
      }
    }
    catch { }
    return false;
  }

  private static bool TryGetDouble(JsonObject? obj, string key, out double value)
  {
    value = 0.0;
    try
    {
      if (obj?[key] is JsonValue jv)
      {
        if (jv.TryGetValue<double>(out var d)) { value = d; return true; }
        if (jv.TryGetValue<int>(out var i)) { value = i; return true; }
      }
    }
    catch { }
    return false;
  }

  private static bool TryGetInt(JsonObject? obj, string key, out int value)
  {
    value = 0;
    try
    {
      if (obj?[key] is JsonValue jv)
      {
        if (jv.TryGetValue<int>(out var i)) { value = i; return true; }
        if (jv.TryGetValue<double>(out var d)) { value = (int)d; return true; }
      }
    }
    catch { }
    return false;
  }
}
