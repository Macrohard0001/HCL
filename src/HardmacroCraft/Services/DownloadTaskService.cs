using System.Collections.ObjectModel;

namespace HardmacroCraft.Services;

/// <summary>
/// 下载任务中心（HMCL 式任务列表）：所有下载 / 安装 / 补全文件任务在此登记，
/// 任务中心页与各页面按钮共同消费进度。线程安全（后台线程回调直接更新）。
/// v0.1.10：任务支持去重 Key（同一逻辑任务重复登记时复用同一实例，修复重复任务），
/// 且每条任务保留一份进度日志（任务中心点击卡片查看详情）。
/// </summary>
public class DownloadTaskService
{
    public sealed class TaskItem
    {
        public string Title { get; internal set; } = "";
        /// <summary>running | done | failed | canceled</summary>
        public string Status { get; internal set; } = "running";
        /// <summary>0–100；Indeterminate 任务为 -1。</summary>
        public double Percent { get; internal set; }
        public string Detail { get; internal set; } = "";
        public DateTime StartedAt { get; internal set; } = DateTime.Now;
        public DateTime EndedAt { get; internal set; }

        /// <summary>v0.1.13：当前下载速度（字节/秒，≤0 = 不可用）。任务中心 / 详情页直接展示，
        /// 不再与文件名 / 字节数混在 Detail 一行里。</summary>
        public double SpeedBps { get; internal set; }
        /// <summary>v0.1.13：正在下载的文件（DisplayName，如 assets/objects/…）。</summary>
        public string CurrentFile { get; internal set; } = "";
        /// <summary>v0.1.13：已下载 / 总字节（批量下载时可展示，单文件下载也写入）。</summary>
        public long DoneBytes { get; internal set; }
        public long TotalBytes { get; internal set; }

        /// <summary>v0.1.10：去重 Key（同一逻辑任务的登记标识，如 "launch:1.20.1"）。
        /// StartKeyed 用它复用运行中的同一任务实例；null = 不参与去重。</summary>
        public string? Key { get; internal set; }

        /// <summary>v0.1.10：任务进度日志（时间戳行，最多 300 条；详情弹窗展示）。</summary>
        private readonly List<string> _logs = new();
        private readonly object _logLock = new();

        /// <summary>fix7：可取消任务持有的取消源（StartCancellable 登记，RequestCancel 触发）。</summary>
        internal CancellationTokenSource? Cts { get; set; }

        /// <summary>v0.1.12：取消是否已请求（用于立即反馈「正在取消…」，避免点完取消后
        /// 卡片长时间停在「运行中」让用户以为取消卡住了）。</summary>
        public bool CancelRequested { get; internal set; }

        public bool IsActive => Status == "running";
        /// <summary>任务是否可以取消（运行中且登记了取消源）。</summary>
        public bool CanCancel => IsActive && Cts != null;

        /// <summary>任务总耗时（运行中为已进行时间；未开始为 0）。</summary>
        public TimeSpan Elapsed => (EndsAt ?? DateTime.Now) - StartedAt;
        internal DateTime? EndsAt => IsActive ? null : EndedAt;

        /// <summary>fix7：请求取消（PCL/HMCL 同款）。触发 CTS，实际状态由业务方在捕获
        /// OperationCanceledException 后调用 DownloadTaskService.Cancel 写入。</summary>
        public void RequestCancel()
        {
            // v0.1.12：立即反馈「正在取消…」—— 此前点了取消后卡片仍是「运行中」+
            // 原进度文案，业务侧感知令牌往往要等网络超时，看起来像取消卡住了
            CancelRequested = true;
            try { Cts?.Cancel(); } catch { /* CTS 已释放等场景忽略 */ }
            if (IsActive)
            {
                lock (_logLock)
                {
                    _logs.Add($"[{DateTime.Now:HH:mm:ss}] 正在取消…（等当前文件 / 网络操作结束后生效）");
                    if (_logs.Count > 300)
                        _logs.RemoveRange(0, _logs.Count - 300);
                }
            }
            DownloadTaskService.Instance.NotifyChanged();
        }

        /// <summary>v0.1.10：追加一条任务日志（线程安全，最多保留 300 行，超出丢弃最早的）。</summary>
        public void AppendLog(string line)
        {
            if (string.IsNullOrWhiteSpace(line)) return;
            lock (_logLock)
            {
                _logs.Add($"[{DateTime.Now:HH:mm:ss}] {line.TrimEnd()}");
                if (_logs.Count > 300)
                    _logs.RemoveRange(0, _logs.Count - 300);
            }
        }

        /// <summary>
        /// v0.1.13：更新速度 / 当前文件 / 字节进度（任务中心卡片与详情页分两行展示）。
        /// 高频调用（字节回调）只改字段 + 广播事件，不写日志避免刷屏。
        /// </summary>
        public void SetProgress(double percent, string? currentFile = null,
            double? speedBps = null, long? doneBytes = null, long? totalBytes = null)
        {
            lock (_logLock)
            {
                Percent = Math.Clamp(percent, -1, 100);
                if (currentFile != null) CurrentFile = currentFile;
                if (speedBps.HasValue) SpeedBps = Math.Max(0, speedBps.Value);
                if (doneBytes.HasValue) DoneBytes = Math.Max(0, doneBytes.Value);
                if (totalBytes.HasValue) TotalBytes = Math.Max(0, totalBytes.Value);
            }
            DownloadTaskService.Instance.NotifyChanged();
        }

        /// <summary>日志快照（详情弹窗 / 测试用）。</summary>
        public IReadOnlyList<string> LogSnapshot()
        {
            lock (_logLock) { return _logs.ToArray(); }
        }
    }

    public static DownloadTaskService Instance { get; } = new();

    private readonly object _lock = new();
    private readonly List<TaskItem> _tasks = new();

    /// <summary>快照（最新在前）。UI 每次刷新时取。</summary>
    public List<TaskItem> Snapshot()
    {
        lock (_lock)
        {
            return _tasks.AsEnumerable().Reverse().ToList();
        }
    }

    /// <summary>登记一个运行中的任务。</summary>
    public TaskItem Start(string title, string detail = "")
        => StartKeyed(null, title, detail);

    /// <summary>
    /// v0.1.10：按 Key 登记任务 —— 若已存在同 Key 的运行中任务，直接复用（刷新标题 /
    /// 详情）而不新建，修复「同一逻辑任务在任务中心出现两条」的重复问题。
    /// key 为 null 时与 Start 等价（不去重）。
    /// </summary>
    public TaskItem StartKeyed(string? key, string title, string detail = "")
    {
        if (key != null)
        {
            TaskItem? existing;
            lock (_lock)
                existing = _tasks.FirstOrDefault(t => t.IsActive && t.Key == key);
            if (existing != null)
            {
                lock (_lock)
                {
                    existing.Title = title;
                    if (detail.Length > 0) existing.Detail = detail;
                }
                Changed?.Invoke();
                return existing;
            }
        }
        var item = new TaskItem { Title = title, Detail = detail, Key = key };
        item.AppendLog("任务开始：" + title);
        lock (_lock)
        {
            _tasks.Add(item);
            // 最多保留 50 条历史
            if (_tasks.Count > 50) _tasks.RemoveAt(0);
        }
        Changed?.Invoke();
        return item;
    }

    /// <summary>
    /// 登记一个可取消的运行中任务（fix7，PCL/HMCL 同款）：任务中心与启动页的
    /// 「取消」按钮调用 RequestCancel → 业务方捕获 OperationCanceledException 后收尾。
    /// v0.1.10：key 非 null 时同 Key 运行中任务复用（取消源沿用原任务的）。
    /// </summary>
    public TaskItem StartCancellable(string title, string detail = "")
        => StartCancellableKeyed(null, title, detail);

    public TaskItem StartCancellableKeyed(string? key, string title, string detail = "")
    {
        if (key != null)
        {
            TaskItem? existing;
            lock (_lock)
                existing = _tasks.FirstOrDefault(t => t.IsActive && t.Key == key);
            if (existing != null && existing.Cts != null) return existing;
        }
        var item = StartKeyed(key, title, detail);
        item.Cts = new CancellationTokenSource();
        return item;
    }

    /// <summary>取任务绑定的取消令牌（不可取消任务返回 default）。</summary>
    public CancellationToken TokenFor(TaskItem item) => item.Cts?.Token ?? default;

    /// <summary>更新进度（0–100，-1 表示不确定）。detail 变化时自动记入任务日志。</summary>
    public void Update(TaskItem item, double percent, string? detail = null)
    {
        string? lastDetail;
        lock (_lock)
        {
            lastDetail = item.Detail;
            item.Percent = Math.Clamp(percent, -1, 100);
            if (detail != null) item.Detail = detail;
        }
        // v0.1.10：详情与上次不同时记一条日志（同进度高频回调不会刷屏）
        if (!string.IsNullOrWhiteSpace(detail) && detail != lastDetail)
            item.AppendLog(detail);
        Changed?.Invoke();
    }

    public void Complete(TaskItem item, string? detail = null) => Finish(item, "done", detail);
    public void Fail(TaskItem item, string? detail = null) => Finish(item, "failed", detail);
    public void Cancel(TaskItem item, string? detail = null) => Finish(item, "canceled", detail);

    /// <summary>v0.1.12：任务列表变化事件的触发入口（供 TaskItem 内部反向通知）。</summary>
    internal void NotifyChanged() => Changed?.Invoke();

    private void Finish(TaskItem item, string status, string? detail)
    {
        lock (_lock)
        {
            if (!item.IsActive) return; // 幂等：首次落定为准，重复取消/完成不覆盖
            item.Status = status;
            item.EndedAt = DateTime.Now;
            item.Percent = status == "done" ? 100 : item.Percent;
            if (detail != null) item.Detail = detail;
            item.SpeedBps = 0; // v0.1.13：终态不再显示速度
            item.Cts?.Dispose();
            item.Cts = null;
        }
        var finishText = status switch
        {
            "done" => "任务完成" + (string.IsNullOrWhiteSpace(detail) ? "" : "：" + detail),
            "failed" => "任务失败" + (string.IsNullOrWhiteSpace(detail) ? "" : "：" + detail),
            "canceled" => "任务已取消",
            _ => status,
        };
        item.AppendLog(finishText);
        Changed?.Invoke();
        // v0.1.3 内存优化：重大操作（启动 / 安装 / 批量下载 / Java 安装）成功收尾后
        // 延迟回收内存。单点挂钩 —— 所有重操作都在此登记任务，无需逐个业务处插入。
        if (status == "done") MemoryManager.OptimizeAfterHeavyOp();
    }

    public void ClearFinished()
    {
        lock (_lock)
        {
            _tasks.RemoveAll(t => !t.IsActive);
        }
        Changed?.Invoke();
    }

    /// <summary>任务列表变化（任何线程触发；UI 端自行封送）。</summary>
    public event Action? Changed;
}
