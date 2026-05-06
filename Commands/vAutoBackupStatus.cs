using Rhino;
using Rhino.Commands;

namespace vAutoBackup.Commands;

/// <summary>
/// Prints the current periodic backup status to the command line.
/// </summary>
public sealed class vAutoBackupStatus : Command
{
  public override string EnglishName => "vAutoBackupStatus";

  protected override Result RunCommand(RhinoDoc doc, RunMode mode)
  {
    AutoBackupMonitor.PrintStatus();
    return Result.Success;
  }
}
