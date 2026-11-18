using DiskCleaner.Core.Models;

namespace DiskCleaner.Services;

public sealed class LargeFileFinder
{
    private const FileAttributes RecallOnOpen = (FileAttributes)0x40000;
    private const FileAttributes RecallOnDataAccess = (FileAttributes)0x400000;

    /// <summary>Top N file lớn nhất (>= minBytes), giảm dần theo size. Dùng min-heap, không đi vào reparse point.</summary>
    public Task<IReadOnlyList<LargeFileEntry>> FindAsync(string root, long minBytes, int maxResults, CancellationToken ct) =>
        Task.Run<IReadOnlyList<LargeFileEntry>>(() => Find(root, minBytes, maxResults, ct), ct);

    private static IReadOnlyList<LargeFileEntry> Find(string root, long minBytes, int maxResults, CancellationToken ct)
    {
        if (maxResults <= 0) return [];
        var heap = new PriorityQueue<LargeFileEntry, long>();
        var stack = new Stack<string>();
        stack.Push(root);

        while (stack.Count > 0)
        {
            ct.ThrowIfCancellationRequested();
            var dir = stack.Pop();
            var entries = RulePaths.ListEntries(dir);
            if (entries == null) continue;

            foreach (var e in entries)
            {
                if (RulePaths.IsReparse(e)) continue;
                if (RulePaths.IsDirectory(e)) { stack.Push(e.FullName); continue; }
                if ((e.Attributes & (FileAttributes.Offline | RecallOnOpen | RecallOnDataAccess)) != 0) continue;

                long len;
                try { len = ((FileInfo)e).Length; }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { continue; }
                if (len < minBytes) continue;

                if (heap.Count < maxResults)
                    heap.Enqueue(new LargeFileEntry(e.FullName, len, e.LastWriteTimeUtc), len);
                else if (heap.TryPeek(out _, out var smallest) && len > smallest)
                {
                    heap.Dequeue();
                    heap.Enqueue(new LargeFileEntry(e.FullName, len, e.LastWriteTimeUtc), len);
                }
            }
        }

        var list = new List<LargeFileEntry>(heap.Count);
        while (heap.Count > 0) list.Add(heap.Dequeue());
        list.Reverse();
        return list;
    }
}
