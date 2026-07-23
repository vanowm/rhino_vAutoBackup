using System;
using System.IO;

namespace vAutoBackup;

/// <summary>
/// Resolves runtime files strictly beside the loaded plug-in assembly.
/// </summary>
internal static class PluginPaths
{
  public static string DirectoryPath
  {
    get
    {
      var directory = Path.GetDirectoryName(typeof(PluginPaths).Assembly.Location);
      if (string.IsNullOrWhiteSpace(directory))
        throw new InvalidOperationException("The vAutoBackup assembly directory is unavailable.");
      return directory;
    }
  }

  public static string ResolveFile(string fileName)
  {
    if (string.IsNullOrWhiteSpace(fileName) ||
        Path.IsPathRooted(fileName) ||
        !string.Equals(Path.GetFileName(fileName), fileName, StringComparison.Ordinal))
      throw new ArgumentException("A deployment-relative file name is required.", nameof(fileName));

    return Path.Combine(DirectoryPath, fileName);
  }
}