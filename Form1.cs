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
        private Label titleLabel;
        private Button lightThemeButton;
        private Button darkThemeButton;
        private Button aboutButton;
        private DataGridView controllerGrid;
        private ContextMenuStrip controllerRightClickMenu;

        private System.Windows.Forms.Timer deviceChangeDelayTimer;

        private bool allowExit = false;
        private bool hasCompletedInitialScan = false;
        private bool startMinimizedRequested = false;

        private string currentTheme = "Light";

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

        public Form1()
        {
            InitializeComponent();

            startMinimizedRequested = Environment.GetCommandLineArgs()
                .Any(x => x.Equals("--minimized", StringComparison.OrdinalIgnoreCase));

            LoadConfig();

            SetupMainWindow();

            appIcon = LoadAppIcon();
            Icon = appIcon;

            SetupTrayIcon();
            SetupControls();
            SetupControllerRightClickMenu();
            SetupDeviceChangeDelayTimer();

            RegisterForHidDeviceNotifications();

            startWithWindowsCheckBox.Checked = IsStartWithWindowsEnabled();

            ApplyThemeToMainWindow();

            RefreshControllers();
        }

        private Icon LoadAppIcon()
        {
            try
            {
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

            controllerRightClickMenu.Items.Add(renameItem);
            controllerRightClickMenu.Items.Add(editActionsItem);

            ApplyThemeToToolStrip(controllerRightClickMenu);
        }

        private void RenameSelectedController()
        {
            if (controllerGrid.SelectedRows.Count == 0)
                return;

            DataGridViewRow row = controllerGrid.SelectedRows[0];

            string deviceId = row.Cells["DeviceId"].Value?.ToString() ?? "";
            string currentName = row.Cells["ControllerName"].Value?.ToString() ?? "";

            if (string.IsNullOrWhiteSpace(deviceId))
                return;

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
            if (controllerGrid.SelectedRows.Count == 0)
                return;

            DataGridViewRow row = controllerGrid.SelectedRows[0];

            string deviceId = row.Cells["DeviceId"].Value?.ToString() ?? "";

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
            creator.Text = "Tool made By Valentin Yovchev";
            creator.Font = new Font("Segoe UI", 11, FontStyle.Bold);
            creator.Left = 155;
            creator.Top = 90;
            creator.Width = 500;
            creator.Height = 28;
            dialog.Controls.Add(creator);

            Label youtubeLabel = new Label();
            youtubeLabel.Text = "YouTube channel:";
            youtubeLabel.Left = 25;
            youtubeLabel.Top = 160;
            youtubeLabel.Width = 220;
            youtubeLabel.Height = 24;
            youtubeLabel.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            dialog.Controls.Add(youtubeLabel);

            LinkLabel youtubeLink = new LinkLabel();
            youtubeLink.Text = "https://www.youtube.com/@SPYBGWTVR";
            youtubeLink.Left = 25;
            youtubeLink.Top = 187;
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
            description1.Top = 235;
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
            description2.Top = 315;
            description2.Width = 650;
            description2.Height = 115;
            description2.Font = new Font("Segoe UI", 10, FontStyle.Regular);
            description2.AutoSize = false;
            dialog.Controls.Add(description2);

            PictureBox youtubeIcon = new PictureBox();
            youtubeIcon.Left = 300;
            youtubeIcon.Top = 455;
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
            okButton.Top = 550;
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

        private void RefreshControllers()
        {
            Dictionary<string, bool> previousStates = new Dictionary<string, bool>();

            foreach (ControllerItem controller in knownControllers.Values)
            {
                previousStates[controller.DeviceId] = controller.Connected;
                controller.Connected = false;
            }

            List<ControllerItem> currentlyConnected = GetConnectedControllersFromDirectInput();

            foreach (ControllerItem detected in currentlyConnected)
            {
                if (knownControllers.ContainsKey(detected.DeviceId))
                {
                    knownControllers[detected.DeviceId].Connected = true;
                    knownControllers[detected.DeviceId].DetectedName = detected.DetectedName;
                }
                else
                {
                    knownControllers[detected.DeviceId] = detected;
                }
            }

            if (hasCompletedInitialScan)
            {
                foreach (ControllerItem controller in knownControllers.Values)
                {
                    bool wasConnected = previousStates.ContainsKey(controller.DeviceId) && previousStates[controller.DeviceId];
                    bool isConnected = controller.Connected;

                    if (!wasConnected && isConnected)
                    {
                        controller.ArmedForDisconnect = true;
                        RunActions(controller.ConnectActionPaths);
                    }

                    if (wasConnected && !isConnected)
                    {
                        if (controller.ArmedForDisconnect)
                        {
                            RunActions(controller.DisconnectActionPaths);
                        }

                        controller.ArmedForDisconnect = false;
                    }
                }
            }

            hasCompletedInitialScan = true;

            SaveConfig();
            DrawControllerTable();
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
                            Connected = true
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "DirectInput Error");
            }

            return result;
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
            controllerGrid.Rows.Clear();

            foreach (ControllerItem controller in knownControllers.Values.OrderBy(x => x.DisplayName))
            {
                int rowIndex = controllerGrid.Rows.Add(
                    controller.DisplayName,
                    controller.Connected ? "Connected" : "Disconnected",
                    controller.DeviceId
                );

                DataGridViewCell statusCell = controllerGrid.Rows[rowIndex].Cells["Status"];

                if (controller.Connected)
                    statusCell.Style.ForeColor = Color.DarkGreen;
                else
                    statusCell.Style.ForeColor = Color.DarkRed;
            }

            if (controllerGrid.Rows.Count == 0)
            {
                controllerGrid.Rows.Add("No controllers found", "Disconnected", "");
            }

            ApplyThemeToControl(controllerGrid);
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

                    currentTheme = string.IsNullOrWhiteSpace(config.Theme) ? "Light" : config.Theme;
                    savedControllers = config.Controllers ?? new List<ControllerItem>();
                }

                knownControllers.Clear();

                foreach (ControllerItem controller in savedControllers)
                {
                    controller.Connected = false;
                    controller.ArmedForDisconnect = false;

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
                currentTheme = "Light";
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

            if (startMinimizedRequested)
            {
                Hide();
                ShowInTaskbar = false;
            }
        }

        private void ShowMainWindow()
        {
            ShowInTaskbar = true;
            Show();
            WindowState = FormWindowState.Normal;
            Activate();
        }

        private void StartWithWindowsCheckBox_CheckedChanged(object sender, EventArgs e)
        {
            if (startWithWindowsCheckBox.Checked)
                EnableStartWithWindows();
            else
                DisableStartWithWindows();
        }

        private bool IsStartWithWindowsEnabled()
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, false);

            if (key == null)
                return false;

            object value = key.GetValue(AppName);

            if (value == null)
                return false;

            return value.ToString().Contains(Application.ExecutablePath);
        }

        private void EnableStartWithWindows()
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true);

            if (key != null)
            {
                string value = "\"" + Application.ExecutablePath + "\" --minimized";
                key.SetValue(AppName, value);
            }
        }

        private void DisableStartWithWindows()
        {
            using RegistryKey key = Registry.CurrentUser.OpenSubKey(RunRegistryPath, true);

            if (key != null)
                key.DeleteValue(AppName, false);
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

    public class AppConfig
    {
        public string Theme { get; set; } = "Light";
        public List<ControllerItem> Controllers { get; set; } = new List<ControllerItem>();
    }

    public class ControllerItem
    {
        public string DeviceId { get; set; } = "";
        public string DetectedName { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public bool Connected { get; set; }

        public List<string> ConnectActionPaths { get; set; } = new List<string>();
        public List<string> DisconnectActionPaths { get; set; } = new List<string>();

        public string ConnectActionPath { get; set; } = "";
        public string DisconnectActionPath { get; set; } = "";

        [JsonIgnore]
        public bool ArmedForDisconnect { get; set; } = false;
    }
}
