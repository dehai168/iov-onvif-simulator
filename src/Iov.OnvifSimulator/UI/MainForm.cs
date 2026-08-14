using Iov.OnvifSimulator.Models;
using Iov.OnvifSimulator.Services;

namespace Iov.OnvifSimulator.UI;

public sealed class MainForm : Form
{
    private readonly ConfigStore _store = new();
    private readonly AppConfig _config;
    private readonly Dictionary<string, SimulatorHost> _hosts = new();

    private readonly ListView _simulatorList;
    private readonly TextBox _nameBox;
    private readonly TextBox _manufacturerBox;
    private readonly TextBox _modelBox;
    private readonly TextBox _serialBox;
    private readonly TextBox _firmwareBox;
    private readonly NumericUpDown _httpPort;
    private readonly NumericUpDown _rtspPort;
    private readonly TextBox _userBox;
    private readonly TextBox _passwordBox;
    private readonly CheckBox _authBox;
    private readonly ListBox _mediaList;
    private readonly CheckBox _loopBox;
    private readonly ComboBox _orderBox;
    private readonly Label _endpointLabel;
    private readonly TextBox _logBox;
    private readonly Button _startButton;
    private readonly Button _stopButton;
    private readonly Panel _editorPanel;

    private bool _suppressEvents;
    private bool _forceClose;

    public MainForm()
    {
        _config = _store.Load();

        Text = "IOV ONVIF 协议模拟器";
        Width = 1120;
        Height = 760;
        MinimumSize = new Size(980, 640);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 9F);

        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 2,
            RowCount = 2,
            Padding = new Padding(10)
        };
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 280));
        root.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        root.RowStyles.Add(new RowStyle(SizeType.Absolute, 160));

        var left = CreateLeftPanel(out _simulatorList, out _startButton, out _stopButton);
        _editorPanel = CreateEditorPanel(
            out _nameBox, out _manufacturerBox, out _modelBox, out _serialBox, out _firmwareBox,
            out _httpPort, out _rtspPort, out _userBox, out _passwordBox, out _authBox,
            out _mediaList, out _loopBox, out _orderBox, out _endpointLabel);

        _logBox = new TextBox
        {
            Dock = DockStyle.Fill,
            Multiline = true,
            ReadOnly = true,
            ScrollBars = ScrollBars.Vertical,
            BackColor = Color.FromArgb(30, 30, 30),
            ForeColor = Color.Gainsboro
        };

        var logHost = new GroupBox { Text = "运行日志", Dock = DockStyle.Fill, Padding = new Padding(8) };
        logHost.Controls.Add(_logBox);

        root.Controls.Add(left, 0, 0);
        root.Controls.Add(_editorPanel, 1, 0);
        root.SetColumnSpan(logHost, 2);
        root.Controls.Add(logHost, 0, 1);
        Controls.Add(root);

        FormClosing += async (_, e) =>
        {
            if (_forceClose || !_hosts.Values.Any(x => x.IsRunning))
            {
                return;
            }

            e.Cancel = true;
            await StopAllAsync().ConfigureAwait(true);
            _forceClose = true;
            Close();
        };

        Load += (_, _) =>
        {
            RefreshList();
            if (_simulatorList.Items.Count > 0)
            {
                _simulatorList.Items[0].Selected = true;
            }

            Log("配置文件: " + _store.FilePath);
            if (!FfmpegLocator.IsAvailable())
            {
                Log("未检测到 FFmpeg。H264/H265 MP4 可直接推流，AVI 或其他编码需要安装 FFmpeg 并加入 PATH。");
            }
        };
    }

    private Control CreateLeftPanel(out ListView list, out Button startButton, out Button stopButton)
    {
        var panel = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            RowCount = 3,
            ColumnCount = 1
        };
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 28));
        panel.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        panel.RowStyles.Add(new RowStyle(SizeType.Absolute, 120));

        panel.Controls.Add(new Label { Text = "模拟器", Dock = DockStyle.Fill, TextAlign = ContentAlignment.MiddleLeft }, 0, 0);

        list = new ListView
        {
            Dock = DockStyle.Fill,
            View = View.Details,
            FullRowSelect = true,
            MultiSelect = false,
            HideSelection = false
        };
        list.Columns.Add("名称", 120);
        list.Columns.Add("状态", 60);
        list.Columns.Add("HTTP", 50);
        list.SelectedIndexChanged += (_, _) => LoadSelectedToEditor();
        panel.Controls.Add(list, 0, 1);

        var buttons = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            FlowDirection = FlowDirection.LeftToRight,
            WrapContents = true
        };
        var addButton = new Button { Text = "添加", Width = 70, Height = 28 };
        var removeButton = new Button { Text = "删除", Width = 70, Height = 28 };
        startButton = new Button { Text = "启动", Width = 70, Height = 28 };
        stopButton = new Button { Text = "停止", Width = 70, Height = 28 };
        var startAll = new Button { Text = "全部启动", Width = 80, Height = 28 };
        var stopAll = new Button { Text = "全部停止", Width = 80, Height = 28 };

        addButton.Click += (_, _) => AddSimulator();
        removeButton.Click += async (_, _) => await RemoveSimulatorAsync().ConfigureAwait(true);
        startButton.Click += async (_, _) => await StartSelectedAsync().ConfigureAwait(true);
        stopButton.Click += async (_, _) => await StopSelectedAsync().ConfigureAwait(true);
        startAll.Click += async (_, _) => await StartAllAsync().ConfigureAwait(true);
        stopAll.Click += async (_, _) => await StopAllAsync().ConfigureAwait(true);

        buttons.Controls.AddRange(new Control[] { addButton, removeButton, startButton, stopButton, startAll, stopAll });
        panel.Controls.Add(buttons, 0, 2);
        return panel;
    }

    private Panel CreateEditorPanel(
        out TextBox nameBox, out TextBox manufacturerBox, out TextBox modelBox, out TextBox serialBox, out TextBox firmwareBox,
        out NumericUpDown httpPort, out NumericUpDown rtspPort, out TextBox userBox, out TextBox passwordBox, out CheckBox authBox,
        out ListBox mediaList, out CheckBox loopBox, out ComboBox orderBox, out Label endpointLabel)
    {
        var panel = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Top,
            AutoSize = true,
            ColumnCount = 4,
            Padding = new Padding(8, 0, 8, 8)
        };
        for (var i = 0; i < 4; i++)
        {
            layout.ColumnStyles.Add(new ColumnStyle(i % 2 == 0 ? SizeType.Absolute : SizeType.Percent, i % 2 == 0 ? 90 : 50));
        }

        nameBox = CreateTextBox();
        manufacturerBox = CreateTextBox();
        modelBox = CreateTextBox();
        serialBox = CreateTextBox();
        firmwareBox = CreateTextBox();
        httpPort = CreatePortBox(8080);
        rtspPort = CreatePortBox(8554);
        userBox = CreateTextBox();
        passwordBox = CreateTextBox();
        passwordBox.UseSystemPasswordChar = true;
        authBox = new CheckBox { Text = "启用 Digest/WS-UsernameToken 认证", AutoSize = true, Checked = true };

        AddRow(layout, 0, "名称", nameBox, "厂商", manufacturerBox);
        AddRow(layout, 1, "型号", modelBox, "序列号", serialBox);
        AddRow(layout, 2, "固件", firmwareBox, "认证用户", userBox);
        AddRow(layout, 3, "HTTP端口", httpPort, "RTSP端口", rtspPort);
        layout.Controls.Add(new Label { Text = "密码", TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, 4);
        layout.Controls.Add(passwordBox, 1, 4);
        layout.SetColumnSpan(authBox, 2);
        layout.Controls.Add(authBox, 2, 4);

        var mediaGroup = new GroupBox { Text = "视频源列表", Dock = DockStyle.Top, Height = 250, Padding = new Padding(8) };
        var mediaLayout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 2 };
        mediaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        mediaLayout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 100));
        mediaList = new ListBox { Dock = DockStyle.Fill, HorizontalScrollbar = true };
        var mediaButtons = new FlowLayoutPanel { Dock = DockStyle.Fill, FlowDirection = FlowDirection.TopDown };
        var addMedia = new Button { Text = "添加文件", Width = 88 };
        var removeMedia = new Button { Text = "移除", Width = 88 };
        var upMedia = new Button { Text = "上移", Width = 88 };
        var downMedia = new Button { Text = "下移", Width = 88 };
        addMedia.Click += (_, _) => AddMediaFiles();
        removeMedia.Click += (_, _) => RemoveMediaFile();
        upMedia.Click += (_, _) => MoveMedia(-1);
        downMedia.Click += (_, _) => MoveMedia(1);
        mediaButtons.Controls.AddRange(new Control[] { addMedia, removeMedia, upMedia, downMedia });
        mediaLayout.Controls.Add(mediaList, 0, 0);
        mediaLayout.Controls.Add(mediaButtons, 1, 0);
        mediaGroup.Controls.Add(mediaLayout);

        loopBox = new CheckBox { Text = "循环推流", AutoSize = true, Checked = true };
        orderBox = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList, Width = 140 };
        orderBox.Items.AddRange(new object[] { "顺序播放", "随机播放" });
        orderBox.SelectedIndex = 0;
        var playOptions = new FlowLayoutPanel { Dock = DockStyle.Top, Height = 36, Padding = new Padding(8, 4, 8, 0) };
        playOptions.Controls.Add(loopBox);
        playOptions.Controls.Add(new Label { Text = "播放顺序", AutoSize = true, Padding = new Padding(16, 6, 8, 0) });
        playOptions.Controls.Add(orderBox);

        endpointLabel = new Label
        {
            Dock = DockStyle.Top,
            Height = 70,
            Padding = new Padding(8, 4, 8, 0),
            Text = "ONVIF / RTSP 地址将在启动后显示。"
        };

        BindEditorEvents();

        panel.Controls.Add(endpointLabel);
        panel.Controls.Add(playOptions);
        panel.Controls.Add(mediaGroup);
        panel.Controls.Add(layout);
        return panel;
    }

    private void BindEditorEvents()
    {
        void OnChanged(object? sender, EventArgs e) => SaveEditorToSelected();
        _nameBox.TextChanged += OnChanged;
        _manufacturerBox.TextChanged += OnChanged;
        _modelBox.TextChanged += OnChanged;
        _serialBox.TextChanged += OnChanged;
        _firmwareBox.TextChanged += OnChanged;
        _userBox.TextChanged += OnChanged;
        _passwordBox.TextChanged += OnChanged;
        _httpPort.ValueChanged += OnChanged;
        _rtspPort.ValueChanged += OnChanged;
        _authBox.CheckedChanged += OnChanged;
        _loopBox.CheckedChanged += OnChanged;
        _orderBox.SelectedIndexChanged += OnChanged;
    }

    private SimulatorConfig? SelectedConfig
    {
        get
        {
            if (_simulatorList.SelectedItems.Count == 0)
            {
                return null;
            }

            var id = _simulatorList.SelectedItems[0].Tag as string;
            return _config.Simulators.FirstOrDefault(x => x.Id == id);
        }
    }

    private void RefreshList(string? selectedId = null)
    {
        selectedId ??= SelectedConfig?.Id;
        _simulatorList.BeginUpdate();
        _simulatorList.Items.Clear();
        foreach (var item in _config.Simulators)
        {
            var running = _hosts.TryGetValue(item.Id, out var host) && host.IsRunning;
            var row = new ListViewItem(item.Name)
            {
                Tag = item.Id
            };
            row.SubItems.Add(running ? "运行中" : "已停止");
            row.SubItems.Add(item.HttpPort.ToString());
            _simulatorList.Items.Add(row);
            if (item.Id == selectedId)
            {
                row.Selected = true;
            }
        }

        _simulatorList.EndUpdate();
        UpdateEditorEnabled();
        UpdateEndpointLabel();
    }

    private void LoadSelectedToEditor()
    {
        var config = SelectedConfig;
        if (config == null)
        {
            return;
        }

        _suppressEvents = true;
        _nameBox.Text = config.Name;
        _manufacturerBox.Text = config.Manufacturer;
        _modelBox.Text = config.Model;
        _serialBox.Text = config.SerialNumber;
        _firmwareBox.Text = config.FirmwareVersion;
        _httpPort.Value = Math.Clamp(config.HttpPort, 1, 65535);
        _rtspPort.Value = Math.Clamp(config.RtspPort, 1, 65535);
        _userBox.Text = config.UserName;
        _passwordBox.Text = config.Password;
        _authBox.Checked = config.EnableAuthentication;
        _loopBox.Checked = config.LoopPlayback;
        _orderBox.SelectedIndex = config.PlayOrder == PlayOrder.Shuffle ? 1 : 0;
        _mediaList.Items.Clear();
        foreach (var file in config.MediaFiles)
        {
            _mediaList.Items.Add(file);
        }

        _suppressEvents = false;
        UpdateEditorEnabled();
        UpdateEndpointLabel();
    }

    private void SaveEditorToSelected()
    {
        if (_suppressEvents)
        {
            return;
        }

        var config = SelectedConfig;
        if (config == null)
        {
            return;
        }

        config.Name = _nameBox.Text.Trim();
        config.Manufacturer = _manufacturerBox.Text.Trim();
        config.Model = _modelBox.Text.Trim();
        config.SerialNumber = _serialBox.Text.Trim();
        config.FirmwareVersion = _firmwareBox.Text.Trim();
        config.HttpPort = (int)_httpPort.Value;
        config.RtspPort = (int)_rtspPort.Value;
        config.UserName = _userBox.Text.Trim();
        config.Password = _passwordBox.Text;
        config.EnableAuthentication = _authBox.Checked;
        config.LoopPlayback = _loopBox.Checked;
        config.PlayOrder = _orderBox.SelectedIndex == 1 ? PlayOrder.Shuffle : PlayOrder.Sequential;
        config.MediaFiles = _mediaList.Items.Cast<string>().ToList();
        _store.Save(_config);
        if (_simulatorList.SelectedItems.Count > 0)
        {
            var row = _simulatorList.SelectedItems[0];
            row.Text = string.IsNullOrWhiteSpace(config.Name) ? config.Id : config.Name;
            row.SubItems[2].Text = config.HttpPort.ToString();
        }

        UpdateEndpointLabel();
    }

    private void AddSimulator()
    {
        var created = ConfigStore.CreateNext(_config.Simulators);
        _config.Simulators.Add(created);
        _store.Save(_config);
        RefreshList(created.Id);
    }

    private async Task RemoveSimulatorAsync()
    {
        var config = SelectedConfig;
        if (config == null)
        {
            return;
        }

        if (_hosts.TryGetValue(config.Id, out var host) && host.IsRunning)
        {
            await host.StopAsync().ConfigureAwait(true);
            _hosts.Remove(config.Id);
        }

        _config.Simulators.Remove(config);
        _store.Save(_config);
        RefreshList();
        if (_simulatorList.Items.Count > 0)
        {
            _simulatorList.Items[0].Selected = true;
        }
    }

    private void AddMediaFiles()
    {
        using var dialog = new OpenFileDialog
        {
            Filter = "视频文件 (*.mp4;*.avi;*.m4v;*.mov)|*.mp4;*.avi;*.m4v;*.mov|所有文件 (*.*)|*.*",
            Multiselect = true,
            Title = "选择推流视频文件"
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            if (!_mediaList.Items.Contains(file))
            {
                _mediaList.Items.Add(file);
            }
        }

        SaveEditorToSelected();
    }

    private void RemoveMediaFile()
    {
        if (_mediaList.SelectedIndex < 0)
        {
            return;
        }

        _mediaList.Items.RemoveAt(_mediaList.SelectedIndex);
        SaveEditorToSelected();
    }

    private void MoveMedia(int offset)
    {
        var index = _mediaList.SelectedIndex;
        var target = index + offset;
        if (index < 0 || target < 0 || target >= _mediaList.Items.Count)
        {
            return;
        }

        var item = _mediaList.Items[index];
        _mediaList.Items.RemoveAt(index);
        _mediaList.Items.Insert(target, item);
        _mediaList.SelectedIndex = target;
        SaveEditorToSelected();
    }

    private async Task StartSelectedAsync()
    {
        var config = SelectedConfig;
        if (config == null)
        {
            return;
        }

        SaveEditorToSelected();
        if (!EnsureUniquePorts(config))
        {
            return;
        }

        try
        {
            UseWaitCursor = true;
            var host = GetOrCreateHost(config);
            await host.StartAsync().ConfigureAwait(true);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, ex.Message, "启动失败", MessageBoxButtons.OK, MessageBoxIcon.Error);
            Log($"启动失败: {ex.Message}");
        }
        finally
        {
            UseWaitCursor = false;
            RefreshList(config.Id);
        }
    }

    private async Task StopSelectedAsync()
    {
        var config = SelectedConfig;
        if (config == null)
        {
            return;
        }

        if (_hosts.TryGetValue(config.Id, out var host))
        {
            await host.StopAsync().ConfigureAwait(true);
        }

        RefreshList(config.Id);
    }

    private async Task StartAllAsync()
    {
        SaveEditorToSelected();
        foreach (var config in _config.Simulators.ToList())
        {
            if (_hosts.TryGetValue(config.Id, out var existing) && existing.IsRunning)
            {
                continue;
            }

            if (!EnsureUniquePorts(config, showDialog: false))
            {
                Log($"{config.Name} 端口冲突，已跳过。");
                continue;
            }

            try
            {
                await GetOrCreateHost(config).StartAsync().ConfigureAwait(true);
            }
            catch (Exception ex)
            {
                Log($"{config.Name} 启动失败: {ex.Message}");
            }
        }

        RefreshList();
    }

    private async Task StopAllAsync()
    {
        foreach (var host in _hosts.Values.ToList())
        {
            await host.StopAsync().ConfigureAwait(true);
        }

        RefreshList();
    }

    private SimulatorHost GetOrCreateHost(SimulatorConfig config)
    {
        if (_hosts.TryGetValue(config.Id, out var host))
        {
            return host;
        }

        host = new SimulatorHost(config, Log);
        _hosts[config.Id] = host;
        return host;
    }

    private bool EnsureUniquePorts(SimulatorConfig config, bool showDialog = true)
    {
        var duplicate = _config.Simulators.FirstOrDefault(x =>
            x.Id != config.Id && (x.HttpPort == config.HttpPort || x.RtspPort == config.RtspPort));
        if (duplicate == null)
        {
            return true;
        }

        var message = $"端口与模拟器 {duplicate.Name} 冲突（HTTP {config.HttpPort} / RTSP {config.RtspPort}）。";
        if (showDialog)
        {
            MessageBox.Show(this, message, "端口冲突", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }

        return false;
    }

    private void UpdateEditorEnabled()
    {
        var running = SelectedConfig != null &&
                      _hosts.TryGetValue(SelectedConfig.Id, out var host) &&
                      host.IsRunning;
        _editorPanel.Enabled = SelectedConfig != null && !running;
        _startButton.Enabled = SelectedConfig != null && !running;
        _stopButton.Enabled = running;
    }

    private void UpdateEndpointLabel()
    {
        var config = SelectedConfig;
        if (config == null)
        {
            _endpointLabel.Text = string.Empty;
            return;
        }

        var ip = NetworkDefaults.GetPrimaryIPv4();
        _endpointLabel.Text =
            $"ONVIF: http://{ip}:{config.HttpPort}/onvif/device_service{Environment.NewLine}" +
            $"RTSP:  rtsp://{ip}:{config.RtspPort}/stream";
    }

    private void Log(string message)
    {
        if (IsDisposed)
        {
            return;
        }

        void Append()
        {
            _logBox.AppendText($"[{DateTime.Now:HH:mm:ss}] {message}{Environment.NewLine}");
        }

        if (InvokeRequired)
        {
            BeginInvoke(Append);
        }
        else
        {
            Append();
        }
    }

    private static TextBox CreateTextBox() => new() { Dock = DockStyle.Fill };

    private static NumericUpDown CreatePortBox(int value) => new()
    {
        Dock = DockStyle.Fill,
        Minimum = 1,
        Maximum = 65535,
        Value = value
    };

    private static void AddRow(TableLayoutPanel layout, int row, string leftLabel, Control left, string rightLabel, Control right)
    {
        layout.Controls.Add(new Label { Text = leftLabel, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 0, row);
        layout.Controls.Add(left, 1, row);
        layout.Controls.Add(new Label { Text = rightLabel, TextAlign = ContentAlignment.MiddleLeft, Dock = DockStyle.Fill }, 2, row);
        layout.Controls.Add(right, 3, row);
    }
}
