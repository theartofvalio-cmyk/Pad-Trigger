using Microsoft.Win32;
using SharpDX.DirectInput;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace PadTrigger
{
    public partial class Form1 : Form
    {
        private NotifyIcon trayIcon;
        private ContextMenuStrip trayMenu;
        private Icon appIcon = SystemIcons.Application;

        private PictureBox titleIconPictureBox;
        private CheckBox startWithWindowsCheckBox;
        private CheckBox showControllersCheckBox;
        private Button detectOtherDevicesButton;
        private Button hiddenDevicesButton;
        private Label titleLabel;
        private Button lightThemeButton;
        private Button darkThemeButton;
        private Button aboutButton;
        private DataGridView controllerGrid;
        private ContextMenuStrip controllerRightClickMenu;

        private string rightClickedControllerDeviceId = "";

        private System.Windows.Forms.Timer deviceChangeDelayTimer;
        private System.Threading.Timer controllerPollingTimer;
        private System.Threading.Timer bluetoothPollingTimer;
        private int controllerScanInProgress = 0;
        private int bluetoothScanInProgress = 0;

        private bool allowExit = false;
        private bool hasCompletedInitialScan = false;
        private bool startupInvisibleRequested = false;
        private bool showControllers = true;
        private bool showOtherDevices = false;

        private string currentTheme = "Dark";

        private const string AppName = "PadTrigger";
        private const string RunRegistryPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
        private const string YoutubeSubscribeUrl = "https://www.youtube.com/@SPYBGWTVR?sub_confirmation=1";

        private const int WM_DEVICECHANGE = 0x0219;
        private const int DBT_DEVICEARRIVAL = 0x8000;
        private const int DBT_DEVICEREMOVECOMPLETE = 0x8004;

        private const int DEVICE_NOTIFY_WINDOW_HANDLE = 0x00000000;
        private const int DBT_DEVTYP_DEVICEINTERFACE = 0x00000005;

        private static readonly Guid GUID_DEVINTERFACE_HID =
            new Guid("4D1E55B2-F16F-11CF-88CB-001111000030");

        private IntPtr deviceNotificationHandle = IntPtr.Zero;

        private readonly Dictionary<string, ControllerItem> knownControllers = new Dictionary<string, ControllerItem>();
        private readonly HashSet<string> removedOtherDeviceIds = new HashSet<string>();

        private readonly string configPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "config.json");

        [StructLayout(LayoutKind.Sequential)]
        private struct DEV_BROADCAST_DEVICEINTERFACE
        {
            public int dbcc_size;
            public int dbcc_devicetype;
            public int dbcc_reserved;
            public Guid dbcc_classguid;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr RegisterDeviceNotification(
            IntPtr hRecipient,
            ref DEV_BROADCAST_DEVICEINTERFACE notificationFilter,
            int flags
        );

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnregisterDeviceNotification(IntPtr handle);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool DestroyIcon(IntPtr hIcon);

        public Form1()
        {
            InitializeComponent();

            startupInvisibleRequested = ShouldStartInvisibleOnBoot();

            LoadConfig();

            SetupMainWindow();

            appIcon = LoadAppIcon();
            Icon = appIcon;

            SetupTrayIcon();
            SetupControls();
            SetupControllerRightClickMenu();
            SetupDeviceChangeDelayTimer();
            SetupControllerPollingTimer();

            RegisterForHidDeviceNotifications();

            RemoveOldRegistryStartupEntry();
            startWithWindowsCheckBox.Checked = IsStartWithWindowsEnabled();

            ApplyThemeToMainWindow();

            RefreshControllers();
        }

        private Icon LoadAppIcon()
        {
            Image embeddedPng = LoadEmbeddedPngImage();

            if (embeddedPng != null)
            {
                try
                {
                    using Bitmap originalBitmap = new Bitmap(embeddedPng);
                    using Bitmap resizedBitmap = new Bitmap(256, 256);

                    using (Graphics graphics = Graphics.FromImage(resizedBitmap))
                    {
                        graphics.Clear(Color.Transparent);
                        graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        graphics.SmoothingMode = SmoothingMode.AntiAlias;
                        graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        graphics.DrawImage(originalBitmap, 0, 0, 256, 256);
                    }

                    IntPtr iconHandle = resizedBitmap.GetHicon();

                    try
                    {
                        return (Icon)Icon.FromHandle(iconHandle).Clone();
                    }
                    finally
                    {
                        DestroyIcon(iconHandle);
                    }
                }
                finally
                {
                    embeddedPng.Dispose();
                }
            }

            try
            {
                string icoPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "pt.ico");

                if (File.Exists(icoPath))
                    return new Icon(icoPath);

                Icon extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

                if (extractedIcon != null)
                    return (Icon)extractedIcon.Clone();
            }
            catch
            {
                // Ignore icon load errors.
            }

            return SystemIcons.Application;
        }

        private Image LoadTitleImage()
        {
            Image embeddedPng = LoadEmbeddedPngImage();

            if (embeddedPng != null)
                return embeddedPng;

            try
            {
                Icon extractedIcon = Icon.ExtractAssociatedIcon(Application.ExecutablePath);

                if (extractedIcon != null)
                    return extractedIcon.ToBitmap();
            }
            catch
            {
                // Ignore icon load errors.
            }

            return SystemIcons.Application.ToBitmap();
        }

        private Image LoadEmbeddedPngImage()
        {
            try
            {
                Assembly assembly = Assembly.GetExecutingAssembly();

                string resourceName = assembly
                    .GetManifestResourceNames()
                    .FirstOrDefault(name =>
                        name.EndsWith(".pt.png", StringComparison.OrdinalIgnoreCase) ||
                        name.EndsWith("pt.png", StringComparison.OrdinalIgnoreCase));

                if (string.IsNullOrWhiteSpace(resourceName))
                    return null;

                using Stream stream = assembly.GetManifestResourceStream(resourceName);

                if (stream == null)
                    return null;

                return new Bitmap(stream);
            }
            catch
            {
                return null;
            }
        }

        private Image CreateYoutubeIconImage(int width, int height)
        {
            Bitmap bitmap = new Bitmap(width, height);

            using (Graphics graphics = Graphics.FromImage(bitmap))
            {
                graphics.Clear(Color.Transparent);
                graphics.SmoothingMode = SmoothingMode.AntiAlias;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;

                Rectangle redBox = new Rectangle(0, 0, width - 1, height - 1);

                using (SolidBrush redBrush = new SolidBrush(Color.FromArgb(255, 0, 0)))
                {
                    graphics.FillRectangle(redBrush, redBox);
                }

                Point[] playTriangle =
                {
                    new Point(width / 2 - 13, height / 2 - 20),
                    new Point(width / 2 - 13, height / 2 + 20),
                    new Point(width / 2 + 23, height / 2)
                };

                using (SolidBrush whiteBrush = new SolidBrush(Color.White))
                {
                    graphics.FillPolygon(whiteBrush, playTriangle);
                }
            }

            return bitmap;
        }

        private void SetupMainWindow()
        {
            Text = "Pad Trigger";
            Width = 820;
            Height = 600;
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;

            // Keep the app as a normal window internally.
            // Startup hiding happens after the form has fully initialized,
            // because starting hidden/minimized too early broke device detection on some PCs.
        }

        private void SetupControls()
        {
            titleIconPictureBox = new PictureBox();
            titleIconPictureBox.Left = 24;
            titleIconPictureBox.Top = 16;
            titleIconPictureBox.Width = 112;
            titleIconPictureBox.Height = 112;
            titleIconPictureBox.SizeMode = PictureBoxSizeMode.Zoom;
            titleIconPictureBox.Image = LoadTitleImage();
            Controls.Add(titleIconPictureBox);

            titleLabel = new Label();
            titleLabel.Text = "Pad Trigger";
            titleLabel.Font = new Font("Segoe UI", 22, FontStyle.Bold);
            titleLabel.AutoSize = true;
            titleLabel.Left = 155;
            titleLabel.Top = 48;
            Controls.Add(titleLabel);

            lightThemeButton = new Button();
            lightThemeButton.Text = "Light";
            lightThemeButton.Left = 540;
            lightThemeButton.Top = 25;
            lightThemeButton.Width = 78;
            lightThemeButton.Height = 32;
            lightThemeButton.Click += (s, e) => SetTheme("Light");
            Controls.Add(lightThemeButton);

            darkThemeButton = new Button();
            darkThemeButton.Text = "Dark";
            darkThemeButton.Left = 628;
            darkThemeButton.Top = 25;
            darkThemeButton.Width = 78;
            darkThemeButton.Height = 32;
            darkThemeButton.Click += (s, e) => SetTheme("Dark");
            Controls.Add(darkThemeButton);

            aboutButton = new Button();
            aboutButton.Text = "About";
            aboutButton.Left = 716;
            aboutButton.Top = 25;
            aboutButton.Width = 78;
            aboutButton.Height = 32;
            aboutButton.Click += (s, e) => ShowAboutWindow();
            Controls.Add(aboutButton);

            startWithWindowsCheckBox = new CheckBox();
            startWithWindowsCheckBox.Text = "Start with Windows minimized";
            startWithWindowsCheckBox.AutoSize = true;
            startWithWindowsCheckBox.Left = 24;
            startWithWindowsCheckBox.Top = 145;
            startWithWindowsCheckBox.CheckedChanged += StartWithWindowsCheckBox_CheckedChanged;
            Controls.Add(startWithWindowsCheckBox);

            showControllersCheckBox = new CheckBox();
            showControllersCheckBox.Text = "Controllers";
            showControllersCheckBox.AutoSize = true;
            showControllersCheckBox.Left = 275;
            showControllersCheckBox.Top = 145;
            showControllersCheckBox.Checked = showControllers;
            showControllersCheckBox.CheckedChanged += ShowControllersCheckBox_CheckedChanged;
            Controls.Add(showControllersCheckBox);

            detectOtherDevicesButton = new Button();
            detectOtherDevicesButton.Text = "Bluetooth Devices";
            detectOtherDevicesButton.Left = 600;
            detectOtherDevicesButton.Top = 102;
            detectOtherDevicesButton.Width = 194;
            detectOtherDevicesButton.Height = 32;
            detectOtherDevicesButton.Click += (s, e) => ShowDetectOtherDeviceWindow();
            Controls.Add(detectOtherDevicesButton);

            hiddenDevicesButton = new Button();
            hiddenDevicesButton.Text = "Hidden Devices";
            hiddenDevicesButton.Left = 600;
            hiddenDevicesButton.Top = 140;
            hiddenDevicesButton.Width = 194;
            hiddenDevicesButton.Height = 32;
            hiddenDevicesButton.Click += (s, e) => ShowHiddenDevicesWindow();
            Controls.Add(hiddenDevicesButton);

            controllerGrid = new DataGridView();
            controllerGrid.Left = 24;
            controllerGrid.Top = 190;
            controllerGrid.Width = 770;
            controllerGrid.Height = 350;

            controllerGrid.AllowUserToAddRows = false;
            controllerGrid.AllowUserToDeleteRows = false;
            controllerGrid.RowHeadersVisible = false;
            controllerGrid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            controllerGrid.MultiSelect = false;
            controllerGrid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            controllerGrid.ReadOnly = true;

            controllerGrid.Columns.Add("ControllerName", "Controller Name");
            controllerGrid.Columns.Add("Status", "Status");

            DataGridViewTextBoxColumn hiddenIdColumn = new DataGridViewTextBoxColumn();
            hiddenIdColumn.Name = "DeviceId";
            hiddenIdColumn.HeaderText = "Device ID";
            hiddenIdColumn.Visible = false;
            controllerGrid.Columns.Add(hiddenIdColumn);

            controllerGrid.CellMouseDown += ControllerGrid_CellMouseDown;

            Controls.Add(controllerGrid);
        }

        private void SetupControllerRightClickMenu()
        {
            controllerRightClickMenu = new ContextMenuStrip();
            controllerRightClickMenu.ShowImageMargin = false;
            controllerRightClickMenu.ShowCheckMargin = false;

            ToolStripMenuItem renameItem = new ToolStripMenuItem("Rename");
            renameItem.Click += (s, e) => RenameSelectedController();

            ToolStripMenuItem editActionsItem = new ToolStripMenuItem("Edit Actions");
            editActionsItem.Click += (s, e) => EditActionsForSelectedController();

            ToolStripMenuItem hideDeviceItem = new ToolStripMenuItem("Hide Device");
            hideDeviceItem.Click += (s, e) => HideSelectedDevice();

            controllerRightClickMenu.Items.Add(renameItem);
            controllerRightClickMenu.Items.Add(editActionsItem);
            controllerRightClickMenu.Items.Add(new ToolStripSeparator());
            controllerRightClickMenu.Items.Add(hideDeviceItem);

            ApplyThemeToToolStrip(controllerRightClickMenu);
        }

        private string GetControllerDeviceIdForMenuAction()
        {
            if (!string.IsNullOrWhiteSpace(rightClickedControllerDeviceId))
                return rightClickedControllerDeviceId;

            if (controllerGrid.SelectedRows.Count > 0)
                return controllerGrid.SelectedRows[0].Cells["DeviceId"].Value?.ToString() ?? "";

            return "";
        }

        private void HideSelectedDevice()
        {
            string deviceId = GetControllerDeviceIdForMenuAction();

            if (string.IsNullOrWhiteSpace(deviceId))
                return;

            if (!knownControllers.ContainsKey(deviceId))
                return;

            // "Hide Device" from the main list means:
            // keep the device in Pad Trigger, but move it to Hidden Devices.
            // It is not permanently blocked and it is not deleted from existence.
            knownControllers[deviceId].Hidden = true;

            SaveConfig();
            DrawControllerTable();
        }

        private void ShowHiddenDevicesWindow()
        {
            Form dialog = new Form();
            dialog.Text = "Hidden Devices";
            dialog.Width = 720;
            dialog.Height = 470;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.Icon = appIcon;

            Label infoLabel = new Label();
            infoLabel.Text = "Select hidden devices to unhide or remove from this hidden list:";
            infoLabel.Left = 20;
            infoLabel.Top = 20;
            infoLabel.Width = 670;
            infoLabel.Height = 24;
            dialog.Controls.Add(infoLabel);

            CheckedListBox hiddenList = new CheckedListBox();
            hiddenList.Left = 20;
            hiddenList.Top = 55;
            hiddenList.Width = 670;
            hiddenList.Height = 295;
            hiddenList.CheckOnClick = true;
            dialog.Controls.Add(hiddenList);

            List<ControllerItem> hiddenDevices = new List<ControllerItem>();

            void ReloadHiddenList()
            {
                hiddenDevices = knownControllers.Values
                    .Where(x => x.Hidden)
                    .OrderBy(x => x.DeviceCategory)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

                hiddenList.Items.Clear();

                foreach (ControllerItem device in hiddenDevices)
                {
                    string category = string.IsNullOrWhiteSpace(device.DeviceCategory) ? "Controller" : device.DeviceCategory;
                    hiddenList.Items.Add(device.DisplayName + "  [" + category + "]", false);
                }
            }

            ReloadHiddenList();

            Button unhideButton = new Button();
            unhideButton.Text = "Unhide Selected";
            unhideButton.Left = 250;
            unhideButton.Top = 375;
            unhideButton.Width = 130;
            unhideButton.Height = 34;
            dialog.Controls.Add(unhideButton);

            Button removeButton = new Button();
            removeButton.Text = "Remove From List";
            removeButton.Left = 390;
            removeButton.Top = 375;
            removeButton.Width = 165;
            removeButton.Height = 34;
            dialog.Controls.Add(removeButton);

            Button closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.Left = 570;
            closeButton.Top = 375;
            closeButton.Width = 120;
            closeButton.Height = 34;
            closeButton.DialogResult = DialogResult.Cancel;
            dialog.Controls.Add(closeButton);

            List<ControllerItem> GetCheckedDevices()
            {
                List<ControllerItem> selected = new List<ControllerItem>();

                foreach (int checkedIndex in hiddenList.CheckedIndices)
                {
                    if (checkedIndex >= 0 && checkedIndex < hiddenDevices.Count)
                        selected.Add(hiddenDevices[checkedIndex]);
                }

                return selected;
            }

            unhideButton.Click += (s, e) =>
            {
                List<ControllerItem> selectedDevices = GetCheckedDevices();

                foreach (ControllerItem selectedDevice in selectedDevices)
                {
                    if (knownControllers.ContainsKey(selectedDevice.DeviceId))
                        knownControllers[selectedDevice.DeviceId].Hidden = false;
                }

                SaveConfig();
                DrawControllerTable();
                ReloadHiddenList();

                if (hiddenDevices.Count == 0)
                    dialog.Close();
            };

            removeButton.Click += (s, e) =>
            {
                List<ControllerItem> selectedDevices = GetCheckedDevices();

                if (selectedDevices.Count == 0)
                    return;

                DialogResult confirm = MessageBox.Show(
                    "Remove the selected device(s) from the Hidden Devices list?\n\nThis does not permanently block them. Controllers can be detected again automatically. Bluetooth devices can be added again from the Bluetooth Devices window.",
                    "Remove From Hidden List",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Information
                );

                if (confirm != DialogResult.Yes)
                    return;

                foreach (ControllerItem device in selectedDevices)
                {
                    // This only forgets the saved hidden entry.
                    // It does NOT add the device to any permanent block list.
                    if (knownControllers.ContainsKey(device.DeviceId))
                        knownControllers.Remove(device.DeviceId);

                    removedOtherDeviceIds.Remove(device.DeviceId);
                }

                SaveConfig();
                DrawControllerTable();
                ReloadHiddenList();

                if (hiddenDevices.Count == 0)
                    dialog.Close();
            };

            dialog.CancelButton = closeButton;

            ApplyThemeToControl(dialog);
            dialog.ShowDialog(this);
        }

        private void ShowDetectOtherDeviceWindow()
        {
            Form dialog = new Form();
            dialog.Text = "Bluetooth Devices";
            dialog.Width = 760;
            dialog.Height = 540;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.Icon = appIcon;

            Label infoLabel = new Label();
            infoLabel.Text = "Connected Bluetooth Devices:";
            infoLabel.Left = 20;
            infoLabel.Top = 20;
            infoLabel.Width = 700;
            infoLabel.Height = 24;
            dialog.Controls.Add(infoLabel);

            Label infoLabel2 = new Label();
            infoLabel2.Text = "Select Bluetooth device(s) to add to Pad Trigger. Press Refresh after connecting a new device.";
            infoLabel2.Left = 20;
            infoLabel2.Top = 45;
            infoLabel2.Width = 700;
            infoLabel2.Height = 24;
            dialog.Controls.Add(infoLabel2);

            CheckedListBox deviceList = new CheckedListBox();
            deviceList.Left = 20;
            deviceList.Top = 85;
            deviceList.Width = 700;
            deviceList.Height = 330;
            deviceList.CheckOnClick = true;
            dialog.Controls.Add(deviceList);

            Button refreshButton = new Button();
            refreshButton.Text = "Refresh";
            refreshButton.Left = 310;
            refreshButton.Top = 440;
            refreshButton.Width = 105;
            refreshButton.Height = 34;
            dialog.Controls.Add(refreshButton);

            Button addButton = new Button();
            addButton.Text = "Add Selected";
            addButton.Left = 425;
            addButton.Top = 440;
            addButton.Width = 140;
            addButton.Height = 34;
            dialog.Controls.Add(addButton);

            Button closeButton = new Button();
            closeButton.Text = "Close";
            closeButton.Left = 580;
            closeButton.Top = 440;
            closeButton.Width = 140;
            closeButton.Height = 34;
            closeButton.DialogResult = DialogResult.Cancel;
            dialog.Controls.Add(closeButton);

            List<ControllerItem> displayedDevices = new List<ControllerItem>();
            bool loading = false;

            void SetLoading(bool isLoading, string message = "")
            {
                loading = isLoading;
                refreshButton.Enabled = !isLoading;
                addButton.Enabled = !isLoading;

                if (!string.IsNullOrWhiteSpace(message))
                    infoLabel.Text = message;
                else
                    infoLabel.Text = isLoading ? "Scanning connected Bluetooth Devices..." : "Connected Bluetooth Devices:";
            }

            void RedrawDeviceList(List<ControllerItem> devices)
            {
                displayedDevices = devices
                    .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
                    .GroupBy(x => x.DisplayName)
                    .Select(x => x.First())
                    .OrderBy(x => knownControllers.ContainsKey(x.DeviceId) ? 1 : 0)
                    .ThenBy(x => x.DisplayName)
                    .ToList();

                deviceList.BeginUpdate();

                try
                {
                    deviceList.Items.Clear();

                    foreach (ControllerItem device in displayedDevices)
                    {
                        bool alreadyAdded = knownControllers.ContainsKey(device.DeviceId);

                        string text = device.DisplayName;

                        if (!device.Connected)
                            text += "  (disconnected)";

                        if (alreadyAdded)
                            text += "  (already added)";

                        deviceList.Items.Add(text, false);
                    }

                    if (deviceList.Items.Count == 0)
                    {
                        deviceList.Items.Add("No connected Bluetooth Devices found", false);
                    }
                }
                finally
                {
                    deviceList.EndUpdate();
                }
            }

            void LoadDevices()
            {
                if (loading)
                    return;

                SetLoading(true);

                Task.Run(() =>
                {
                    List<ControllerItem> devices = new List<ControllerItem>();

                    try
                    {
                        List<ControllerItem> controllers = GetConnectedControllersFromDirectInput();
                        devices = GetAllOtherDevicesFromPowerShell(controllers)
                            .Where(x => !string.IsNullOrWhiteSpace(x.DisplayName))
                            .ToList();
                    }
                    catch
                    {
                        devices = new List<ControllerItem>();
                    }

                    try
                    {
                        if (dialog.IsDisposed || !dialog.IsHandleCreated)
                            return;

                        dialog.BeginInvoke(new Action(() =>
                        {
                            RedrawDeviceList(devices);
                            SetLoading(false, devices.Count == 0 ? "Connected Bluetooth Devices: none found" : "Connected Bluetooth Devices:");
                        }));
                    }
                    catch
                    {
                        // Ignore if the window was closed while scanning.
                    }
                });
            }

            refreshButton.Click += (s, e) =>
            {
                LoadDevices();
            };

            addButton.Click += (s, e) =>
            {
                if (loading)
                    return;

                foreach (int checkedIndex in deviceList.CheckedIndices)
                {
                    if (checkedIndex < 0 || checkedIndex >= displayedDevices.Count)
                        continue;

                    ControllerItem device = displayedDevices[checkedIndex];

                    if (knownControllers.ContainsKey(device.DeviceId))
                        continue;

                    if (device.ConnectActionPaths.Count == 0)
                        device.ConnectActionPaths.Add("");

                    if (device.DisconnectActionPaths.Count == 0)
                        device.DisconnectActionPaths.Add("");

                    device.Hidden = false;
                    device.DeviceCategory = "Other Device";
                    removedOtherDeviceIds.Remove(device.DeviceId);
                    knownControllers[device.DeviceId] = device;
                }

                SaveConfig();
                RefreshControllers(true);
                dialog.Close();
            };

            dialog.Shown += (s, e) =>
            {
                LoadDevices();
            };

            dialog.CancelButton = closeButton;
            ApplyThemeToControl(dialog);
            dialog.ShowDialog(this);
        }

        private void RenameSelectedController()
        {
            string deviceId = GetControllerDeviceIdForMenuAction();

            if (string.IsNullOrWhiteSpace(deviceId))
                return;

            if (!knownControllers.ContainsKey(deviceId))
                return;

            string currentName = knownControllers[deviceId].DisplayName;

            string newName = ShowRenameDialog(currentName);

            if (knownControllers.ContainsKey(deviceId))
            {
                if (string.IsNullOrWhiteSpace(newName))
                    knownControllers[deviceId].DisplayName = knownControllers[deviceId].DetectedName;
                else
                    knownControllers[deviceId].DisplayName = newName.Trim();

                SaveConfig();
                DrawControllerTable();
            }
        }

        private string ShowRenameDialog(string currentName)
        {
            Form dialog = new Form();
            dialog.Text = "Rename Controller";
            dialog.Width = 500;
            dialog.Height = 230;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.Icon = appIcon;

            Label label = new Label();
            label.Text = "Controller name:";
            label.Left = 20;
            label.Top = 25;
            label.Width = 430;
            dialog.Controls.Add(label);

            TextBox textBox = new TextBox();
            textBox.Left = 20;
            textBox.Top = 60;
            textBox.Width = 440;
            textBox.Height = 28;
            textBox.Text = currentName;
            dialog.Controls.Add(textBox);

            Button okButton = new Button();
            okButton.Text = "OK";
            okButton.Left = 270;
            okButton.Top = 115;
            okButton.Width = 90;
            okButton.Height = 34;
            okButton.DialogResult = DialogResult.OK;
            dialog.Controls.Add(okButton);

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Left = 370;
            cancelButton.Top = 115;
            cancelButton.Width = 90;
            cancelButton.Height = 34;
            cancelButton.DialogResult = DialogResult.Cancel;
            dialog.Controls.Add(cancelButton);

            dialog.AcceptButton = okButton;
            dialog.CancelButton = cancelButton;

            ApplyThemeToControl(dialog);

            textBox.SelectAll();

            dialog.Shown += (s, e) =>
            {
                textBox.Focus();
            };

            DialogResult result = dialog.ShowDialog(this);

            if (result == DialogResult.OK)
                return textBox.Text.Trim();

            return currentName;
        }

        private void EditActionsForSelectedController()
        {
            string deviceId = GetControllerDeviceIdForMenuAction();

            if (string.IsNullOrWhiteSpace(deviceId))
                return;

            if (!knownControllers.ContainsKey(deviceId))
                return;

            ControllerItem controller = knownControllers[deviceId];

            Form dialog = new Form();
            dialog.Text = "Edit Actions - " + controller.DisplayName;
            dialog.Width = 920;
            dialog.Height = 700;
            dialog.MinimumSize = new Size(820, 600);
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.Sizable;
            dialog.MaximizeBox = true;
            dialog.MinimizeBox = false;
            dialog.Icon = appIcon;

            Label importantLabel = new Label();
            importantLabel.Text = "Important: Actions run from top to bottom.";
            importantLabel.Left = 20;
            importantLabel.Top = 20;
            importantLabel.Width = 800;
            importantLabel.Height = 24;
            dialog.Controls.Add(importantLabel);

            Panel separatorPanel = new Panel();
            separatorPanel.Left = 20;
            separatorPanel.Top = 52;
            separatorPanel.Width = 820;
            separatorPanel.Height = 1;
            dialog.Controls.Add(separatorPanel);

            Label noteLabel = new Label();
            noteLabel.Text = "Note: Empty fields are saved, but ignored when actions run.";
            noteLabel.Left = 20;
            noteLabel.Top = 65;
            noteLabel.Width = 800;
            noteLabel.Height = 24;
            dialog.Controls.Add(noteLabel);

            Label connectLabel = new Label();
            connectLabel.Text = "When controller connects, run:";
            connectLabel.Left = 20;
            connectLabel.Top = 105;
            connectLabel.Width = 400;
            dialog.Controls.Add(connectLabel);

            Button connectAddButton = new Button();
            connectAddButton.Text = "+";
            connectAddButton.Width = 45;
            connectAddButton.Height = 30;
            dialog.Controls.Add(connectAddButton);

            Button connectRemoveButton = new Button();
            connectRemoveButton.Text = "-";
            connectRemoveButton.Width = 45;
            connectRemoveButton.Height = 30;
            dialog.Controls.Add(connectRemoveButton);

            Panel connectPanel = new Panel();
            connectPanel.Left = 20;
            connectPanel.Top = 140;
            connectPanel.AutoScroll = true;
            connectPanel.BorderStyle = BorderStyle.FixedSingle;
            dialog.Controls.Add(connectPanel);

            Label disconnectLabel = new Label();
            disconnectLabel.Text = "When controller disconnects, run:";
            disconnectLabel.Left = 20;
            disconnectLabel.Width = 400;
            dialog.Controls.Add(disconnectLabel);

            Button disconnectAddButton = new Button();
            disconnectAddButton.Text = "+";
            disconnectAddButton.Width = 45;
            disconnectAddButton.Height = 30;
            dialog.Controls.Add(disconnectAddButton);

            Button disconnectRemoveButton = new Button();
            disconnectRemoveButton.Text = "-";
            disconnectRemoveButton.Width = 45;
            disconnectRemoveButton.Height = 30;
            dialog.Controls.Add(disconnectRemoveButton);

            Panel disconnectPanel = new Panel();
            disconnectPanel.Left = 20;
            disconnectPanel.AutoScroll = true;
            disconnectPanel.BorderStyle = BorderStyle.FixedSingle;
            dialog.Controls.Add(disconnectPanel);

            Button saveButton = new Button();
            saveButton.Text = "Save";
            saveButton.Width = 90;
            saveButton.Height = 34;
            saveButton.DialogResult = DialogResult.OK;
            dialog.Controls.Add(saveButton);

            Button cancelButton = new Button();
            cancelButton.Text = "Cancel";
            cancelButton.Width = 90;
            cancelButton.Height = 34;
            cancelButton.DialogResult = DialogResult.Cancel;
            dialog.Controls.Add(cancelButton);

            dialog.AcceptButton = saveButton;
            dialog.CancelButton = cancelButton;

            List<TextBox> connectTextBoxes = new List<TextBox>();
            List<Panel> connectRows = new List<Panel>();

            List<TextBox> disconnectTextBoxes = new List<TextBox>();
            List<Panel> disconnectRows = new List<Panel>();

            void LayoutRows(Panel parentPanel, List<Panel> rows)
            {
                int rowWidth = parentPanel.ClientSize.Width - 25;

                if (rowWidth < 600)
                    rowWidth = 600;

                for (int i = 0; i < rows.Count; i++)
                {
                    Panel rowPanel = rows[i];

                    rowPanel.Left = 5;
                    rowPanel.Top = i * 42;
                    rowPanel.Width = rowWidth;
                    rowPanel.Height = 38;

                    TextBox textBox = rowPanel.Controls.OfType<TextBox>().First();
                    Button browseButton = rowPanel.Controls.OfType<Button>().First();

                    browseButton.Width = 115;
                    browseButton.Height = 30;
                    browseButton.Left = rowPanel.Width - browseButton.Width - 5;
                    browseButton.Top = 4;
                    browseButton.TextAlign = ContentAlignment.MiddleCenter;

                    textBox.Left = 0;
                    textBox.Top = 6;
                    textBox.Width = browseButton.Left - 12;
                }

                parentPanel.AutoScrollMinSize = new Size(0, rows.Count * 42 + 10);
                parentPanel.HorizontalScroll.Enabled = false;
                parentPanel.HorizontalScroll.Visible = false;
            }

            void AddActionRow(Panel parentPanel, List<TextBox> textBoxes, List<Panel> rows, string path)
            {
                Panel rowPanel = new Panel();

                TextBox textBox = new TextBox();
                textBox.Text = path;
                rowPanel.Controls.Add(textBox);

                Button browseButton = new Button();
                browseButton.Text = "Browse";
                browseButton.Width = 115;
                browseButton.Height = 30;
                browseButton.TextAlign = ContentAlignment.MiddleCenter;
                browseButton.Click += (s, e) =>
                {
                    string selectedPath = BrowseForActionFile();

                    if (!string.IsNullOrWhiteSpace(selectedPath))
                        textBox.Text = selectedPath;
                };
                rowPanel.Controls.Add(browseButton);

                textBoxes.Add(textBox);
                rows.Add(rowPanel);
                parentPanel.Controls.Add(rowPanel);

                ApplyThemeToControl(rowPanel);
                LayoutRows(parentPanel, rows);
            }

            void RemoveLastActionRow(Panel parentPanel, List<TextBox> textBoxes, List<Panel> rows)
            {
                if (textBoxes.Count <= 1)
                {
                    textBoxes[0].Text = "";
                    return;
                }

                int lastIndex = rows.Count - 1;

                Panel rowToRemove = rows[lastIndex];

                parentPanel.Controls.Remove(rowToRemove);
                rows.RemoveAt(lastIndex);
                textBoxes.RemoveAt(lastIndex);

                rowToRemove.Dispose();

                LayoutRows(parentPanel, rows);
            }

            void LayoutDialog()
            {
                int margin = 20;
                int right = dialog.ClientSize.Width - margin;

                importantLabel.Width = dialog.ClientSize.Width - 40;
                noteLabel.Width = dialog.ClientSize.Width - 40;

                separatorPanel.Width = dialog.ClientSize.Width - 40;
                separatorPanel.BackColor = currentTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase)
                    ? Color.FromArgb(100, 100, 100)
                    : Color.FromArgb(170, 170, 170);

                connectAddButton.Left = right - 100;
                connectAddButton.Top = 100;

                connectRemoveButton.Left = right - 45;
                connectRemoveButton.Top = 100;

                connectPanel.Width = dialog.ClientSize.Width - 40;

                int saveTop = dialog.ClientSize.Height - 65;

                saveButton.Left = dialog.ClientSize.Width - 220;
                saveButton.Top = saveTop;

                cancelButton.Left = dialog.ClientSize.Width - 120;
                cancelButton.Top = saveTop;

                int availableHeight = saveTop - 175;
                int panelHeight = Math.Max(140, availableHeight / 2);

                connectPanel.Height = panelHeight;

                disconnectLabel.Top = connectPanel.Bottom + 35;

                disconnectAddButton.Left = right - 100;
                disconnectAddButton.Top = disconnectLabel.Top - 5;

                disconnectRemoveButton.Left = right - 45;
                disconnectRemoveButton.Top = disconnectLabel.Top - 5;

                disconnectPanel.Top = disconnectLabel.Bottom + 10;
                disconnectPanel.Width = dialog.ClientSize.Width - 40;
                disconnectPanel.Height = saveButton.Top - disconnectPanel.Top - 20;

                LayoutRows(connectPanel, connectRows);
                LayoutRows(disconnectPanel, disconnectRows);
            }

            List<string> connectPathsToLoad = controller.ConnectActionPaths.ToList();
            List<string> disconnectPathsToLoad = controller.DisconnectActionPaths.ToList();

            if (connectPathsToLoad.Count == 0)
                connectPathsToLoad.Add("");

            if (disconnectPathsToLoad.Count == 0)
                disconnectPathsToLoad.Add("");

            foreach (string path in connectPathsToLoad)
                AddActionRow(connectPanel, connectTextBoxes, connectRows, path);

            foreach (string path in disconnectPathsToLoad)
                AddActionRow(disconnectPanel, disconnectTextBoxes, disconnectRows, path);

            connectAddButton.Click += (s, e) =>
            {
                AddActionRow(connectPanel, connectTextBoxes, connectRows, "");
                LayoutDialog();
            };

            connectRemoveButton.Click += (s, e) =>
            {
                RemoveLastActionRow(connectPanel, connectTextBoxes, connectRows);
                LayoutDialog();
            };

            disconnectAddButton.Click += (s, e) =>
            {
                AddActionRow(disconnectPanel, disconnectTextBoxes, disconnectRows, "");
                LayoutDialog();
            };

            disconnectRemoveButton.Click += (s, e) =>
            {
                RemoveLastActionRow(disconnectPanel, disconnectTextBoxes, disconnectRows);
                LayoutDialog();
            };

            dialog.Resize += (s, e) =>
            {
                LayoutDialog();
            };

            ApplyThemeToControl(dialog);
            LayoutDialog();

            DialogResult result = dialog.ShowDialog(this);

            if (result == DialogResult.OK)
            {
                controller.ConnectActionPaths = connectTextBoxes
                    .Select(x => x.Text.Trim())
                    .ToList();

                controller.DisconnectActionPaths = disconnectTextBoxes
                    .Select(x => x.Text.Trim())
                    .ToList();

                if (controller.ConnectActionPaths.Count == 0)
                    controller.ConnectActionPaths.Add("");

                if (controller.DisconnectActionPaths.Count == 0)
                    controller.DisconnectActionPaths.Add("");

                SaveConfig();
            }
        }

        private void ShowAboutWindow()
        {
            Form dialog = new Form();
            dialog.Text = "About Pad Trigger";
            dialog.Width = 720;
            dialog.Height = 650;
            dialog.StartPosition = FormStartPosition.CenterParent;
            dialog.FormBorderStyle = FormBorderStyle.FixedDialog;
            dialog.MaximizeBox = false;
            dialog.MinimizeBox = false;
            dialog.Icon = appIcon;

            PictureBox aboutIcon = new PictureBox();
            aboutIcon.Left = 25;
            aboutIcon.Top = 25;
            aboutIcon.Width = 110;
            aboutIcon.Height = 110;
            aboutIcon.SizeMode = PictureBoxSizeMode.Zoom;
            aboutIcon.Image = LoadTitleImage();
            dialog.Controls.Add(aboutIcon);

            Label title = new Label();
            title.Text = "Pad Trigger";
            title.Font = new Font("Segoe UI", 24, FontStyle.Bold);
            title.Left = 155;
            title.Top = 25;
            title.AutoSize = true;
            title.MaximumSize = new Size(500, 0);
            dialog.Controls.Add(title);

            Label creator = new Label();
            creator.Text = "Tool made By Valentin Yochev";
            creator.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            creator.Left = 155;
            creator.Top = 90;
            creator.Width = 500;
            creator.Height = 28;
            dialog.Controls.Add(creator);

            Label versionLabel = new Label();
            versionLabel.Text = "Version 1.1";
            versionLabel.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            versionLabel.Left = 155;
            versionLabel.Top = 120;
            versionLabel.Width = 500;
            versionLabel.Height = 24;
            dialog.Controls.Add(versionLabel);

            Label youtubeLabel = new Label();
            youtubeLabel.Text = "YouTube channel:";
            youtubeLabel.Left = 25;
            youtubeLabel.Top = 175;
            youtubeLabel.Width = 220;
            youtubeLabel.Height = 24;
            youtubeLabel.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            dialog.Controls.Add(youtubeLabel);

            LinkLabel youtubeLink = new LinkLabel();
            youtubeLink.Text = "https://www.youtube.com/@SPYBGWTVR";
            youtubeLink.Left = 25;
            youtubeLink.Top = 202;
            youtubeLink.Width = 620;
            youtubeLink.Height = 28;
            youtubeLink.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            youtubeLink.LinkClicked += (s, e) =>
            {
                OpenYoutubeChannel();
            };
            dialog.Controls.Add(youtubeLink);

            Label description1 = new Label();
            description1.Text =
                "This tool allows you to trigger specific actions or scripts depending on when " +
                "your controller connects or disconnects from your computer.";
            description1.Left = 25;
            description1.Top = 250;
            description1.Width = 650;
            description1.Height = 70;
            description1.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            description1.AutoSize = false;
            dialog.Controls.Add(description1);

            Label description2 = new Label();
            description2.Text =
                "The tool is free and is open source for everybody. If you like the tool and " +
                "you want to help me out, subscribe to my YouTube channel by clicking the icon below. " +
                "That will help me more than any donation. Thanks!";
            description2.Left = 25;
            description2.Top = 330;
            description2.Width = 650;
            description2.Height = 115;
            description2.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            description2.AutoSize = false;
            dialog.Controls.Add(description2);

            PictureBox youtubeIcon = new PictureBox();
            youtubeIcon.Left = 300;
            youtubeIcon.Top = 470;
            youtubeIcon.Width = 120;
            youtubeIcon.Height = 80;
            youtubeIcon.SizeMode = PictureBoxSizeMode.Zoom;
            youtubeIcon.Cursor = Cursors.Hand;
            youtubeIcon.Image = CreateYoutubeIconImage(120, 80);
            youtubeIcon.Click += (s, e) =>
            {
                OpenYoutubeChannel();
            };
            dialog.Controls.Add(youtubeIcon);

            Button okButton = new Button();
            okButton.Text = "OK";
            okButton.Left = 585;
            okButton.Top = 565;
            okButton.Width = 90;
            okButton.Height = 34;
            okButton.DialogResult = DialogResult.OK;
            dialog.Controls.Add(okButton);

            dialog.AcceptButton = okButton;

            ApplyThemeToControl(dialog);

            if (currentTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase))
            {
                youtubeLink.LinkColor = Color.DeepSkyBlue;
                youtubeLink.ActiveLinkColor = Color.Cyan;
                youtubeLink.VisitedLinkColor = Color.DeepSkyBlue;
            }

            dialog.ShowDialog(this);

            if (aboutIcon.Image != null)
            {
                aboutIcon.Image.Dispose();
            }

            if (youtubeIcon.Image != null)
            {
                youtubeIcon.Image.Dispose();
            }
        }

        private void OpenYoutubeChannel()
        {
            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = YoutubeSubscribeUrl;
                startInfo.UseShellExecute = true;
                Process.Start(startInfo);
            }
            catch
            {
                // Ignore browser open errors.
            }
        }

        private string BrowseForActionFile()
        {
            OpenFileDialog dialog = new OpenFileDialog();
            dialog.Title = "Choose script or program";
            dialog.Filter = "Programs and scripts|*.exe;*.bat;*.cmd;*.ps1;*.vbs|All files|*.*";

            DialogResult result = dialog.ShowDialog(this);

            if (result == DialogResult.OK)
                return dialog.FileName;

            return "";
        }

        private void SetupDeviceChangeDelayTimer()
        {
            deviceChangeDelayTimer = new System.Windows.Forms.Timer();
            deviceChangeDelayTimer.Interval = 1000;
            deviceChangeDelayTimer.Tick += (s, e) =>
            {
                deviceChangeDelayTimer.Stop();
                RefreshControllers();
            };
        }

        private void SetupControllerPollingTimer()
        {
            // Fast controller watcher.
            // Controllers must stay instant and must never wait for Bluetooth / PowerShell scans.
            controllerPollingTimer = new System.Threading.Timer(_ =>
            {
                QueueControllerScan(false);
            }, null, 250, 250);

            // Slower Bluetooth watcher.
            // Bluetooth scanning is more expensive because Windows exposes it through PnP/PowerShell.
            // Keeping it separate prevents Bluetooth devices from slowing down controller detection.
            bluetoothPollingTimer = new System.Threading.Timer(_ =>
            {
                QueueBluetoothScan(false);
            }, null, 1000, 2000);
        }

        private void RegisterForHidDeviceNotifications()
        {
            DEV_BROADCAST_DEVICEINTERFACE notificationFilter = new DEV_BROADCAST_DEVICEINTERFACE();
            notificationFilter.dbcc_size = Marshal.SizeOf(typeof(DEV_BROADCAST_DEVICEINTERFACE));
            notificationFilter.dbcc_devicetype = DBT_DEVTYP_DEVICEINTERFACE;
            notificationFilter.dbcc_reserved = 0;
            notificationFilter.dbcc_classguid = GUID_DEVINTERFACE_HID;

            deviceNotificationHandle = RegisterDeviceNotification(
                Handle,
                ref notificationFilter,
                DEVICE_NOTIFY_WINDOW_HANDLE
            );
        }

        private void ControllerGrid_CellMouseDown(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button == MouseButtons.Right && e.RowIndex >= 0)
            {
                controllerGrid.ClearSelection();
                controllerGrid.Rows[e.RowIndex].Selected = true;
                controllerGrid.CurrentCell = controllerGrid.Rows[e.RowIndex].Cells["ControllerName"];
                rightClickedControllerDeviceId = controllerGrid.Rows[e.RowIndex].Cells["DeviceId"].Value?.ToString() ?? "";
                controllerRightClickMenu.Show(Cursor.Position);
            }
        }

        private void SetupTrayIcon()
        {
            trayMenu = new ContextMenuStrip();
            trayMenu.ShowImageMargin = false;
            trayMenu.ShowCheckMargin = false;

            ToolStripMenuItem openItem = new ToolStripMenuItem("Open Pad Trigger");
            openItem.Click += (s, e) => ShowMainWindow();

            ToolStripMenuItem aboutItem = new ToolStripMenuItem("About");
            aboutItem.Click += (s, e) => ShowAboutWindow();

            ToolStripMenuItem exitItem = new ToolStripMenuItem("Exit");
            exitItem.Click += (s, e) =>
            {
                allowExit = true;
                trayIcon.Visible = false;
                Application.Exit();
            };

            trayMenu.Items.Add(openItem);
            trayMenu.Items.Add(aboutItem);
            trayMenu.Items.Add(new ToolStripSeparator());
            trayMenu.Items.Add(exitItem);

            trayIcon = new NotifyIcon();
            trayIcon.Text = "Pad Trigger";
            trayIcon.Icon = appIcon;
            trayIcon.ContextMenuStrip = trayMenu;
            trayIcon.Visible = true;
            trayIcon.DoubleClick += (s, e) => ShowMainWindow();

            ApplyThemeToToolStrip(trayMenu);
        }

        private void RefreshControllers(bool forceRedraw = false)
        {
            QueueControllerScan(forceRedraw);
        }

        private void QueueControllerScan(bool forceRedraw)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (Interlocked.Exchange(ref controllerScanInProgress, 1) == 1)
                return;

            bool includeControllers = showControllers;

            Task.Run(() =>
            {
                List<ControllerItem> currentlyConnected = new List<ControllerItem>();

                if (includeControllers)
                    currentlyConnected = GetConnectedControllersFromDirectInput();

                try
                {
                    if (IsDisposed || !IsHandleCreated)
                    {
                        Interlocked.Exchange(ref controllerScanInProgress, 0);
                        return;
                    }

                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            ApplyControllerScanResult(
                                currentlyConnected,
                                forceRedraw,
                                scannedControllers: includeControllers,
                                scannedOtherDevices: false
                            );
                        }
                        finally
                        {
                            Interlocked.Exchange(ref controllerScanInProgress, 0);
                        }
                    }));
                }
                catch
                {
                    Interlocked.Exchange(ref controllerScanInProgress, 0);
                }
            });
        }

        private void QueueBluetoothScan(bool forceRedraw)
        {
            if (IsDisposed || !IsHandleCreated)
                return;

            if (Interlocked.Exchange(ref bluetoothScanInProgress, 1) == 1)
                return;

            HashSet<string> trackedBluetoothDeviceIds = knownControllers.Values
                .Where(x => string.Equals(x.DeviceCategory, "Other Device", StringComparison.Ordinal))
                .Select(x => x.DeviceId)
                .ToHashSet();

            if (trackedBluetoothDeviceIds.Count == 0)
            {
                Interlocked.Exchange(ref bluetoothScanInProgress, 0);
                return;
            }

            Task.Run(() =>
            {
                List<ControllerItem> connectedControllersSnapshot = GetConnectedControllersFromDirectInput();
                List<ControllerItem> allBluetoothDevices = GetAllOtherDevicesFromPowerShell(connectedControllersSnapshot);

                List<ControllerItem> currentlyConnected = allBluetoothDevices
                    .Where(x => trackedBluetoothDeviceIds.Contains(x.DeviceId))
                    .ToList();

                try
                {
                    if (IsDisposed || !IsHandleCreated)
                    {
                        Interlocked.Exchange(ref bluetoothScanInProgress, 0);
                        return;
                    }

                    BeginInvoke(new Action(() =>
                    {
                        try
                        {
                            ApplyControllerScanResult(
                                currentlyConnected,
                                forceRedraw,
                                scannedControllers: false,
                                scannedOtherDevices: true
                            );
                        }
                        finally
                        {
                            Interlocked.Exchange(ref bluetoothScanInProgress, 0);
                        }
                    }));
                }
                catch
                {
                    Interlocked.Exchange(ref bluetoothScanInProgress, 0);
                }
            });
        }

        private void ApplyControllerScanResult(
            List<ControllerItem> currentlyConnected,
            bool forceRedraw,
            bool scannedControllers,
            bool scannedOtherDevices
        )
        {
            Dictionary<string, bool> previousStates = new Dictionary<string, bool>();

            foreach (ControllerItem controller in knownControllers.Values)
            {
                previousStates[controller.DeviceId] = controller.Connected;

                string category = string.IsNullOrWhiteSpace(controller.DeviceCategory) ? "Controller" : controller.DeviceCategory;

                if ((category == "Controller" && scannedControllers) ||
                    (category == "Other Device" && scannedOtherDevices))
                {
                    controller.Connected = false;
                }
            }

            bool tableNeedsRedraw = forceRedraw;
            bool configNeedsSave = false;

            foreach (ControllerItem detected in currentlyConnected)
            {
                if (knownControllers.ContainsKey(detected.DeviceId))
                {
                    ControllerItem existing = knownControllers[detected.DeviceId];

                    if (existing.Connected != detected.Connected)
                        tableNeedsRedraw = true;

                    existing.Connected = detected.Connected;

                    if (!string.Equals(existing.DetectedName, detected.DetectedName, StringComparison.Ordinal))
                    {
                        existing.DetectedName = detected.DetectedName;
                        tableNeedsRedraw = true;
                        configNeedsSave = true;
                    }

                    if (string.IsNullOrWhiteSpace(existing.DeviceCategory) ||
                        !string.Equals(existing.DeviceCategory, detected.DeviceCategory, StringComparison.Ordinal))
                    {
                        existing.DeviceCategory = detected.DeviceCategory;
                        tableNeedsRedraw = true;
                        configNeedsSave = true;
                    }
                }
                else
                {
                    // Bluetooth devices must be added manually from the Bluetooth Devices window.
                    if (string.Equals(detected.DeviceCategory, "Other Device", StringComparison.Ordinal))
                        continue;

                    knownControllers[detected.DeviceId] = detected;
                    tableNeedsRedraw = true;
                    configNeedsSave = true;
                }
            }

            foreach (ControllerItem controller in knownControllers.Values)
            {
                string category = string.IsNullOrWhiteSpace(controller.DeviceCategory) ? "Controller" : controller.DeviceCategory;

                if ((category == "Controller" && !scannedControllers) ||
                    (category == "Other Device" && !scannedOtherDevices))
                {
                    continue;
                }

                bool wasConnected = previousStates.ContainsKey(controller.DeviceId) && previousStates[controller.DeviceId];
                bool isConnected = controller.Connected;

                if (wasConnected != isConnected)
                    tableNeedsRedraw = true;

                if (!hasCompletedInitialScan)
                    continue;

                if (!wasConnected && isConnected)
                {
                    controller.ArmedForDisconnect = true;
                    RunActions(controller.ConnectActionPaths);
                }

                if (wasConnected && !isConnected)
                {
                    if (controller.ArmedForDisconnect)
                        RunActions(controller.DisconnectActionPaths);

                    controller.ArmedForDisconnect = false;
                }
            }

            if (!hasCompletedInitialScan)
                tableNeedsRedraw = true;

            hasCompletedInitialScan = true;

            if (configNeedsSave)
                SaveConfig();

            if (tableNeedsRedraw)
                DrawControllerTable();
        }

        private List<ControllerItem> GetConnectedDevices(bool includeControllers, HashSet<string> trackedOtherDeviceIds)
        {
            List<ControllerItem> result = new List<ControllerItem>();

            if (includeControllers)
                result.AddRange(GetConnectedControllersFromDirectInput());

            if (trackedOtherDeviceIds != null && trackedOtherDeviceIds.Count > 0)
            {
                List<ControllerItem> allOtherDevices = GetAllOtherDevicesFromPowerShell(result);
                result.AddRange(allOtherDevices.Where(x => trackedOtherDeviceIds.Contains(x.DeviceId)));
            }

            return result;
        }

        private List<ControllerItem> GetConnectedControllersFromDirectInput()
        {
            List<ControllerItem> result = new List<ControllerItem>();

            try
            {
                using DirectInput directInput = new DirectInput();

                List<DeviceInstance> devices = new List<DeviceInstance>();

                devices.AddRange(directInput.GetDevices(DeviceType.Joystick, DeviceEnumerationFlags.AttachedOnly));
                devices.AddRange(directInput.GetDevices(DeviceType.Gamepad, DeviceEnumerationFlags.AttachedOnly));
                devices.AddRange(directInput.GetDevices(DeviceType.FirstPerson, DeviceEnumerationFlags.AttachedOnly));
                devices.AddRange(directInput.GetDevices(DeviceType.Flight, DeviceEnumerationFlags.AttachedOnly));

                foreach (DeviceInstance device in devices)
                {
                    string name = device.ProductName;

                    if (string.IsNullOrWhiteSpace(name))
                        name = device.InstanceName;

                    if (string.IsNullOrWhiteSpace(name))
                        name = "Unknown Controller";

                    string deviceId = device.InstanceGuid.ToString();

                    if (!result.Any(x => x.DeviceId == deviceId))
                    {
                        result.Add(new ControllerItem
                        {
                            DeviceId = deviceId,
                            DetectedName = name,
                            DisplayName = name,
                            DeviceCategory = "Controller",
                            Connected = true
                        });
                    }
                }
            }
            catch
            {
                // Keep polling silent. A temporary DirectInput failure should not freeze or annoy the user.
            }

            return result;
        }

        private List<ControllerItem> GetAllOtherDevicesFromPowerShell(List<ControllerItem> currentControllers)
        {
            List<ControllerItem> result = new List<ControllerItem>();

            try
            {
                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = "powershell.exe";

                // Bluetooth-only scan with a stronger connection check.
                //
                // Important:
                // A paired Bluetooth keyboard can still exist in Windows while it is actually using USB.
                // So we try DEVPKEY_Device_IsConnected first. This is the closest Windows PnP property
                // to what the Bluetooth settings page shows as connected/disconnected.
                //
                // If that property is unavailable, we fall back to checking active present child devices
                // that share the Bluetooth device address.
                startInfo.Arguments =
                    "-NoProfile -ExecutionPolicy Bypass -Command " +
                    "\"$all=Get-PnpDevice;" +
                    "$present=Get-PnpDevice -PresentOnly;" +
                    "$bt=$all | Where-Object { $_.FriendlyName -and $_.Class -eq 'Bluetooth' };" +
                    "$items=foreach($d in $bt){" +
                    "$id=[string]$d.InstanceId;" +
                    "$addr='';" +
                    "if($id -match 'DEV_([0-9A-Fa-f]{12})'){$addr=$matches[1]}elseif($id -match '([0-9A-Fa-f]{12})'){$addr=$matches[1]};" +

                    "$propConnected=$null;" +
                    "try{" +
                    "$p=Get-PnpDeviceProperty -InstanceId $id -KeyName 'DEVPKEY_Device_IsConnected' -ErrorAction SilentlyContinue;" +
                    "if($null -ne $p -and $null -ne $p.Data){$propConnected=[bool]$p.Data}" +
                    "}catch{};" +

                    "$childConnected=$false;" +
                    "if($addr){" +
                    "$children=$present | Where-Object { " +
                    "([string]$_.InstanceId) -like ('*'+$addr+'*') -and " +
                    "([string]$_.InstanceId) -ne $id -and " +
                    "$_.Status -eq 'OK' -and " +
                    "$_.FriendlyName -and " +
                    "($_.FriendlyName -notmatch 'Generic Attribute|Enumerator|Service|Transport|Protocol|Avrcp|Hands-Free|Handsfree') " +
                    "};" +
                    "$childConnected=($children.Count -gt 0)" +
                    "};" +

                    "$connected=$false;" +
                    "if($null -ne $propConnected){$connected=$propConnected}else{$connected=$childConnected};" +

                    "[pscustomobject]@{FriendlyName=$d.FriendlyName;InstanceId=$id;Class=$d.Class;Connected=$connected}" +
                    "};" +
                    "$items | ConvertTo-Json -Compress\"";

                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;

                using Process process = Process.Start(startInfo);

                if (process == null)
                    return result;

                Task<string> readOutputTask = process.StandardOutput.ReadToEndAsync();

                if (!process.WaitForExit(3500))
                {
                    try
                    {
                        process.Kill(true);
                    }
                    catch
                    {
                        // Ignore kill errors.
                    }

                    return result;
                }

                string output = readOutputTask.Result;

                if (string.IsNullOrWhiteSpace(output))
                    return result;

                JsonDocument document = JsonDocument.Parse(output);

                IEnumerable<JsonElement> items;

                if (document.RootElement.ValueKind == JsonValueKind.Array)
                    items = document.RootElement.EnumerateArray();
                else if (document.RootElement.ValueKind == JsonValueKind.Object)
                    items = new[] { document.RootElement };
                else
                    return result;

                foreach (JsonElement item in items)
                {
                    string name = GetJsonString(item, "FriendlyName");
                    string instanceId = GetJsonString(item, "InstanceId");
                    bool connected = GetJsonBool(item, "Connected");

                    if (string.IsNullOrWhiteSpace(instanceId))
                        continue;

                    if (string.IsNullOrWhiteSpace(name))
                        continue;

                    if (ShouldSkipBluetoothDeviceName(name, instanceId, currentControllers))
                        continue;

                    string deviceId = "OTHER|" + instanceId.Trim();

                    if (!result.Any(x => x.DeviceId == deviceId))
                    {
                        result.Add(new ControllerItem
                        {
                            DeviceId = deviceId,
                            DetectedName = name.Trim(),
                            DisplayName = name.Trim(),
                            DeviceCategory = "Other Device",
                            Connected = connected
                        });
                    }
                }
            }
            catch
            {
                // Bluetooth Devices is optional. If Windows blocks or delays PowerShell/PnP, keep the app silent.
            }

            return result
                .OrderByDescending(x => x.Connected)
                .ThenBy(x => x.DisplayName)
                .ToList();
        }

private bool GetJsonBool(JsonElement element, string propertyName)
        {
            if (!element.TryGetProperty(propertyName, out JsonElement value))
                return false;

            if (value.ValueKind == JsonValueKind.True)
                return true;

            if (value.ValueKind == JsonValueKind.False)
                return false;

            if (bool.TryParse(value.ToString(), out bool result))
                return result;

            return false;
        }

                private string GetJsonString(JsonElement element, string propertyName)
        {
            if (element.TryGetProperty(propertyName, out JsonElement value) && value.ValueKind != JsonValueKind.Null)
                return value.ToString();

            return "";
        }


        private string GetBestOtherDeviceName(List<RawOtherDeviceItem> items)
        {
            RawOtherDeviceItem best = items
                .OrderByDescending(x => GetOtherDeviceNameScore(x.FriendlyName, x.DeviceClass))
                .ThenBy(x => x.FriendlyName)
                .FirstOrDefault();

            if (best == null)
                return "";

            return best.FriendlyName;
        }

        private int GetOtherDeviceNameScore(string name, string deviceClass)
        {
            if (string.IsNullOrWhiteSpace(name))
                return -1000;

            string lower = name.ToLowerInvariant();

            int score = 50;

            if (!string.IsNullOrWhiteSpace(deviceClass) &&
                deviceClass.Equals("Bluetooth", StringComparison.OrdinalIgnoreCase))
                score += 80;

            if (!string.IsNullOrWhiteSpace(deviceClass) &&
                (deviceClass.Equals("Keyboard", StringComparison.OrdinalIgnoreCase) ||
                 deviceClass.Equals("Mouse", StringComparison.OrdinalIgnoreCase) ||
                 deviceClass.Equals("HIDClass", StringComparison.OrdinalIgnoreCase)))
                score += 25;

            if (!IsGenericOtherDeviceName(name))
                score += 160;

            if (lower.Contains("keyboard") || lower.Contains("mouse") || lower.Contains("receiver") || lower.Contains("dongle"))
                score += 45;

            if (lower.Contains("kbp") || lower.Contains("lorgar") || lower.Contains("dark project") || lower.Contains("novus"))
                score += 120;

            if (IsGenericInfrastructureDeviceName(name))
                score -= 300;

            if (lower.Contains("hid keyboard device") ||
                lower.Contains("hid-compliant") ||
                lower.Contains("usb input device") ||
                lower.Contains("bluetooth le generic attribute") ||
                lower.Contains("consumer control") ||
                lower.Contains("vendor-defined"))
                score -= 120;

            return score;
        }

        private bool IsGenericOtherDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            string lower = name.ToLowerInvariant();

            return lower == "hid keyboard device" ||
                   lower == "hid-compliant consumer control device" ||
                   lower == "hid-compliant vendor-defined device" ||
                   lower == "hid-compliant mouse" ||
                   lower == "usb input device" ||
                   lower == "bluetooth le generic attribute service" ||
                   lower == "bluetooth device" ||
                   lower == "usb composite device" ||
                   lower == "generic usb hub" ||
                   lower == "usb root hub" ||
                   lower == "usb root hub (usb 3.0)";
        }

private bool IsGenericInfrastructureDeviceName(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return true;

            string lower = name.ToLowerInvariant();

            return lower == "generic usb hub" ||
                   lower == "generic superspeed usb hub" ||
                   lower == "usb root hub" ||
                   lower == "usb root hub (usb 3.0)" ||
                   lower == "usb composite device" ||
                   lower == "usb input device" ||
                   lower.Contains("generic monitor") ||
                   lower.Contains("print to pdf") ||
                   lower.Contains("scsi disk device") ||
                   lower.Contains("storage device") ||
                   lower.Contains("host controller") ||
                   lower.Contains("root complex") ||
                   lower.Contains("pci express") ||
                   lower.Contains("standard sata") ||
                   lower.Contains("acpi") ||
                   lower.Contains("system timer") ||
                   lower.Contains("motherboard") ||
                   lower.Contains("processor") ||
                   lower.Contains("trusted platform module");
        }

        
        private bool ShouldSkipBluetoothDeviceName(string name, string instanceId, List<ControllerItem> currentControllers)
        {
            string combined = (name + " " + instanceId).ToLowerInvariant();

            // Avoid showing controllers twice as Bluetooth devices.
            if (combined.Contains("game controller") ||
                combined.Contains("gamepad") ||
                combined.Contains("joystick") ||
                combined.Contains("xinput") ||
                combined.Contains("xbox") ||
                combined.Contains("8bitdo"))
            {
                return true;
            }

            // Avoid Bluetooth adapter / service entries.
            if (combined.Contains("bluetooth device") ||
                combined.Contains("bluetooth radio") ||
                combined.Contains("bluetooth adapter") ||
                combined.Contains("generic bluetooth") ||
                combined.Contains("microsoft bluetooth") ||
                combined.Contains("bluetooth le generic attribute") ||
                combined.Contains("enumerator") ||
                combined.Contains("avrcp transport") ||
                combined.Contains("hands-free") ||
                combined.Contains("handsfree"))
            {
                return true;
            }

            foreach (ControllerItem controller in currentControllers)
            {
                if (!string.IsNullOrWhiteSpace(controller.DetectedName) &&
                    combined.Contains(controller.DetectedName.ToLowerInvariant()))
                {
                    return true;
                }
            }

            return false;
        }

        private bool ShouldSkipOtherDevice(string name, string instanceId, List<ControllerItem> currentControllers)
        {
            string combined = (name + " " + instanceId).ToLowerInvariant();

            // Avoid showing controllers twice when Other Devices is used.
            if (combined.Contains("game controller") ||
                combined.Contains("gamepad") ||
                combined.Contains("joystick") ||
                combined.Contains("xinput") ||
                combined.Contains("xbox") ||
                combined.Contains("8bitdo"))
            {
                return true;
            }

            // Avoid showing Windows infrastructure as a selectable device.
            if (IsGenericInfrastructureDeviceName(name))
                return true;

            foreach (ControllerItem controller in currentControllers)
            {
                if (!string.IsNullOrWhiteSpace(controller.DetectedName) &&
                    combined.Contains(controller.DetectedName.ToLowerInvariant()))
                {
                    return true;
                }
            }

            return false;
        }

        private void RunActions(List<string> actionPaths)
        {
            if (actionPaths == null || actionPaths.Count == 0)
                return;

            List<string> pathsToRun = actionPaths
                .Where(x => !string.IsNullOrWhiteSpace(x))
                .ToList();

            if (pathsToRun.Count == 0)
                return;

            Task.Run(() =>
            {
                foreach (string actionPath in pathsToRun)
                {
                    RunSingleAction(actionPath);
                }
            });
        }

        private void RunSingleAction(string actionPath)
        {
            try
            {
                if (!File.Exists(actionPath))
                    return;

                string extension = Path.GetExtension(actionPath).ToLowerInvariant();

                ProcessStartInfo startInfo = new ProcessStartInfo();

                if (extension == ".ps1")
                {
                    startInfo.FileName = "powershell.exe";
                    startInfo.Arguments = "-NoProfile -ExecutionPolicy Bypass -File \"" + actionPath + "\"";
                    startInfo.UseShellExecute = false;
                }
                else
                {
                    startInfo.FileName = actionPath;
                    startInfo.UseShellExecute = true;
                }

                startInfo.WorkingDirectory = Path.GetDirectoryName(actionPath) ?? "";

                Process process = Process.Start(startInfo);

                if (process != null)
                {
                    process.WaitForExit();
                    process.Dispose();
                }
            }
            catch
            {
                // Keep Pad Trigger silent if one action fails.
            }
        }

        private void DrawControllerTable()
        {
            string selectedDeviceId = "";

            if (controllerGrid.SelectedRows.Count > 0)
                selectedDeviceId = controllerGrid.SelectedRows[0].Cells["DeviceId"].Value?.ToString() ?? "";

            int oldFirstDisplayedRow = -1;

            try
            {
                if (controllerGrid.Rows.Count > 0)
                    oldFirstDisplayedRow = controllerGrid.FirstDisplayedScrollingRowIndex;
            }
            catch
            {
                oldFirstDisplayedRow = -1;
            }

            controllerGrid.Rows.Clear();

            List<ControllerItem> visibleDevices = knownControllers.Values
                .Where(x => !x.Hidden)
                .Where(x =>
                {
                    string category = string.IsNullOrWhiteSpace(x.DeviceCategory) ? "Controller" : x.DeviceCategory;

                    if (category == "Controller")
                        return showControllers;

                    if (category == "Other Device")
                        return true;

                    return true;
                })
                .OrderBy(x => x.DeviceCategory)
                .ThenBy(x => x.DisplayName)
                .ToList();

            foreach (ControllerItem controller in visibleDevices)
            {
                string displayName = controller.DisplayName;

                int rowIndex = controllerGrid.Rows.Add(
                    displayName,
                    controller.Connected ? "Connected" : "Disconnected",
                    controller.DeviceId
                );

                DataGridViewCell statusCell = controllerGrid.Rows[rowIndex].Cells["Status"];

                if (controller.Connected)
                    statusCell.Style.ForeColor = Color.DarkGreen;
                else
                    statusCell.Style.ForeColor = Color.DarkRed;

                if (!string.IsNullOrWhiteSpace(selectedDeviceId) && controller.DeviceId == selectedDeviceId)
                    controllerGrid.Rows[rowIndex].Selected = true;
            }

            if (controllerGrid.Rows.Count == 0)
            {
                controllerGrid.Rows.Add("No visible devices found", "Disconnected", "");
            }

            ApplyThemeToControl(controllerGrid);

            if (oldFirstDisplayedRow >= 0 && controllerGrid.Rows.Count > 0)
            {
                try
                {
                    if (oldFirstDisplayedRow >= controllerGrid.Rows.Count)
                        oldFirstDisplayedRow = controllerGrid.Rows.Count - 1;

                    controllerGrid.FirstDisplayedScrollingRowIndex = oldFirstDisplayedRow;
                }
                catch
                {
                    // Ignore restore scroll errors.
                }
            }

            if (string.IsNullOrWhiteSpace(selectedDeviceId))
                controllerGrid.ClearSelection();
        }

        private void LoadConfig()
        {
            try
            {
                if (!File.Exists(configPath))
                    return;

                string json = File.ReadAllText(configPath);

                string trimmed = json.TrimStart();

                List<ControllerItem> savedControllers = new List<ControllerItem>();

                if (trimmed.StartsWith("["))
                {
                    savedControllers =
                        JsonSerializer.Deserialize<List<ControllerItem>>(json) ?? new List<ControllerItem>();
                }
                else
                {
                    AppConfig config =
                        JsonSerializer.Deserialize<AppConfig>(json) ?? new AppConfig();

                    currentTheme = string.IsNullOrWhiteSpace(config.Theme) ? "Dark" : config.Theme;
                    showControllers = config.ShowControllers;
                    showOtherDevices = false;
                    removedOtherDeviceIds.Clear();

                    // Old versions used a permanent removed-device block list.
                    // That behavior is removed now, so old removed IDs are intentionally ignored.
                    savedControllers = config.Controllers ?? new List<ControllerItem>();
                }

                knownControllers.Clear();

                foreach (ControllerItem controller in savedControllers)
                {
                    controller.Connected = false;
                    controller.ArmedForDisconnect = false;

                    if (string.IsNullOrWhiteSpace(controller.DeviceCategory))
                        controller.DeviceCategory = "Controller";

                    if (controller.ConnectActionPaths.Count == 0 && !string.IsNullOrWhiteSpace(controller.ConnectActionPath))
                        controller.ConnectActionPaths.Add(controller.ConnectActionPath);

                    if (controller.DisconnectActionPaths.Count == 0 && !string.IsNullOrWhiteSpace(controller.DisconnectActionPath))
                        controller.DisconnectActionPaths.Add(controller.DisconnectActionPath);

                    if (controller.ConnectActionPaths.Count == 0)
                        controller.ConnectActionPaths.Add("");

                    if (controller.DisconnectActionPaths.Count == 0)
                        controller.DisconnectActionPaths.Add("");

                    if (!string.IsNullOrWhiteSpace(controller.DeviceId))
                    {
                        knownControllers[controller.DeviceId] = controller;
                    }
                }
            }
            catch
            {
                currentTheme = "Dark";
            }
        }

        private void SaveConfig()
        {
            try
            {
                JsonSerializerOptions options = new JsonSerializerOptions();
                options.WriteIndented = true;

                AppConfig config = new AppConfig();
                config.Theme = currentTheme;
                config.ShowControllers = showControllers;
                config.ShowOtherDevices = false;
                config.RemovedOtherDeviceIds = new List<string>();
                config.Controllers = knownControllers.Values.ToList();

                string json = JsonSerializer.Serialize(config, options);
                File.WriteAllText(configPath, json);
            }
            catch
            {
                // Ignore save errors for now.
            }
        }

        protected override void WndProc(ref Message m)
        {
            base.WndProc(ref m);

            if (m.Msg == WM_DEVICECHANGE)
            {
                int eventType = m.WParam.ToInt32();

                if (eventType == DBT_DEVICEARRIVAL || eventType == DBT_DEVICEREMOVECOMPLETE)
                {
                    deviceChangeDelayTimer.Stop();
                    deviceChangeDelayTimer.Start();
                }
            }
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);

            if (startupInvisibleRequested)
            {
                BeginInvoke(new Action(() =>
                {
                    System.Windows.Forms.Timer hideTimer = new System.Windows.Forms.Timer();
                    hideTimer.Interval = 250;
                    hideTimer.Tick += (s, args) =>
                    {
                        hideTimer.Stop();
                        hideTimer.Dispose();

                        Hide();
                        ShowInTaskbar = false;
                        Opacity = 1;
                        RestoreMainWindowSizeAndPosition();
                    };
                    hideTimer.Start();
                }));
            }
        }

        private void RestoreMainWindowSizeAndPosition()
        {
            Opacity = 1;
            Width = 820;
            Height = 600;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            StartPosition = FormStartPosition.Manual;

            Rectangle screen = Screen.PrimaryScreen.WorkingArea;
            Left = screen.Left + (screen.Width - Width) / 2;
            Top = screen.Top + (screen.Height - Height) / 2;
        }

        private void ShowMainWindow()
        {
            Opacity = 1;
            RestoreMainWindowSizeAndPosition();

            ShowInTaskbar = true;
            WindowState = FormWindowState.Normal;

            if (!Visible)
                Show();

            BringToFront();
            Activate();
        }

        private void ShowControllersCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            showControllers = showControllersCheckBox.Checked;
            SaveConfig();
            RefreshControllers(true);
        }

        private void StartWithWindowsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (startWithWindowsCheckBox.Checked)
                EnableStartWithWindows();
            else
                DisableStartWithWindows();
        }

        private bool ShouldStartInvisibleOnBoot()
        {
            // This flag is only used by our own Startup shortcut.
            // The app still launches normally, initializes normally, then hides itself to tray after the form is shown.
            return Environment.GetCommandLineArgs()
                .Any(x => x.Equals("--startup", StringComparison.OrdinalIgnoreCase));
        }

        private string GetStartupShortcutPath()
        {
            string startupFolder = Environment.GetFolderPath(Environment.SpecialFolder.Startup);
            return Path.Combine(startupFolder, "Pad Trigger.lnk");
        }

        private bool IsStartWithWindowsEnabled()
        {
            return File.Exists(GetStartupShortcutPath());
        }

        private void RemoveOldRegistryStartupEntry()
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true);

            if (key != null)
                key.DeleteValue(AppName, false);
        }

        private void EnableStartWithWindows()
        {
            RemoveOldRegistryStartupEntry();

            string shortcutPath = GetStartupShortcutPath();
            string exePath = Application.ExecutablePath;
            string workingDirectory = AppDomain.CurrentDomain.BaseDirectory;

            Type shellType = Type.GetTypeFromProgID("WScript.Shell");

            if (shellType == null)
                return;

            dynamic shell = Activator.CreateInstance(shellType);
            dynamic shortcut = shell.CreateShortcut(shortcutPath);

            shortcut.TargetPath = exePath;
            shortcut.Arguments = "--startup";
            shortcut.WindowStyle = 1;
            shortcut.WorkingDirectory = workingDirectory;

            string icoPath = Path.Combine(workingDirectory, "pt.ico");
            shortcut.IconLocation = File.Exists(icoPath) ? icoPath : exePath;

            shortcut.Description = "Start Pad Trigger with Windows";
            shortcut.Save();
        }

        private void DisableStartWithWindows()
        {
            string shortcutPath = GetStartupShortcutPath();

            if (File.Exists(shortcutPath))
                File.Delete(shortcutPath);

            RemoveOldRegistryStartupEntry();
        }

        private void SetTheme(string theme)
        {
            currentTheme = theme;
            SaveConfig();
            ApplyThemeToMainWindow();
        }

        private void ApplyThemeToMainWindow()
        {
            ApplyThemeToControl(this);

            if (controllerRightClickMenu != null)
                ApplyThemeToToolStrip(controllerRightClickMenu);

            if (trayMenu != null)
                ApplyThemeToToolStrip(trayMenu);

            if (controllerGrid != null)
                controllerGrid.ClearSelection();
        }

        private void ApplyThemeToToolStrip(ToolStrip toolStrip)
        {
            bool dark = currentTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase);

            Color backColor = dark ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
            Color foreColor = dark ? Color.White : Color.Black;

            toolStrip.BackColor = backColor;
            toolStrip.ForeColor = foreColor;
            toolStrip.RenderMode = ToolStripRenderMode.Professional;
            toolStrip.Renderer = new ToolStripProfessionalRenderer(new PadTriggerColorTable(dark));

            if (toolStrip is ContextMenuStrip contextMenu)
            {
                contextMenu.ShowImageMargin = false;
                contextMenu.ShowCheckMargin = false;
            }

            foreach (ToolStripItem item in toolStrip.Items)
            {
                item.BackColor = backColor;
                item.ForeColor = foreColor;
            }
        }

        private void ApplyThemeToControl(Control root)
        {
            bool dark = currentTheme.Equals("Dark", StringComparison.OrdinalIgnoreCase);

            Color backColor = dark ? Color.FromArgb(30, 30, 30) : SystemColors.Control;
            Color foreColor = dark ? Color.White : Color.Black;
            Color inputBackColor = dark ? Color.FromArgb(45, 45, 48) : Color.White;
            Color buttonBackColor = dark ? Color.FromArgb(60, 60, 65) : SystemColors.Control;

            root.BackColor = backColor;
            root.ForeColor = foreColor;

            foreach (Control control in root.Controls)
            {
                control.ForeColor = foreColor;

                if (control is PictureBox pictureBox)
                {
                    pictureBox.BackColor = backColor;
                }
                else if (control is LinkLabel linkLabel)
                {
                    linkLabel.BackColor = backColor;
                    linkLabel.ForeColor = foreColor;

                    if (dark)
                    {
                        linkLabel.LinkColor = Color.DeepSkyBlue;
                        linkLabel.ActiveLinkColor = Color.Cyan;
                        linkLabel.VisitedLinkColor = Color.DeepSkyBlue;
                    }
                }
                else if (control is TextBox)
                {
                    control.BackColor = inputBackColor;
                    control.ForeColor = foreColor;
                }
                else if (control is Button button)
                {
                    button.BackColor = buttonBackColor;
                    button.ForeColor = foreColor;
                    button.FlatStyle = FlatStyle.Flat;
                    button.FlatAppearance.BorderColor = dark ? Color.FromArgb(180, 180, 180) : Color.FromArgb(120, 120, 120);
                }
                else if (control is DataGridView grid)
                {
                    Color gridBackColor = dark ? Color.FromArgb(35, 35, 38) : Color.White;
                    Color headerBackColor = dark ? Color.FromArgb(45, 45, 48) : SystemColors.Control;
                    Color selectionBackColor = dark ? Color.FromArgb(70, 70, 75) : Color.FromArgb(220, 220, 220);

                    grid.BackgroundColor = gridBackColor;
                    grid.GridColor = dark ? Color.FromArgb(80, 80, 80) : Color.LightGray;
                    grid.EnableHeadersVisualStyles = false;

                    grid.DefaultCellStyle.BackColor = gridBackColor;
                    grid.DefaultCellStyle.ForeColor = foreColor;
                    grid.DefaultCellStyle.SelectionBackColor = selectionBackColor;
                    grid.DefaultCellStyle.SelectionForeColor = foreColor;

                    grid.ColumnHeadersDefaultCellStyle.BackColor = headerBackColor;
                    grid.ColumnHeadersDefaultCellStyle.ForeColor = foreColor;
                    grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = headerBackColor;
                    grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = foreColor;

                    grid.RowHeadersDefaultCellStyle.BackColor = headerBackColor;
                    grid.RowHeadersDefaultCellStyle.ForeColor = foreColor;
                    grid.RowHeadersDefaultCellStyle.SelectionBackColor = headerBackColor;
                    grid.RowHeadersDefaultCellStyle.SelectionForeColor = foreColor;

                    foreach (DataGridViewColumn column in grid.Columns)
                    {
                        column.SortMode = DataGridViewColumnSortMode.NotSortable;
                    }
                }
                else
                {
                    control.BackColor = backColor;
                }

                if (control.HasChildren)
                    ApplyThemeToControl(control);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!allowExit)
            {
                e.Cancel = true;
                Hide();
                ShowInTaskbar = false;

                trayIcon.ShowBalloonTip(
                    1500,
                    "Pad Trigger",
                    "Pad Trigger is still running in the tray.",
                    ToolTipIcon.Info
                );
            }

            base.OnFormClosing(e);
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            if (controllerPollingTimer != null)
            {
                controllerPollingTimer.Dispose();
                controllerPollingTimer = null;
            }

            if (bluetoothPollingTimer != null)
            {
                bluetoothPollingTimer.Dispose();
                bluetoothPollingTimer = null;
            }

            if (deviceChangeDelayTimer != null)
            {
                deviceChangeDelayTimer.Stop();
                deviceChangeDelayTimer.Dispose();
            }

            if (deviceNotificationHandle != IntPtr.Zero)
            {
                UnregisterDeviceNotification(deviceNotificationHandle);
                deviceNotificationHandle = IntPtr.Zero;
            }

            SaveConfig();

            if (titleIconPictureBox != null && titleIconPictureBox.Image != null)
            {
                titleIconPictureBox.Image.Dispose();
            }

            if (trayIcon != null)
            {
                trayIcon.Visible = false;
                trayIcon.Dispose();
            }

            if (appIcon != null && appIcon != SystemIcons.Application)
            {
                appIcon.Dispose();
            }

            base.OnFormClosed(e);
        }
    }

    public class PadTriggerColorTable : ProfessionalColorTable
    {
        private readonly bool dark;

        public PadTriggerColorTable(bool darkMode)
        {
            dark = darkMode;
        }

        public override Color ToolStripDropDownBackground
        {
            get { return dark ? Color.FromArgb(45, 45, 48) : SystemColors.Control; }
        }

        public override Color ImageMarginGradientBegin
        {
            get { return dark ? Color.FromArgb(45, 45, 48) : SystemColors.Control; }
        }

        public override Color ImageMarginGradientMiddle
        {
            get { return dark ? Color.FromArgb(45, 45, 48) : SystemColors.Control; }
        }

        public override Color ImageMarginGradientEnd
        {
            get { return dark ? Color.FromArgb(45, 45, 48) : SystemColors.Control; }
        }

        public override Color MenuItemSelected
        {
            get { return dark ? Color.FromArgb(70, 70, 75) : SystemColors.Highlight; }
        }

        public override Color MenuItemBorder
        {
            get { return dark ? Color.FromArgb(90, 90, 95) : SystemColors.Highlight; }
        }
    }


    public class RawOtherDeviceItem
    {
        public string FriendlyName { get; set; } = "";
        public string InstanceId { get; set; } = "";
        public string DeviceClass { get; set; } = "";
        public string ContainerId { get; set; } = "";
    }

    public class AppConfig
    {
        public string Theme { get; set; } = "Dark";
        public bool ShowControllers { get; set; } = true;
        public bool ShowOtherDevices { get; set; } = false;
        public List<string> RemovedOtherDeviceIds { get; set; } = new List<string>();
        public List<ControllerItem> Controllers { get; set; } = new List<ControllerItem>();
    }

    public class ControllerItem
    {
        public string DeviceId { get; set; } = "";
        public string DetectedName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string DeviceCategory { get; set; } = "Controller";
        public bool Hidden { get; set; } = false;
        public bool Connected { get; set; }

        public List<string> ConnectActionPaths { get; set; } = new List<string>();
        public List<string> DisconnectActionPaths { get; set; } = new List<string>();

        public string ConnectActionPath { get; set; } = "";
        public string DisconnectActionPath { get; set; } = "";

        [JsonIgnore]
        public bool ArmedForDisconnect { get; set; } = false;
    }
}
