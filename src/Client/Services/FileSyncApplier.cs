using Shared;
using System.IO;

namespace Client;

public static class FileSyncApplier
{
    public static void Apply(string root, FileSyncMessage msg)
    {
        var full = Path.Combine(root, msg.RelativePath);
        var dir = Path.GetDirectoryName(full)!;
        Directory.CreateDirectory(dir);
        switch (msg.Operation)
        {
            case FileSyncOp.Create:
            case FileSyncOp.Update:
                if (msg.Base64Content == null) return;
                var bytes = Convert.FromBase64String(msg.Base64Content);
                File.WriteAllBytes(full, bytes);
                break;
            case FileSyncOp.Delete:
                if (File.Exists(full)) File.Delete(full);
                break;
        }
    }

    // New mode: only overwrite files that already exist locally; ignore creates for non-existent paths
    public static void ApplyOverwriteExistingOnly(string root, FileSyncMessage msg)
    {
        var full = Path.Combine(root, msg.RelativePath);
        var dir = Path.GetDirectoryName(full)!;
        switch (msg.Operation)
        {
            case FileSyncOp.Create:
            case FileSyncOp.Update:
                if (!File.Exists(full)) return; // only accept if file already exists
                if (msg.Base64Content == null) return;
                Directory.CreateDirectory(dir);
                var bytes = Convert.FromBase64String(msg.Base64Content);
                File.WriteAllBytes(full, bytes);
                break;
            case FileSyncOp.Delete:
                if (File.Exists(full)) File.Delete(full);
                break;
        }
    }
}
