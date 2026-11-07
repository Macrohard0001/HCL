using System.Collections.ObjectModel;

namespace HardmacroCraft.Services;

/// <summary>
/// 下载任务中心（HMCL 式任务列表）：所有下载 / 安装 / 补全文件任务在此登记，
/// 任务中心页与各页面按钮共同消费进度。线程安全（后台线程回调直接更新）。
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

        /// <summary>fix7：可取消任务持有的取消源（StartCancellable 登记，RequestCancel 触发）。</summary>
        internal CancellationTokenSource? Cts { get; set; }

        public bool IsActive => Status == "running";
        /// <summary>任务是否可以取消（运行中且登记了取消源）。</summary>
        public bool CanCancel => IsActive && Cts != null;

        /// <summary>fix7：请求取消（PCL/HMCL 同款）。触发 CTS，实际状态由业务方在捕获
        /// OperationCanceledException 后调用 DownloadTaskService.Cancel 写入。</summary>
        public void RequestCancel()
        {
            try { Cts?.Cancel(); } catch { /* CTS 已释放等场景忽略 */ }
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
    {
        var item = new TaskItem { Title = title, Detail = detail };
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
    /// </summary>
    public TaskItem StartCancellable(string title, string detail = "")
    {
        var item = Start(title, detail);
        item.Cts = new CancellationTokenSource();
        return item;
    }

    /// <summary>取任务绑定的取消令牌（不可取消任务返回 default）。</summary>
    public CancellationToken TokenFor(TaskItem item) => item.Cts?.Token ?? default;

    /// <summary>更新进度（0–100，-1 表示不确定）。</summary>
    public void Update(TaskItem item, double percent, string? detail = null)
    {
        lock (_lock)
        {
            item.Percent = Math.Clamp(percent, -1, 100);
            if (detail != null) item.Detail = detail;
        }
        Changed?.Invoke();
    }

    public void Complete(TaskItem item, string? detail = null) => Finish(item, "done", detail);
    public void Fail(TaskItem item, string? detail = null) => Finish(item, "failed", detail);
    public void Cancel(TaskItem item, string? detail = null) => Finish(item, "canceled", detail);

    private void Finish(TaskItem item, string status, string? detail)
    {
        lock (_lock)
        {
            if (!item.IsActive) return; // 幂等：首次落定为准，重复取消/完成不覆盖
            item.Status = status;
            item.EndedAt = DateTime.Now;
            item.Percent = status == "done" ? 100 : item.Percent;
            if (detail != null) item.Detail = detail;
            item.Cts?.Dispose();
            item.Cts = null;
        }
        Changed?.Invoke();
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
