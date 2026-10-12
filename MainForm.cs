using System;
using System.Drawing;
using System.IO;
using System.IO.Ports;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using SaabDidTool.Interfaces;
using SaabDidTool.Models;
using SaabDidTool.Services;
using SaabDidTool.Services.J2534;

namespace SaabDidTool
{
    public class MainForm : Form
    {
        // ---- dark theme palette ----
        private static readonly Color BgColor = Color.FromArgb(18, 18, 21);
        private static readonly Color PanelColor = Color.FromArgb(30, 30, 34);
        private static readonly Color CardColor = Color.FromArgb(26, 26, 30);
        private static readonly Color BorderColor = Color.FromArgb(50, 50, 56);
        private static readonly Color TextColor = Color.FromArgb(232, 232, 235);
        private static readonly Color SubTextColor = Color.FromArgb(150, 150, 158);
        private static readonly Color AccentColor = Color.FromArgb(0, 153, 102);
        private static readonly Color AccentTextColor = Color.FromArgb(10, 10, 10);
        private static readonly Color InputBgColor = Color.FromArgb(14, 14, 16);

        private const int OutageByteIndex = 0;
        private FlowLayoutPanel _responsiveCards;
        private Panel _cardsViewport;

        private static readonly string[] FrontChannelNames =
        {
            "Parking Lights", "Fog Lights", "Low Beams", "High Beams", "Turn Signals",
        };
        private static readonly string[] RearChannelNames =
        {
            "Tail Lights", "Fog Lights", "Stop Lights", "Reverse Light", "License Plate", "Turn Signals",
        };

        private enum DeviceMode { J2534, Tech2 }

        private J2534GmlanChannel _j2534Channel;
        private readonly BackupManager _backup = new BackupManager();
        private GmlanDidService _didService;
        private DeviceMode _mode = DeviceMode.J2534;

        // Device panel
        private RadioButton _rbJ2534;
        private RadioButton _rbTech2;
        private Panel _j2534FieldsPanel;
        private Panel _tech2FieldsPanel;
        private Panel _j2534AdvancedPanel;
        private Button _btnToggleAdvanced;
        private bool _advancedVisible;
        private ComboBox _cmbJ2534Device;
        private ComboBox _cmbTech2Device;
        private ComboBox _cmbJ2534Protocol;
        private Button _btnConnect;
        private Label _lblBattery;

        // Front (UEC) / Rear (REC) checklists
        private readonly CheckBox[] _frontChecks = new CheckBox[FrontChannelNames.Length];
        private readonly CheckBox[] _rearChecks = new CheckBox[RearChannelNames.Length];
        private byte[] _frontRawValue = Array.Empty<byte>();
        private byte[] _rearRawValue = Array.Empty<byte>();
        private bool _suppressChecklistEvents;

        private Button _btnReadConfig;
        private Button _btnWriteConfig;
        private Button _btnOpenBackups;
        private TextBox _txtLog;

        private sealed class J2534ProtocolOption
        {
            public string Label { get; }
            public uint Id { get; }
            public J2534ProtocolOption(string label, uint id) { Label = label; Id = id; }
            public override string ToString() => Label;
        }

        public MainForm()
        {
            AutoScaleMode = AutoScaleMode.Dpi;
            Font = new Font("Segoe UI", 9.5F);
            Text = "Saab 93 NG LED Enabler  ·  v1.0.5  ·  BCR Auto";
            BackColor = BgColor;
            ForeColor = TextColor;
            Width = 1240;
            Height = 875;
            MinimumSize = new Size(1010, 680);
            StartPosition = FormStartPosition.CenterScreen;
            Padding = new Padding(12);

            // Use the icon embedded in the executable; it works after Inno Setup,
            // even if the Resources directory is not present beside the .exe.
            try
            {
                using var embeddedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);
                if (embeddedIcon != null) Icon = (Icon)embeddedIcon.Clone();
            }
            catch { /* icon is cosmetic; never block startup */ }
            ShowIcon = true;

            // Keep the full three-card layout visible on smaller screens.
            var working = Screen.FromControl(this).WorkingArea;
            if (working.Width < Width || working.Height < Height)
            {
                Width = Math.Min(Width, working.Width);
                Height = Math.Min(Height, working.Height);
                if (working.Width >= 1010 && working.Height >= 680)
                    MinimumSize = new Size(1010, 680);
                else
                    MinimumSize = new Size(780, 580);
            }

            BuildUi();
            SetDeviceMode(DeviceMode.J2534);
            PopulateJ2534Devices();

            Load += async (s, e) => await CheckForUpdatesAsync();
        }

        private async Task CheckForUpdatesAsync()
        {
            UpdateInfo update = await UpdateChecker.CheckForUpdateAsync(AppendLog);
            if (update == null) return;

            AppendLog($"Update available: v{update.Version} (current: v{UpdateChecker.CurrentVersion})");
            string notes = string.IsNullOrWhiteSpace(update.ReleaseNotes) ? "" : "\n\n" + update.ReleaseNotes;
            DialogResult result = MessageBox.Show(
                this,
                $"A new version is available: v{update.Version} (you have v{UpdateChecker.CurrentVersion}).{notes}\n\nDownload and install now?",
                "Update available",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Information);
            if (result != DialogResult.Yes) return;

            try
            {
                AppendLog("Downloading update...");
                string installerPath = await UpdateChecker.DownloadInstallerAsync(update);
                AppendLog("Launching installer...");
                UpdateChecker.RunInstaller(installerPath);
                Application.Exit();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Update download failed: " + ex.Message, "Update failed");
            }
        }

        // ---- styling helpers ----

        private static Label SectionLabel(string text)
        {
            return new Label
            {
                Text = text.ToUpperInvariant(),
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F),
                ForeColor = AccentColor,
                Margin = new Padding(0, 0, 0, 8),
            };
        }

        private static ComboBox StyledCombo(int width)
        {
            return new ComboBox
            {
                Width = width,
                DropDownStyle = ComboBoxStyle.DropDownList,
                FlatStyle = FlatStyle.Flat,
                BackColor = InputBgColor,
                ForeColor = TextColor,
                Margin = new Padding(0, 3, 8, 3),
            };
        }

        private static Button StyledButton(string text, bool primary = false)
        {
            var btn = new Button
            {
                Text = text,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                Height = 30,
                Padding = new Padding(14, 4, 14, 4),
                Margin = new Padding(0, 0, 8, 8),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary ? AccentColor : PanelColor,
                ForeColor = primary ? AccentTextColor : TextColor,
                Cursor = Cursors.Hand,
            };
            btn.FlatAppearance.BorderColor = primary ? AccentColor : BorderColor;
            btn.FlatAppearance.BorderSize = 1;
            RoundedUi.RoundOnResize(btn, 12);
            return btn;
        }

        private static Label PlainLabel(string text, Color? color = null)
        {
            return new Label { Text = text, AutoSize = true, ForeColor = color ?? TextColor, Margin = new Padding(0, 6, 6, 0) };
        }

        private static FlowLayoutPanel Row()
        {
            return new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                BackColor = Color.Transparent,
                Margin = new Padding(0, 0, 0, 4),
            };
        }

        private static FlowLayoutPanel Card(string title, out FlowLayoutPanel body)
        {
            var outer = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.TopDown,
                WrapContents = false,
                AutoSize = false,
                Size = new Size(285, 450),
                BackColor = CardColor,
                Padding = new Padding(16),
                Margin = new Padding(0, 0, 12, 12),
                BorderStyle = BorderStyle.None,
            };
            RoundedUi.RoundOnResize(outer, 18);
            outer.Controls.Add(SectionLabel(title));
            body = outer;
            return outer;
        }

        // ---- layout ----

        private void BuildUi()
        {
            var root = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                BackColor = BgColor,
            };
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));

            _responsiveCards = new FlowLayoutPanel
            {
                FlowDirection = FlowDirection.LeftToRight,
                WrapContents = true,
                AutoSize = false,
                BackColor = BgColor,
                Padding = new Padding(0, 2, 0, 0),
            };
            var deviceCard = BuildDeviceCard();
            deviceCard.Height = 450;
            var frontCard = BuildChannelCard("Front", FrontChannelNames, _frontChecks, isFront: true);
            frontCard.Height = 450;
            var rearCard = BuildChannelCard("Rear", RearChannelNames, _rearChecks, isFront: false);
            rearCard.Height = 450;
            _responsiveCards.Controls.Add(deviceCard);
            _responsiveCards.Controls.Add(frontCard);
            _responsiveCards.Controls.Add(rearCard);

            _cardsViewport = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = false,
                BackColor = BgColor,
            };
            _cardsViewport.Controls.Add(_responsiveCards);
            _cardsViewport.Resize += (s, e) => UpdateResponsiveCards();

            root.RowStyles.Clear();
            root.RowCount = 3;
            root.RowStyles.Add(new RowStyle(SizeType.Absolute, 470F));
            root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
            root.RowStyles.Add(new RowStyle(SizeType.Percent, 100F));
            root.Controls.Add(_cardsViewport, 0, 0);

            var actionsRow = Row();
            actionsRow.Margin = new Padding(0, 0, 0, 12);
            _btnReadConfig = StyledButton("\u2193  Read Config", primary: true);
            _btnReadConfig.Enabled = false;
            _btnReadConfig.Click += BtnReadConfig_Click;
            _btnWriteConfig = StyledButton("\u2191  Write Config", primary: true);
            _btnWriteConfig.Enabled = false;
            _btnWriteConfig.Click += BtnWriteConfig_Click;
            var btnRestoreDefaults = StyledButton("\u21BB  Restore Defaults (check all)");
            btnRestoreDefaults.Click += (s, e) =>
            {
                _suppressChecklistEvents = true;
                foreach (var chk in _frontChecks) chk.Checked = true;
                foreach (var chk in _rearChecks) chk.Checked = true;
                _suppressChecklistEvents = false;
                AppendLog("Restored defaults: all channels checked (outage detection disabled for everything).");
            };
            _btnOpenBackups = StyledButton("Open backup log");
            _btnOpenBackups.Click += (s, e) => _backup.OpenContainingFolder();
            actionsRow.Controls.AddRange(new Control[] { _btnReadConfig, _btnWriteConfig, btnRestoreDefaults, _btnOpenBackups });
            actionsRow.Dock = DockStyle.Fill;
            root.Controls.Add(actionsRow, 0, 1);

            var logPanel = new Panel { Dock = DockStyle.Fill, BackColor = BgColor };
            var logLabel = SectionLabel("Activity Log");
            logLabel.Dock = DockStyle.Top;
            _txtLog = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Font = new Font("Consolas", 9F),
                BackColor = InputBgColor,
                ForeColor = Color.FromArgb(100, 220, 156),
                BorderStyle = BorderStyle.FixedSingle,
            };
            // Best-effort dark native scrollbar (supported by recent Windows builds).
            _txtLog.HandleCreated += (s, e) => DarkScrollbar.Apply(_txtLog.Handle);
            logPanel.Controls.Add(_txtLog);
            logPanel.Controls.Add(logLabel);
            root.Controls.Add(logPanel, 0, 2);

            Controls.Add(root);

            var footer = new Label
            {
                Text = "Saab 93 NG Led Enabler  \u00B7  BCR Auto",
                AutoSize = true,
                ForeColor = SubTextColor,
                Dock = DockStyle.Bottom,
                Padding = new Padding(0, 6, 0, 0),
            };
            Controls.Add(footer);
            Shown += (s, e) => UpdateResponsiveCards();
        }

        private void UpdateResponsiveCards()
        {
            if (_cardsViewport == null || _responsiveCards == null) return;
            int available = Math.Max(750, _cardsViewport.ClientSize.Width - 4);
            int cardWidth = Math.Max(245, (available - 36) / 3);
            _responsiveCards.SuspendLayout();
            foreach (Control card in _responsiveCards.Controls)
            {
                card.Width = cardWidth;
                card.Height = 450;
            }
            _responsiveCards.Size = new Size(available, 465);
            _responsiveCards.ResumeLayout(true);

            // The WinForms viewport itself never scrolls. Give the log the
            // remaining height; resize to preserve the cards on small screens.
            if (_cardsViewport.Parent is TableLayoutPanel root)
            {
                int usable = Math.Max(1, root.ClientSize.Height);
                root.RowStyles[0].Height = Math.Min(470, Math.Max(350, usable - 175));
                int height = Math.Max(345, (int)root.RowStyles[0].Height - 15);
                foreach (Control card in _responsiveCards.Controls) card.Height = height;
                _responsiveCards.Height = height + 8;
            }
        }

        private FlowLayoutPanel BuildDeviceCard()
        {
            var card = Card("Device", out var body);

            var modeRow = Row();
            _rbJ2534 = new RadioButton { Text = "J2534 (MDI2 / other)", AutoSize = true, ForeColor = TextColor, Checked = true, Margin = new Padding(0, 4, 16, 4) };
            _rbTech2 = new RadioButton { Text = "Tech2", AutoSize = true, ForeColor = TextColor, Margin = new Padding(0, 4, 0, 4) };
            _rbJ2534.CheckedChanged += (s, e) => { if (_rbJ2534.Checked) SetDeviceMode(DeviceMode.J2534); };
            _rbTech2.CheckedChanged += (s, e) => { if (_rbTech2.Checked) SetDeviceMode(DeviceMode.Tech2); };
            modeRow.Controls.AddRange(new Control[] { _rbJ2534, _rbTech2 });
            body.Controls.Add(modeRow);

            // J2534 (MDI2 / other, excludes Tech2) - basic
            _j2534FieldsPanel = new Panel { AutoSize = true, Margin = new Padding(0), Visible = true };
            var j2534Row = Row();
            _cmbJ2534Device = StyledCombo(220);
            j2534Row.Controls.Add(_cmbJ2534Device);
            _j2534FieldsPanel.Controls.Add(j2534Row);
            _j2534FieldsPanel.Size = j2534Row.PreferredSize;
            body.Controls.Add(_j2534FieldsPanel);

            // Tech2 - its own standalone box
            _tech2FieldsPanel = new Panel { AutoSize = true, Margin = new Padding(0), Visible = false };
            var tech2Row = Row();
            _cmbTech2Device = StyledCombo(220);
            tech2Row.Controls.Add(_cmbTech2Device);
            _tech2FieldsPanel.Controls.Add(tech2Row);
            _tech2FieldsPanel.Size = tech2Row.PreferredSize;
            body.Controls.Add(_tech2FieldsPanel);

            // Advanced: rescan/browse (works for either combo, context-aware) + protocol
            _j2534AdvancedPanel = new Panel { AutoSize = true, Margin = new Padding(0), Visible = false };
            var j2534AdvRow = Row();
            var btnRescanJ2534 = StyledButton("Rescan");
            btnRescanJ2534.Click += (s, e) => PopulateJ2534Devices();
            var btnBrowseJ2534 = StyledButton("Browse...");
            btnBrowseJ2534.Click += BtnBrowseJ2534Dll_Click;
            var btnTestVoltage = StyledButton("Test battery voltage");
            btnTestVoltage.Click += BtnTestVoltage_Click;
            j2534AdvRow.Controls.AddRange(new Control[] { btnRescanJ2534, btnBrowseJ2534, btnTestVoltage });
            _j2534AdvancedPanel.Controls.Add(j2534AdvRow);

            var protocolRow = Row();
            protocolRow.Controls.Add(PlainLabel("Protocol:", SubTextColor));
            _cmbJ2534Protocol = StyledCombo(220);
            _cmbJ2534Protocol.Items.Add(new J2534ProtocolOption("SW_CAN_PS (0x8008)", J2534Protocol.SW_CAN_PS));
            _cmbJ2534Protocol.Items.Add(new J2534ProtocolOption("SW_ISO15765_PS (0x8007)", J2534Protocol.SW_ISO15765_PS));
            _cmbJ2534Protocol.Items.Add(new J2534ProtocolOption("CAN (0x05)", J2534Protocol.CAN));
            _cmbJ2534Protocol.SelectedIndex = 0;
            protocolRow.Controls.Add(_cmbJ2534Protocol);
            _j2534AdvancedPanel.Controls.Add(protocolRow);
            _j2534AdvancedPanel.Size = new Size(
                Math.Max(j2534AdvRow.PreferredSize.Width, protocolRow.PreferredSize.Width),
                j2534AdvRow.PreferredSize.Height + protocolRow.PreferredSize.Height);
            body.Controls.Add(_j2534AdvancedPanel);

            var connectRow = Row();
            _btnConnect = StyledButton("Connect", primary: true);
            _btnConnect.Click += BtnConnect_Click;
            connectRow.Controls.Add(_btnConnect);
            body.Controls.Add(connectRow);

            _btnToggleAdvanced = StyledButton("Show advanced \u25BE");
            _btnToggleAdvanced.Click += BtnToggleAdvanced_Click;
            body.Controls.Add(_btnToggleAdvanced);

            var vehicleHeader = SectionLabel("Vehicle Info");
            vehicleHeader.Margin = new Padding(0, 16, 0, 8);
            body.Controls.Add(vehicleHeader);
            var battRow = Row();
            battRow.Controls.Add(PlainLabel("Battery:", SubTextColor));
            _lblBattery = new Label { Text = "\u2014", AutoSize = true, ForeColor = TextColor, Margin = new Padding(4, 6, 0, 0) };
            battRow.Controls.Add(_lblBattery);
            body.Controls.Add(battRow);

            return card;
        }

        private FlowLayoutPanel BuildChannelCard(string title, string[] names, CheckBox[] checkArray, bool isFront)
        {
            var card = Card(title, out var body);
            for (int i = 0; i < names.Length; i++)
            {
                var chk = new RoundedToggleCheckBox
                {
                    Text = names[i],
                    Checked = false,
                    Width = 245,
                    Height = 36,
                    ForeColor = TextColor,
                    BackColor = CardColor,
                    Margin = new Padding(0, 3, 0, 3),
                };
                chk.CheckedChanged += (s, e) => OnChannelCheckChanged();
                checkArray[i] = chk;
                body.Controls.Add(chk);
            }

            var btnDisableAll = StyledButton("Disable All Checks");
            btnDisableAll.Margin = new Padding(0, 10, 0, 0);
            btnDisableAll.Click += (s, e) =>
            {
                _suppressChecklistEvents = true;
                foreach (var chk in checkArray) chk.Checked = true;
                _suppressChecklistEvents = false;
                AppendLog($"{title}: bulb-out checks disabled for all channels (green = disabled). Click Write Config to save.");
            };
            body.Controls.Add(btnDisableAll);
            body.Controls.Add(new Label
            {
                Text = "GREEN: bulb check OFF   |   RED: bulb check ON",
                AutoSize = true,
                ForeColor = SubTextColor,
                Font = new Font("Segoe UI", 8F),
                Margin = new Padding(0, 8, 0, 0),
            });

            return card;
        }

        private void SetDeviceMode(DeviceMode mode)
        {
            _mode = mode;
            _j2534FieldsPanel.Visible = mode == DeviceMode.J2534;
            _tech2FieldsPanel.Visible = mode == DeviceMode.Tech2;
            _j2534AdvancedPanel.Visible = _advancedVisible;
        }

        private void BtnToggleAdvanced_Click(object sender, EventArgs e)
        {
            _advancedVisible = !_advancedVisible;
            _btnToggleAdvanced.Text = _advancedVisible ? "Hide advanced \u25B4" : "Show advanced \u25BE";
            _j2534AdvancedPanel.Visible = _advancedVisible;
        }

        private void PopulateJ2534Devices()
        {
            var all = J2534Registry.FindInstalledDevices();

            _cmbJ2534Device.Items.Clear();
            foreach (J2534DeviceInfo device in all.Where(d => !d.Name.Contains("Tech2", StringComparison.OrdinalIgnoreCase)))
            {
                _cmbJ2534Device.Items.Add(device);
            }
            if (_cmbJ2534Device.Items.Count > 0) _cmbJ2534Device.SelectedIndex = 0;

            _cmbTech2Device.Items.Clear();
            foreach (J2534DeviceInfo device in all.Where(d => d.Name.Contains("Tech2", StringComparison.OrdinalIgnoreCase)))
            {
                _cmbTech2Device.Items.Add(device);
            }
            if (_cmbTech2Device.Items.Count > 0) _cmbTech2Device.SelectedIndex = 0;

            if (all.Count == 0)
            {
                AppendLog("No J2534 devices found. Use \"Browse...\" (advanced options) to point at the vendor DLL directly.");
            }
        }

        private void BtnBrowseJ2534Dll_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select the J2534 vendor DLL",
                Filter = "DLL files (*.dll)|*.dll|All files (*.*)|*.*",
            };
            if (dialog.ShowDialog(this) == DialogResult.OK)
            {
                var manual = new J2534DeviceInfo { Name = "(manual)", Vendor = "", DllPath = dialog.FileName };
                ComboBox target = _mode == DeviceMode.Tech2 ? _cmbTech2Device : _cmbJ2534Device;
                target.Items.Insert(0, manual);
                target.SelectedIndex = 0;
            }
        }

        private void BtnConnect_Click(object sender, EventArgs e)
        {
            bool currentlyConnected = _j2534Channel != null;

            if (currentlyConnected)
            {
                DisconnectCurrent();
                _btnConnect.Text = "Connect";
                _btnReadConfig.Enabled = false;
                _btnWriteConfig.Enabled = false;
                _lblBattery.Text = "\u2014";
                AppendLog("Disconnected.");
                return;
            }

            try
            {
                ComboBox source = _mode == DeviceMode.Tech2 ? _cmbTech2Device : _cmbJ2534Device;
                if (source.SelectedItem is not J2534DeviceInfo device)
                {
                    MessageBox.Show(this, "Select a device first (or Browse for the DLL).", "No device selected");
                    return;
                }
                var protoOption = (J2534ProtocolOption)_cmbJ2534Protocol.SelectedItem;
                AppendLog($"Opening J2534 device: {device.Name} ({device.DllPath})");
                _j2534Channel = new J2534GmlanChannel(device.DllPath, protoOption.Id);
                _j2534Channel.Open();
                _didService = new GmlanDidService(_j2534Channel);
                AppendLog("J2534 device open.");

                try
                {
                    double v = _j2534Channel.ReadBatteryVoltage();
                    _lblBattery.Text = $"{v:F2} V";
                    AppendLog($"Battery voltage: {v:F2} V");
                }
                catch (Exception vex)
                {
                    AppendLog("Could not read battery voltage: " + vex.Message);
                }

                _btnConnect.Text = "Disconnect";
                _btnReadConfig.Enabled = true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, "Failed to connect: " + ex.Message, "Connection error");
                DisconnectCurrent();
            }
        }

        private void DisconnectCurrent()
        {
            if (_j2534Channel != null)
            {
                _j2534Channel.Dispose();
                _j2534Channel = null;
            }
            _didService = null;
        }

        private void BtnTestVoltage_Click(object sender, EventArgs e)
        {
            if (_j2534Channel == null)
            {
                MessageBox.Show(this, "Connect first.", "Not connected");
                return;
            }
            try
            {
                double volts = _j2534Channel.ReadBatteryVoltage();
                AppendLog($"Battery voltage: {volts:F2} V");
                MessageBox.Show(this, $"Battery voltage: {volts:F2} V", "Voltage test");
            }
            catch (Exception ex)
            {
                AppendLog("ERROR: " + ex.Message);
                MessageBox.Show(this, "Voltage read failed: " + ex.Message, "Voltage test failed");
            }
        }

        // ---- Read / Write config (both ECUs at once) ----

        private void BtnReadConfig_Click(object sender, EventArgs e)
        {
            if (!EnsureReadyForCommunication()) return;
            AppendLog("Trying to read car config...");

            bool ok = true;
            ok &= TryReadUec();
            ok &= TryReadRec();

            _btnWriteConfig.Enabled = ok;
        }

        private bool TryReadUec()
        {
            try
            {
                AppendLog("Reading UEC configuration...");
                _didService.SelectTarget(EcuTarget.Uec);
                DidDefinition did = EcuTarget.Uec.KnownDids.First(d => d.Description.Contains("Bulb Outage"));
                DidReadResult result = _didService.ReadDid(did.Id);
                _frontRawValue = result.Value;
                _backup.Log(EcuTarget.Uec.Name, did.Id, result.Value, "read");
                AppendLog($"UEC raw: {result.RawHex}");

                if (result.Value.Length != 4)
                    throw new InvalidOperationException($"UEC expected 4 config bytes, got {result.Value.Length}.");
                {
                    var (parking, fog, low, high, turn) = GmlanBitMap.DecodeUecMask(result.Value[1]);
                    _suppressChecklistEvents = true;
                    _frontChecks[0].Checked = parking;
                    _frontChecks[1].Checked = fog;
                    _frontChecks[2].Checked = low;
                    _frontChecks[3].Checked = high;
                    _frontChecks[4].Checked = turn;
                    _suppressChecklistEvents = false;
                }
                return true;
            }
            catch (Exception ex)
            {
                AppendLog("ERROR reading UEC: " + ex.Message);
                return false;
            }
        }

        private bool TryReadRec()
        {
            try
            {
                AppendLog("Reading REC configuration...");
                _didService.SelectTarget(EcuTarget.Rec);
                DidDefinition did = EcuTarget.Rec.KnownDids.First(d => d.Description.Contains("Bulb Outage"));
                DidReadResult result = _didService.ReadDid(did.Id);
                _rearRawValue = result.Value;
                _backup.Log(EcuTarget.Rec.Name, did.Id, result.Value, "read");
                AppendLog($"REC raw: {result.RawHex}");

                if (result.Value.Length != 6)
                    throw new InvalidOperationException($"REC expected 6 config bytes, got {result.Value.Length}.");
                {
                    var (tail, fog, stop, reverse, plate, turn) = GmlanBitMap.DecodeRecMask(result.Value[1], result.Value[2]);
                    _suppressChecklistEvents = true;
                    _rearChecks[0].Checked = tail;
                    _rearChecks[1].Checked = fog;
                    _rearChecks[2].Checked = stop;
                    _rearChecks[3].Checked = reverse;
                    _rearChecks[4].Checked = plate;
                    _rearChecks[5].Checked = turn;
                    _suppressChecklistEvents = false;
                }
                return true;
            }
            catch (Exception ex)
            {
                AppendLog("ERROR reading REC: " + ex.Message);
                return false;
            }
        }

        private void OnChannelCheckChanged()
        {
            // Live preview only - the byte(s) are rebuilt at write time.
        }

        private void BtnWriteConfig_Click(object sender, EventArgs e)
        {
            if (!EnsureReadyForCommunication()) return;
            if (_frontRawValue.Length == 0 || _rearRawValue.Length == 0)
            {
                MessageBox.Show(this, "Read the current config first.", "Nothing to write");
                return;
            }

            DialogResult confirm = MessageBox.Show(
                this,
                "Write the checked configuration to both UEC (Front) and REC (Rear)?\n\n" +
                "Original values were already logged to the backup file. Proceed?",
                "Confirm write",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes) return;

            _btnWriteConfig.Enabled = false;
            bool uecOk = TryWriteUec();
            bool recOk = TryWriteRec();
            bool bothOk = uecOk && recOk;
            if (bothOk)
                MessageBox.Show(this, "UEC and REC verified by re-reading both DIDs.", "Verified");
            else
                MessageBox.Show(this, "One or both writes failed verification. Read Config again and inspect the log. Do not assume both ECUs were updated.", "Verification failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            // Require a fresh read before the next write, even after partial failure.
            _frontRawValue = Array.Empty<byte>();
            _rearRawValue = Array.Empty<byte>();
        }

        private bool TryWriteUec()
        {
            try
            {
                byte newB0 = GmlanBitMap.EncodeUecMask(
                    _frontChecks[0].Checked, _frontChecks[1].Checked, _frontChecks[2].Checked,
                    _frontChecks[3].Checked, _frontChecks[4].Checked);

                byte[] newValue = (byte[])_frontRawValue.Clone();
                newValue[1] = (byte)((newValue[1] & ~0xF1) | newB0);

                _didService.SelectTarget(EcuTarget.Uec);
                DidDefinition did = EcuTarget.Uec.KnownDids.First(d => d.Description.Contains("Bulb Outage"));
                _didService.WriteDid(did.Id, newValue);
                _backup.Log(EcuTarget.Uec.Name, did.Id, newValue, "write (value sent)");
                byte[] verified = _didService.ReadDid(did.Id).Value;
                _backup.Log(EcuTarget.Uec.Name, did.Id, verified, "verify read");
                if (!verified.SequenceEqual(newValue))
                    throw new InvalidOperationException($"UEC mismatch: expected {Convert.ToHexString(newValue)}, read {Convert.ToHexString(verified)}");
                AppendLog($"UEC verified: {Convert.ToHexString(verified)}");
                return true;
            }
            catch (Exception ex)
            {
                AppendLog("ERROR writing UEC: " + ex.Message);
                return false;
            }
        }

        private bool TryWriteRec()
        {
            try
            {
                var (b1, _) = GmlanBitMap.EncodeRecMask(
                    _rearChecks[0].Checked, _rearChecks[1].Checked, _rearChecks[2].Checked,
                    _rearChecks[3].Checked, _rearChecks[4].Checked, _rearChecks[5].Checked);

                byte[] newValue = (byte[])_rearRawValue.Clone();
                // [0] is PWM; [1] carries five flags; [2] carries rear turn signals.
                // Preserve every unrelated bit and all remaining config bytes.
                newValue[1] = (byte)((newValue[1] & ~0xF8) | b1);
                newValue[2] = _rearChecks[5].Checked
                    ? (byte)(newValue[2] | 0x08)
                    : (byte)(newValue[2] & ~0x08);

                _didService.SelectTarget(EcuTarget.Rec);
                DidDefinition did = EcuTarget.Rec.KnownDids.First(d => d.Description.Contains("Bulb Outage"));
                _didService.WriteDid(did.Id, newValue);
                _backup.Log(EcuTarget.Rec.Name, did.Id, newValue, "write (value sent)");
                byte[] verified = _didService.ReadDid(did.Id).Value;
                _backup.Log(EcuTarget.Rec.Name, did.Id, verified, "verify read");
                if (!verified.SequenceEqual(newValue))
                    throw new InvalidOperationException($"REC mismatch: expected {Convert.ToHexString(newValue)}, read {Convert.ToHexString(verified)}");
                AppendLog($"REC verified: {Convert.ToHexString(verified)}");
                return true;
            }
            catch (Exception ex)
            {
                AppendLog("ERROR writing REC: " + ex.Message);
                return false;
            }
        }

        private bool EnsureReadyForCommunication()
        {
            if (_j2534Channel != null && _didService != null) return true;
            MessageBox.Show(this, "Connect to the adapter first.", "Not connected");
            return false;
        }

        private void AppendLog(string line)
        {
            if (_txtLog.InvokeRequired)
            {
                _txtLog.Invoke(new Action(() => AppendLog(line)));
                return;
            }
            _txtLog.AppendText($"{DateTime.Now:HH:mm:ss} {line}{Environment.NewLine}");
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            DisconnectCurrent();
            base.OnFormClosing(e);
        }
    }
}
