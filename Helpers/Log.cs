using System;
using System.IO;

namespace vAutoBackup;

/// <summary>
/// Append-only session log shared by the plug-in's commands and helpers.
/// </summary>
internal static class Log
{
  private static string? _path;
  private static readonly object SyncRoot = new();

  public static string? FilePath => _path;

  public static void Initialize()
  {
    try
    {
      lock (SyncRoot)
      {
        _path = ResolvePath();
        if (!string.IsNullOrEmpty(_path))
          File.WriteAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] vAutoBackup log initialized\n");
      }
    }
    catch { }
  }

  public static void Write(string message)
    => Append(message);

  public static void Write(string tag, string message)
    => Append($"[{tag}] {message}");

  public static void Write(string tag, string format, params object[] args)
    => Append($"[{tag}] {string.Format(format, args)}");

  private static void Append(string text)
  {
    try
    {
      lock (SyncRoot)
      {
        _path ??= ResolvePath();
        if (string.IsNullOrEmpty(_path)) return;
        File.AppendAllText(_path, $"[{DateTime.Now:HH:mm:ss.fff}] {text}\n");
      }
    }
    catch { }
  }

  private static string ResolvePath()
  {
    try
    {
      return PluginPaths.ResolveFile("vAutoBackup.log");
    }
    catch { return string.Empty; }
  }
}