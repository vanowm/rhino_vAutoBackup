using Rhino;
using Rhino.Commands;

namespace vAutoBackup.Commands;

/// <summary>
/// Opens the AutoBackup options menu: backup root, interval, cleanup, and verbose settings.
/// </summary>
public sealed class vAutoBackupOptions : Command
{
  public override string EnglishName => "vAutoBackupOptions";

  protected override Result RunCommand(RhinoDoc doc, RunMode mode)
  {
    return AutoBackupMonitor.ConfigureOptions() ? Result.Success : Result.Cancel;
  }
}
