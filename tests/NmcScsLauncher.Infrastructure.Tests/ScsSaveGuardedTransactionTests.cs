using System.Security.Cryptography;
using System.Text;
using NmcScsLauncher.Core;
using NmcScsLauncher.Infrastructure;
using Xunit;

namespace NmcScsLauncher.Infrastructure.Tests;

public sealed class ScsSaveGuardedTransactionTests : IDisposable
{
    private const string Original = "SiiNunit\n{\n value: 1\n}\n";
    private const string Updated = "SiiNunit\n{\n value: 2\n}\n";
    private const string External = "SiiNunit\n{\n value: 3\n}\n";
    private readonly string _root = Path.Combine(Path.GetTempPath(), "nmc-guarded-save-tests", Guid.NewGuid().ToString("N"));
    private readonly string _target;
    private readonly string _backups;
    private readonly MutableGuard _guard = new();

    public ScsSaveGuardedTransactionTests()
    {
        Directory.CreateDirectory(_root);
        _target = Path.Combine(_root, "game.sii");
        _backups = Path.Combine(_root, "backups");
        File.WriteAllText(_target, Original, new UTF8Encoding(false));
    }

    [Fact]
    public async Task SuccessUsesFreshActualBytesAndOneWrittenValidationWithMatchingBackup()
    {
        var beforeCalls = 0;
        var writtenCalls = 0;
        var request = Request(before: _ => { beforeCalls++; Assert.Equal(Original, File.ReadAllText(_target)); return Task.CompletedTask; },
            written: (content, _) => { writtenCalls++; Assert.Equal(Updated, content); Assert.Equal(content, File.ReadAllText(_target)); return Task.CompletedTask; });
        var result = await Service(new RejectCodec()).ApplyAsync(request);
        Assert.Equal(2, beforeCalls);
        Assert.Equal(1, writtenCalls);
        Assert.Equal(Updated, File.ReadAllText(_target));
        Assert.Equal(Original, File.ReadAllText(result.BackupPath));
        Assert.Equal(Hash(Original), result.OriginalSha256, ignoreCase: true);
        Assert.Equal(Hash(Updated), result.UpdatedSha256, ignoreCase: true);
        Assert.False(File.ReadAllBytes(_target).AsSpan().StartsWith(Encoding.UTF8.Preamble));
        Assert.Empty(Directory.GetFiles(_root, ".nmc-save-*.tmp"));
    }

    [Fact]
    public async Task GuardedRequestCannotBypassStoppedGameRuleWithLiveWriteFlag()
    {
        _guard.Running = true;
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request() with
        { WriteMode = ScsSaveWriteMode.AllowWhileGameRunning, ExpectedLastWriteTimeUtc = File.GetLastWriteTimeUtc(_target) }));
        Assert.Equal(Original, File.ReadAllText(_target));
        Assert.False(Directory.Exists(_backups));
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LateGameStartBeforeBackupOrReplacePreventsWriting(int startAt)
    {
        var calls = 0;
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(before: _ =>
        { if (++calls == startAt) _guard.Running = true; return Task.CompletedTask; })));
        Assert.Equal(Original, File.ReadAllText(_target));
        Assert.Equal(startAt == 1 ? 0 : 1, BackupFiles().Length);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public async Task LateSourceFailureBeforeBackupOrReplacePreventsWriting(int failAt)
    {
        var calls = 0;
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(before: _ =>
        { if (++calls == failAt) throw new ScsSaveEditException("Source changed"); return Task.CompletedTask; })));
        Assert.Equal(Original, File.ReadAllText(_target));
        Assert.Equal(failAt == 1 ? 0 : 1, BackupFiles().Length);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    public async Task SameLengthAndTimestampChangesAreRejectedAtEveryOriginalBoundary(int changeAt)
    {
        var timestamp = File.GetLastWriteTimeUtc(_target);
        void Change() { File.WriteAllText(_target, External); File.SetLastWriteTimeUtc(_target, timestamp); }
        var calls = 0;
        var request = Request(before: _ => { if (++calls == changeAt) Change(); return Task.CompletedTask; });
        if (changeAt == 0) Change();
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(request));
        Assert.Equal(External, File.ReadAllText(_target));
        Assert.Equal(timestamp, File.GetLastWriteTimeUtc(_target));
    }

    [Fact]
    public async Task ModifiedBackupCannotAuthorizeCommit()
    {
        var calls = 0;
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(before: _ =>
        { if (++calls == 2) File.WriteAllText(Assert.Single(BackupFiles()), External); return Task.CompletedTask; })));
        Assert.Equal(Original, File.ReadAllText(_target));
    }

    [Fact]
    public async Task PostWriteSemanticFailureRestoresOriginalAndKeepsVerifiedBackup()
    {
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(written: (_, _) =>
            throw new ScsSaveEditException("Written graph rejected"))));
        Assert.Equal(Original, File.ReadAllText(_target));
        Assert.Equal(Original, File.ReadAllText(Assert.Single(BackupFiles())));
        Assert.Empty(Directory.GetFiles(_root, ".nmc-save-rollback-*.tmp"));
    }

    [Fact]
    public async Task CancellationAfterReplaceStillRestoresOriginalWithUncancelledRecovery()
    {
        using var cancellation = new CancellationTokenSource();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => Service().ApplyAsync(Request(written: (_, _) =>
        { cancellation.Cancel(); return Task.CompletedTask; }), cancellation.Token));
        Assert.Equal(Original, File.ReadAllText(_target));
        Assert.Equal(Original, File.ReadAllText(Assert.Single(BackupFiles())));
    }

    [Fact]
    public async Task GameStartingDuringPostValidationRollsBack()
    {
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(written: (_, _) =>
        { _guard.Running = true; return Task.CompletedTask; })));
        Assert.Equal(Original, File.ReadAllText(_target));
    }

    [Fact]
    public async Task ExternalSaveAfterReplaceIsPreservedAlongWithRecoveryArtifacts()
    {
        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(written: (_, _) =>
        { File.WriteAllText(_target, External); return Task.CompletedTask; })));
        Assert.Equal(External, File.ReadAllText(_target));
        var rollback = Assert.Single(Directory.GetFiles(_root, ".nmc-save-rollback-*.tmp"));
        var backup = Assert.Single(BackupFiles());
        Assert.Equal(Original, File.ReadAllText(rollback));
        Assert.Equal(Original, File.ReadAllText(backup));
        Assert.Contains(rollback, exception.Message);
        Assert.Contains(backup, exception.Message);
    }

    [Fact]
    public async Task AlteredRollbackIsNeverRestoredAndIsNotDeleted()
    {
        var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(written: (_, _) =>
        {
            File.WriteAllText(Assert.Single(Directory.GetFiles(_root, ".nmc-save-rollback-*.tmp")), External);
            throw new ScsSaveEditException("Source changed");
        })));
        Assert.Equal(Updated, File.ReadAllText(_target));
        var rollback = Assert.Single(Directory.GetFiles(_root, ".nmc-save-rollback-*.tmp"));
        Assert.Contains(rollback, exception.Message);
        Assert.Equal(Original, File.ReadAllText(Assert.Single(BackupFiles())));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task OpaqueBinaryOriginalIsPreservedExactlyInBackupAndRollback(bool rejectWritten)
    {
        byte[] original = [0x53, 0x63, 0x73, 0x43, 0, 0xff, 0xfe, 0x80, 0x41, 0, 0];
        File.WriteAllBytes(_target, original);
        var request = Request(written: (_, _) => rejectWritten ? throw new ScsSaveEditException("Postvalidation rejected") : Task.CompletedTask);
        request = request with { TransactionValidation = request.TransactionValidation! with { ExpectedOriginalSha256 = Convert.ToHexString(SHA256.HashData(original)) } };
        if (rejectWritten)
        {
            await Assert.ThrowsAsync<ScsSaveEditException>(() => Service(new RejectCodec()).ApplyAsync(request));
            Assert.Equal(original, File.ReadAllBytes(_target));
        }
        else
        {
            await Service(new RejectCodec()).ApplyAsync(request);
            Assert.Equal(Updated, File.ReadAllText(_target));
        }
        Assert.Equal(original, File.ReadAllBytes(Assert.Single(BackupFiles())));
    }

    [Fact]
    public async Task LastWindowRaceRestoresActualCapturedPreimageInsteadOfStaleBackup()
    {
        var beforeCalls = 0;
        var preReplaceChecks = 0;
        _guard.OnCheck = () =>
        {
            if (beforeCalls == 2 && ++preReplaceChecks == 2) File.WriteAllText(_target, External);
        };
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(before: _ =>
        { beforeCalls++; return Task.CompletedTask; })));
        Assert.Equal(External, File.ReadAllText(_target));
        Assert.Equal(Original, File.ReadAllText(Assert.Single(BackupFiles())));
    }

    [Fact]
    public async Task LockedTargetKeepsRecoveryFilesAndActionablePaths()
    {
        FileStream? lease = null;
        try
        {
            var exception = await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(written: (_, _) =>
            {
                lease = new FileStream(_target, FileMode.Open, FileAccess.Read, FileShare.None);
                throw new ScsSaveEditException("Postvalidation rejected");
            })));
            Assert.Contains(Assert.Single(Directory.GetFiles(_root, ".nmc-save-rollback-*.tmp")), exception.Message);
            Assert.Contains(Assert.Single(BackupFiles()), exception.Message);
        }
        finally { lease?.Dispose(); }
        Assert.Equal(Updated, File.ReadAllText(_target));
    }

    [Theory]
    [InlineData("227300")]
    [InlineData("270880")]
    public async Task SteamUserdataPathCannotBypassBackendByCallingTransactionDirectly(string appId)
    {
        var directory = Path.Combine(_root, "Steam", "userdata", "123", appId, "remote", "profiles", "TEST", "save", "1");
        Directory.CreateDirectory(directory);
        var target = Path.Combine(directory, "game.sii");
        File.WriteAllText(target, Original);
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request() with { FilePath = target }));
        Assert.Equal(Original, File.ReadAllText(target));
        Assert.False(Directory.Exists(_backups));
    }

    [Fact]
    public async Task ReparseAncestorIsRejectedBeforeCallbacksOrBackups()
    {
        var link = Path.Combine(_root, "link");
        var real = Path.Combine(_root, "real");
        Directory.CreateDirectory(real);
        File.WriteAllText(Path.Combine(real, "game.sii"), Original);
        Directory.CreateSymbolicLink(link, real);
        var calls = 0;
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(Request(before: _ =>
        { calls++; return Task.CompletedTask; }) with { FilePath = Path.Combine(link, "game.sii") }));
        Assert.Equal(0, calls);
        Assert.False(Directory.Exists(_backups));
    }

    [Fact]
    public async Task InvalidUtf16InputCannotBeSilentlyReplacedByUtf8ReplacementCharacters()
    {
        var request = Request() with { UpdatedContent = "SiiNunit\n{\n name: \"\ud800\"\n}\n" };
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(request));
        Assert.Equal(Original, File.ReadAllText(_target));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-a-sha")]
    [InlineData("0000000000000000000000000000000000000000000000000000000000000000")]
    public async Task MissingMalformedOrWrongOriginalHashFailsBeforeBackup(string expected)
    {
        var request = Request();
        request = request with { TransactionValidation = request.TransactionValidation! with { ExpectedOriginalSha256 = expected } };
        await Assert.ThrowsAsync<ScsSaveEditException>(() => Service().ApplyAsync(request));
        Assert.Equal(Original, File.ReadAllText(_target));
        Assert.False(Directory.Exists(_backups));
    }

    private ScsSaveEditRequest Request(Func<CancellationToken, Task>? before = null, Func<string, CancellationToken, Task>? written = null) =>
        new(_target, "Verified synthetic edit", Updated, TransactionValidation: new(Hash(Original).ToUpperInvariant(),
            before ?? (_ => Task.CompletedTask), written ?? ((_, _) => Task.CompletedTask)));
    private ScsSaveEditService Service(IScsSaveCodec? codec = null) => new(codec ?? new ScsPlainTextSaveCodec(), _guard, _backups);
    private string[] BackupFiles() => Directory.Exists(_backups) ? Directory.GetFiles(_backups, "game.sii", SearchOption.AllDirectories) : [];
    private static string Hash(string content) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
    private sealed class MutableGuard : IScsGameProcessGuard
    {
        internal bool Running;
        internal Action? OnCheck;
        public bool IsAnyScsGameRunning() { OnCheck?.Invoke(); return Running; }
    }
    private sealed class RejectCodec : IScsSaveCodec
    {
        public Task<ScsSaveDocument> ReadAsync(string filePath, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("Guarded byte validation must not read through a cached codec.");
    }
}
