using System.IO.Compression;
using System.Text;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class UpdatePackageApplierTests
{
    [Fact]
    public async Task ApplyReplacesTargetAndKeepsRollbackBackup()
    {
        var root = CreateRoot();
        try
        {
            var target = Path.Combine(root, "app");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "NmcScsLauncher.App.exe"), "old");
            await File.WriteAllTextAsync(Path.Combine(target, "obsolete.txt"), "old-only");

            var package = Path.Combine(root, "update.zip");
            CreateZip(package,
                ("NmcScsLauncher.App.exe", "new"),
                ("new.txt", "new-only"));

            var result = await new UpdatePackageApplier().ApplyAsync(
                package,
                target,
                "NmcScsLauncher.App.exe");

            Assert.Equal("new", await File.ReadAllTextAsync(Path.Combine(target, "NmcScsLauncher.App.exe")));
            Assert.True(File.Exists(Path.Combine(target, "new.txt")));
            Assert.False(File.Exists(Path.Combine(target, "obsolete.txt")));
            Assert.True(Directory.Exists(result.BackupDirectory));
            Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(result.BackupDirectory, "NmcScsLauncher.App.exe")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task ApplyRejectsZipTraversalBeforeMovingTarget()
    {
        var root = CreateRoot();
        try
        {
            var target = Path.Combine(root, "app");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "NmcScsLauncher.App.exe"), "old");
            var package = Path.Combine(root, "update.zip");

            using (var archive = ZipFile.Open(package, ZipArchiveMode.Create))
            {
                var entry = archive.CreateEntry("../escape.txt");
                await using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
                await writer.WriteAsync("escape");
            }

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                new UpdatePackageApplier().ApplyAsync(package, target, "NmcScsLauncher.App.exe"));

            Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(target, "NmcScsLauncher.App.exe")));
            Assert.False(File.Exists(Path.Combine(root, "escape.txt")));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    [Fact]
    public async Task RollbackRestoresOriginalDirectory()
    {
        var root = CreateRoot();
        try
        {
            var target = Path.Combine(root, "app");
            Directory.CreateDirectory(target);
            await File.WriteAllTextAsync(Path.Combine(target, "NmcScsLauncher.App.exe"), "old");
            var package = Path.Combine(root, "update.zip");
            CreateZip(package, ("NmcScsLauncher.App.exe", "new"));

            var applier = new UpdatePackageApplier();
            var result = await applier.ApplyAsync(package, target, "NmcScsLauncher.App.exe");
            await applier.RollbackAsync(result);

            Assert.Equal("old", await File.ReadAllTextAsync(Path.Combine(target, "NmcScsLauncher.App.exe")));
            Assert.False(Directory.Exists(result.BackupDirectory));
        }
        finally
        {
            DeleteRoot(root);
        }
    }

    private static string CreateRoot()
    {
        var root = Path.Combine(Path.GetTempPath(), "NmcScsLauncherUpdaterTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        return root;
    }

    private static void CreateZip(string path, params (string Name, string Content)[] entries)
    {
        using var archive = ZipFile.Open(path, ZipArchiveMode.Create);
        foreach (var (name, content) in entries)
        {
            var entry = archive.CreateEntry(name);
            using var writer = new StreamWriter(entry.Open(), Encoding.UTF8);
            writer.Write(content);
        }
    }

    private static void DeleteRoot(string root)
    {
        if (!Directory.Exists(root)) return;
        try
        {
            Directory.Delete(root, recursive: true);
        }
        catch
        {
            // Test cleanup must not hide assertions.
        }
    }
}
