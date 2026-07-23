using Rhino;
using Rhino.Commands;

namespace vAutoBackup.Commands;

/// <summary>
/// Starts a single timestamped backup of the active document immediately,
/// bypassing the change-detection skip filter. Archive verification and finalization
/// continue asynchronously after the Rhino document write completes.
/// </summary>
public sealed class vAutoBackupOneShot : Command
{
  public override string EnglishName => "vAutoBackupOneShot";

  protected override Result RunCommand(RhinoDoc doc, RunMode mode)
  {
    return AutoBackupMonitor.BackupNow() ? Result.Success : Result.Failure;
  }
}
