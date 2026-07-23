using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Rhino;
using Rhino.PlugIns;

namespace vAutoBackup;

/// <summary>
/// Rhino plug-in entry point for vAutoBackup.
/// Loads at startup and, when <c>autoStart</c> is enabled in settings, starts
/// the periodic backup timer automatically — no startup commands required.
/// </summary>
[System.Runtime.InteropServices.Guid("f8e4d2c1-3b7a-4e5f-9d0c-6a2b8e7f1c4d")]
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
    var asm = GetType().Assembly;
    var version = (!string.IsNullOrEmpty(asm.Location)
      ? System.Diagnostics.FileVersionInfo.GetVersionInfo(asm.Location).FileVersion
      : null) ?? asm.GetName().Version?.ToString() ?? "unknown";
    var commandNames = CollectRegisteredCommandNames();
    var settings = AutoBackupSettings.Current;
    Log.Initialize();
    Log.Write($"startup  rhino={RhinoApp.Version}  version={version}  dll={asm.Location}");
    Log.Write($"startup  commands ({commandNames.Count}): {string.Join(", ", commandNames)}");
    if (settings.LogLevel >= AutoBackupLogLevel.Info)
      RhinoApp.WriteLine($"vAutoBackup v{version} loaded. Commands: {string.Join(", ", commandNames)}");

    if (settings.AutoStart)
      AutoBackupMonitor.Start();

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
}
