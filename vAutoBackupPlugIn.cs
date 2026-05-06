using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Rhino;
using Rhino.PlugIns;

namespace vAutoBackup;

[System.Runtime.InteropServices.Guid("f8e4d2c1-3b7a-4e5f-9d0c-6a2b8e7f1c4d")]
/// <summary>
/// Rhino plug-in entry point for vAutoBackup.
/// Loads at startup and, when <c>autoStart</c> is enabled in settings, starts
/// the periodic backup timer automatically — no startup commands required.
/// </summary>
public class vAutoBackupPlugIn : PlugIn
{
  public override PlugInLoadTime LoadTime => PlugInLoadTime.AtStartup;

  protected override string LocalPlugInName => "vAutoBackup";

  public vAutoBackupPlugIn()
  {
    Instance = this;
  }

  public static vAutoBackupPlugIn Instance { get; private set; } = null!;

  protected override LoadReturnCode OnLoad(ref string errorMessage)
  {
    var version = GetType().Assembly.GetName().Version?.ToString() ?? "unknown";
    var commandNames = CollectRegisteredCommandNames();
    TryLog($"OnLoad OK. Version={version}. Assembly={GetType().Assembly.Location}");
    RhinoApp.WriteLine($"vAutoBackup v{version} loaded. Commands: {string.Join(", ", commandNames)}");

    var settings = AutoBackupSettings.Current;
    if (settings.AutoStart)
      AutoBackupMonitor.Start(printMessage: true);

    return LoadReturnCode.Success;
  }

  // ---------------------------------------------------------------------------

  private static List<string> CollectRegisteredCommandNames()
  {
    var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    try
    {
      var commandTypes = typeof(vAutoBackupPlugIn)
        .Assembly
        .GetTypes()
        .Where(t => t is { IsClass: true, IsAbstract: false }
                 && typeof(Rhino.Commands.Command).IsAssignableFrom(t));

      foreach (var t in commandTypes)
      {
        try
        {
          if (Activator.CreateInstance(t) is Rhino.Commands.Command cmd)
          {
            var name = (cmd.EnglishName ?? string.Empty).Trim();
            if (!string.IsNullOrEmpty(name))
              names.Add(name);
          }
        }
        catch { }
      }
    }
    catch { }

    return names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList();
  }

  internal static void TryLog(string message)
  {
    try
    {
      var logDir = ResolveProjectLogsDir();
      if (string.IsNullOrWhiteSpace(logDir))
        return;

      Directory.CreateDirectory(logDir);
      var path = Path.Combine(logDir, "vAutoBackup.log");
      File.AppendAllText(path, $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss}] {message}\n");
    }
    catch { }
  }

  private static string ResolveProjectLogsDir()
  {
    try
    {
      var asmDir = Path.GetDirectoryName(typeof(vAutoBackupPlugIn).Assembly.Location) ?? ".";
      var dir = new DirectoryInfo(asmDir);

      while (dir is not null)
      {
        if (File.Exists(Path.Combine(dir.FullName, "vAutoBackup.csproj")))
          return Path.Combine(dir.FullName, "logs");

        dir = dir.Parent;
      }

      return Path.Combine(asmDir, "logs");
    }
    catch { return string.Empty; }
  }
}
