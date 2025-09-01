using System;
using System.IO;
using Client;
using Shared;
using Xunit;

namespace Client.Tests;

public class FileSyncApplierTests
{
    [Fact]
    public void Apply_Create_WritesFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "cocodex-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            var content = "hello";
            var msg = new FileSyncMessage("sender", "subdir/a.txt", FileSyncOp.Create, Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(content)));
            FileSyncApplier.Apply(root, msg);
            var text = File.ReadAllText(Path.Combine(root, "subdir", "a.txt"));
            Assert.Equal(content, text);
        }
        finally
        {
            Directory.Delete(root, true);
        }
    }
}

