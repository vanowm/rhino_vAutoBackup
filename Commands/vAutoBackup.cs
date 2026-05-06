using Rhino;
using Rhino.Commands;

namespace vAutoBackup.Commands;

/// <summary>
/// Interactive AutoBackup command: presents a menu of OneShot / Start / Stop / Status / Options.
/// Options re-enters the menu loop; all other actions execute and return.
/// </summary>
public sealed class vAutoBackup : Command
{
  public override string EnglishName => "vAutoBackup";

  protected override Result RunCommand(RhinoDoc doc, RunMode mode)
  {
    while (true)
    {
      var go = new Rhino.Input.Custom.GetOption();
      go.SetCommandPrompt("AutoBackup");
      go.AcceptNothing(false);

      go.AddOption("OneShot");
      go.AddOption("Start");
      go.AddOption("Stop");
      go.AddOption("Status");
      go.AddOption("Options");

      var res = go.Get();

      if (res == Rhino.Input.GetResult.Cancel)
        return Result.Cancel;
      if (res != Rhino.Input.GetResult.Option)
        return Result.Nothing;

      var name = go.Option().EnglishName;

      switch (name)
      {
        case "OneShot":
          AutoBackupMonitor.BackupNow();
          return Result.Success;

        case "Start":
          AutoBackupMonitor.Start();
          return Result.Success;

        case "Stop":
          AutoBackupMonitor.Stop();
          return Result.Success;

        case "Status":
          AutoBackupMonitor.PrintStatus();
          return Result.Success;

        case "Options":
          AutoBackupMonitor.ConfigureOptions();
          continue; // re-enter the menu after options
      }
    }
  }
}
