using Rhino;
using Rhino.Commands;

namespace vAutoBackup.Commands;

/// <summary>
/// Stops the periodic backup timer.
/// </summary>
public sealed class vAutoBackupStop : Command
{
  public override string EnglishName => "vAutoBackupStop";

  protected override Result RunCommand(RhinoDoc doc, RunMode mode)
  {
    AutoBackupMonitor.Stop();
    return Result.Success;
  }
}
