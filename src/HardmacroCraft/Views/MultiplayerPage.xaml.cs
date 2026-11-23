using System.Collections.Specialized;
using System.Windows;
using System.Windows.Controls;
using HardmacroCraft.Services;

namespace HardmacroCraft.Views;

/// <summary>
/// v0.15.9：联机页 —— 局域网房间发现 / 房间码加入 / 中转预留（见 MultiplayerService）。
/// </summary>
public partial class MultiplayerPage : Page
{
    private bool _updatingRoomList;

    public MultiplayerPage()
    {
        InitializeComponent();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        RefreshHostStatus();
        RefreshRelayStatus();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        MultiplayerService.Instance.StartDiscovery();
        MultiplayerService.Instance.RoomsChanged += OnRoomsChanged;
        RoomList.ItemsSource = MultiplayerService.Instance.Rooms;
        // 发现列表从后台线程增删 —— ObservableCollection 的 CollectionChanged 在
        // 非 UI 线程触发时绑定会崩，这里统一调度回 UI 线程刷新空态文案
        MultiplayerService.Instance.Rooms.CollectionChanged += OnRoomCollectionChanged;
        RefreshRoomsEmpty();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        MultiplayerService.Instance.RoomsChanged -= OnRoomsChanged;
        MultiplayerService.Instance.Rooms.CollectionChanged -= OnRoomCollectionChanged;
        // 发现保持运行（后台房间列表持续更新），页面重进时直接复用
    }

    private void OnRoomCollectionChanged(object? sender, NotifyCollectionChangedEventArgs e)
        => Dispatcher.BeginInvoke(RefreshRoomsEmpty);

    private void OnRoomsChanged() => Dispatcher.BeginInvoke(RefreshRoomsEmpty);

    private void RefreshRoomsEmpty()
    {
        TxtRoomsEmpty.Visibility = RoomList.Items.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    /// <summary>开放 / 关闭本机房间（端口取 MC「对局域网开放」提示框里的数字）。</summary>
    private void BtnHost_Click(object sender, RoutedEventArgs e)
    {
        var service = MultiplayerService.Instance;
        if (service.Hosting)
        {
            service.StopHosting();
            RefreshHostStatus();
            return;
        }
        if (!int.TryParse(TxtGamePort.Text.Trim(), out var port) || port is < 1024 or > 65535)
        {
            TxtHostStatus.Text = (string?)TryFindResource("S.Multi.PortInvalid") ?? "端口无效（1024-65535）";
            return;
        }
        if (service.StartHosting(port, TxtRoomName.Text))
            RefreshHostStatus();
        else
            TxtHostStatus.Text = (string?)TryFindResource("S.Multi.HostFailed") ?? "开放失败：发现端口被占用（可能有另一实例在运行）";
    }

    private void RefreshHostStatus()
    {
        var service = MultiplayerService.Instance;
        if (service.Hosting)
        {
            BtnHost.Content = (string?)TryFindResource("S.Multi.HostClose") ?? "关闭房间";
            TxtHostStatus.Text = (string?)TryFindResource("S.Multi.Hosting") is { } t
                ? t.Replace("{code}", service.HostingRoomCode).Replace("{port}", service.HostingGamePort.ToString())
                : $"房间码 {service.HostingRoomCode}";
        }
        else
        {
            BtnHost.Content = (string?)TryFindResource("S.Multi.HostOpen") ?? "开放房间";
            TxtHostStatus.Text = "";
        }
    }

    /// <summary>加入：房间码或 host:port → 解析为 MC 直接连接地址并复制。</summary>
    private void BtnCopy_Click(object sender, RoutedEventArgs e)
    {
        var target = TxtJoinTarget.Text.Trim();
        if (target.Length == 0) return;
        var service = MultiplayerService.Instance;
        var address = service.CopyJoinAddress(target);
        TxtJoinStatus.Text = address != null
            ? ((string?)TryFindResource("S.Multi.Copied") is { } t ? t.Replace("{address}", address) : $"已复制 {address}")
            : (string?)TryFindResource("S.Multi.ResolveFailed") ?? "未找到该房间码对应的局域网房间";
    }

    /// <summary>房间行内「复制」按钮。</summary>
    private void BtnRoomCopy_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is LanRoom room)
            Clipboard.SetText(room.JoinAddress);
    }

    private void RefreshRelayStatus()
    {
        TxtRelay.Text = MultiplayerService.Instance.RelayConfigured
            ? (string?)TryFindResource("S.Multi.RelayReady") ?? "中转服务器已配置，公网房间即将可用。"
            : (string?)TryFindResource("S.Multi.RelayPending") ?? "中转服务器尚未配置：当前支持同一局域网内的房间发现与直连；公网互通将在服务器配置后自动开放。";
    }
}
