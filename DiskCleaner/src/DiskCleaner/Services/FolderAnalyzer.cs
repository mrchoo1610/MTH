using System.Security;
using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

public sealed class FolderAnalyzer
{
    private sealed class Counters
    {
        public long Files;
        public long Bytes;
    }

    /// <summary>
    /// Quét cây thư mục, size gộp từ dưới lên. Không đi vào reparse point.
    /// maxDepth: chỉ GIỮ node con tới độ sâu này (size vẫn được tính đủ) để tiết kiệm bộ nhớ khi quét cả ổ.
    /// </summary>
    public Task<FolderNode> AnalyzeAsync(string rootPath, IProgress<AnalyzeProgress>? progress,
        CancellationToken ct, int maxDepth = int.MaxValue) =>
        Task.Run(async () =>
        {
            var counters = new Counters();
            var root = new FolderNode { Name = GetName(rootPath), FullPath = rootPath };
            var subDirs = new List<string>();
            ScanOwn(root, counters, progress, ct, subDirs, retain: maxDepth > 0);

            var options = new ParallelOptions { MaxDegreeOfParallelism = Environment.ProcessorCount, CancellationToken = ct };
            await Parallel.ForEachAsync(subDirs, options, (dir, token) =>
            {
                var node = Build(dir, 1, maxDepth, counters, progress, token);
                lock (root)
                {
                    root.SizeBytes += node.SizeBytes;
                    root.FileCount += node.FileCount;
                    if (maxDepth > 0) root.Children.Add(node);
                }
                return ValueTask.CompletedTask;
            }).ConfigureAwait(false);

            root.Children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
            progress?.Report(new AnalyzeProgress(rootPath, Interlocked.Read(ref counters.Files), Interlocked.Read(ref counters.Bytes)));
            return root;
        }, ct);

    private static string GetName(string path)
    {
        var trimmed = path.TrimEnd('\\', '/');
        var name = Path.GetFileName(trimmed);
        return string.IsNullOrEmpty(name) ? path : name;
    }

    private FolderNode Build(string path, int depth, int maxDepth, Counters counters,
        IProgress<AnalyzeProgress>? progress, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var node = new FolderNode { Name = GetName(path), FullPath = path };
        var subDirs = new List<string>();
        ScanOwn(node, counters, progress, ct, subDirs, retain: depth < maxDepth);

        foreach (var sub in subDirs)
        {
            var child = Build(sub, depth + 1, maxDepth, counters, progress, ct);
            node.SizeBytes += child.SizeBytes;
            node.FileCount += child.FileCount;
            if (depth < maxDepth) node.Children.Add(child);
        }
        if (node.Children.Count > 1) node.Children.Sort((a, b) => b.SizeBytes.CompareTo(a.SizeBytes));
        return node;
    }

    /// <summary>Đếm file trực tiếp của thư mục, gom danh sách thư mục con (bỏ reparse point).</summary>
    private static void ScanOwn(FolderNode node, Counters counters, IProgress<AnalyzeProgress>? progress,
        CancellationToken ct, List<string> subDirs, bool retain)
    {
        var options = new EnumerationOptions
        {
            RecurseSubdirectories = false,
            IgnoreInaccessible = false,
            AttributesToSkip = 0,
            ReturnSpecialDirectories = false
        };
        try
        {
            foreach (var e in new DirectoryInfo(node.FullPath).EnumerateFileSystemInfos("*", options))
            {
                if ((e.Attributes & FileAttributes.ReparsePoint) != 0) continue;
                if ((e.Attributes & FileAttributes.Directory) != 0)
                {
                    subDirs.Add(e.FullName);
                    continue;
                }
                long len;
                try { len = ((FileInfo)e).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }

                node.SizeBytes += len;
                node.FileCount++;
                var files = Interlocked.Increment(ref counters.Files);
                Interlocked.Add(ref counters.Bytes, len);
                if (files % 2000 == 0)
                {
                    ct.ThrowIfCancellationRequested();
                    progress?.Report(new AnalyzeProgress(node.FullPath, files, Interlocked.Read(ref counters.Bytes)));
                }
            }
        }
        catch (Exception ex) when (ex is UnauthorizedAccessException or SecurityException)
        {
            node.IsAccessDenied = true;
        }
        catch (Exception ex) when (ex is IOException)
        {
            node.IsAccessDenied = true;
        }
    }
}
