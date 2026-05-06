using Rhino;
using Rhino.Commands;

namespace vAutoBackup.Commands;

/// <summary>
/// Starts the periodic backup timer using the current persisted settings.
/// </summary>
public sealed class vAutoBackupStart : Command
{
  public override string EnglishName => "vAutoBackupStart";

  protected override Result RunCommand(RhinoDoc doc, RunMode mode)
  {
    AutoBackupMonitor.Start();
    return Result.Success;
  }
}
