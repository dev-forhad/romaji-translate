using System;
using System.Drawing;
using System.Windows.Forms;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Microsoft.Win32;
using Kawazu;
using WindowsInput;
using WindowsInput.Native;
using GTranslate.Translators;

namespace RomajiHotkeyTool
{
    static class Program
    {
        private static NotifyIcon trayIcon;
        private static KawazuConverter converter = new KawazuConverter();
        private static InputSimulator inputSim = new InputSimulator();
        private static AggregateTranslator translator = new AggregateTranslator();

        private const string APP_NAME = "Kanji Read-Assist";

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool RegisterHotKey(IntPtr hWnd, int id, uint fsModifiers, uint vk);

        [DllImport("user32.dll", SetLastError = true)]
        public static extern bool UnregisterHotKey(IntPtr hWnd, int id);

        [STAThread]
        static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            // 1. ENFORCE STARTUP ON FIRST RUN (NEW CODE BLOCK)
            // Checks if startup is currently disabled and enables it automatically.
            if (!IsStartupEnabled())
            {
                SetStartup(true);
            }

            // Tries to load the custom icon resource named AppIcon
            Icon myIcon = SystemIcons.Application;
            try { myIcon = Properties.Resources.AppIcon; } catch { }

            // 2. TRAY ICON SETUP
            trayIcon = new NotifyIcon
            {
                Icon = myIcon,
                Text = "Romaji + EN (Ctrl+F2)", // Hotkey text updated
                Visible = true
            };

            // 3. CONTEXT MENU WITH STARTUP OPTION
            var menu = new ContextMenuStrip();

            var startupItem = new ToolStripMenuItem("Run at Startup");
            // This will now correctly reflect the enforced state
            startupItem.Checked = IsStartupEnabled();
            startupItem.Click += (s, e) => {
                bool newState = !startupItem.Checked;
                SetStartup(newState);
                startupItem.Checked = newState;
            };
            menu.Items.Add(startupItem);

            menu.Items.Add("-");
            menu.Items.Add("Exit", null, (s, e) => Application.Exit());
            trayIcon.ContextMenuStrip = menu;

            trayIcon.ShowBalloonTip(3000, "Tool Ready", "Select text → Press Ctrl + F2", ToolTipIcon.Info);

            // 4. HIDDEN FORM FOR HOTKEY
            var form = new HiddenForm();
            form.HotkeyPressed += async () => await ProcessSelection(myIcon);

            Application.Run(form);
        }

        private static async Task ProcessSelection(Icon appIcon)
        {
            // Robust copy mechanism
            Clipboard.Clear();
            await Task.Delay(50);
            inputSim.Keyboard.ModifiedKeyStroke(VirtualKeyCode.CONTROL, VirtualKeyCode.VK_C);

            bool foundText = false;
            for (int i = 0; i < 20; i++)
            {
                if (Clipboard.ContainsText()) { foundText = true; break; }
                await Task.Delay(50);
            }

            if (!foundText) return;

            ConvertAndShow(appIcon);
        }

        private static async void ConvertAndShow(Icon appIcon)
        {
            string jp = Clipboard.GetText().Trim();
            if (string.IsNullOrEmpty(jp)) return;

            string romajiResult = "";
            string englishResult = "";

            try
            {
                // Run Romaji and Translation in parallel
                var taskRomaji = converter.Convert(jp, To.Romaji, Mode.Okurigana, RomajiSystem.Hepburn, "(", ")");
                var taskTranslate = translator.TranslateAsync(jp, "en");

                await Task.WhenAll(taskRomaji, taskTranslate);

                // Romaji result format will be similar to 日本語(nihongo)
                romajiResult = taskRomaji.Result;
                englishResult = taskTranslate.Result.Translation;
            }
            catch (Exception ex)
            {
                if (string.IsNullOrEmpty(romajiResult)) romajiResult = "Error: " + ex.Message;
                englishResult = "Translation Error (Check Internet connection)";
            }

            ShowSplitPopup(romajiResult, englishResult, appIcon);
        }

        private static void ShowSplitPopup(string romaji, string english, Icon appIcon)
        {
            // Form size increased for readability
            var f = new Form
            {
                TopMost = true,
                Icon = appIcon,
                FormBorderStyle = FormBorderStyle.Sizable,
                StartPosition = FormStartPosition.Manual,
                Size = new Size(650, 650),
                BackColor = Color.FromArgb(240, 240, 240),
                Text = "Translation Result",
                MinimizeBox = false,
                ShowInTaskbar = false
            };

            // Splitter set to give English (Panel2) 60% of the height
            var splitContainer = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
                // SplitterDistance = 220 means 40% for Romaji (Panel1)
                SplitterDistance = 220,
                SplitterWidth = 6
            };

            // Romaji Pane
            var lblRomaji = new Label { Text = "ROMAJI (Inline):", Dock = DockStyle.Top, Font = new Font("Segoe UI", 11, FontStyle.Bold), Height = 20 };
            var txtRomaji = CreateScrollableBox(romaji);
            splitContainer.Panel1.Padding = new Padding(10);
            splitContainer.Panel1.Controls.Add(txtRomaji);
            splitContainer.Panel1.Controls.Add(lblRomaji);

            // English Pane
            var lblEnglish = new Label { Text = "ENGLISH TRANSLATION:", Dock = DockStyle.Top, Font = new Font("Segoe UI", 11, FontStyle.Bold), Height = 20 };
            var txtEnglish = CreateScrollableBox(english);
            splitContainer.Panel2.Padding = new Padding(10);
            splitContainer.Panel2.Controls.Add(txtEnglish);
            splitContainer.Panel2.Controls.Add(lblEnglish);

            f.Controls.Add(splitContainer);

            // Position Bottom Right
            Rectangle workingArea = Screen.PrimaryScreen.WorkingArea;
            f.Location = new Point(workingArea.Width - f.Width - 30, workingArea.Height - f.Height - 30);

            f.Load += (s, e) => lblRomaji.Focus();
            f.Show();
            f.Activate();
        }

        private static TextBox CreateScrollableBox(string content)
        {
            return new TextBox
            {
                Text = content,
                Font = new Font("Meiryo UI", 13F, FontStyle.Regular), // Increased Font Size
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Dock = DockStyle.Fill,
                BackColor = Color.White,
                BorderStyle = BorderStyle.FixedSingle
            };
        }

        // --- STARTUP LOGIC ---
        private static void SetStartup(bool enable)
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                if (enable)
                    rk.SetValue(APP_NAME, Application.ExecutablePath);
                else
                    rk.DeleteValue(APP_NAME, false);
            }
            catch (Exception ex)
            {
                MessageBox.Show("Could not update startup setting: " + ex.Message);
            }
        }

        private static bool IsStartupEnabled()
        {
            try
            {
                RegistryKey rk = Registry.CurrentUser.OpenSubKey("SOFTWARE\\Microsoft\\Windows\\CurrentVersion\\Run", true);
                return rk.GetValue(APP_NAME) != null;
            }
            catch { return false; }
        }

        // --- HOTKEY LOGIC (CTRL + F2) ---
        public static bool RegisterHotKeyInternal(IntPtr handle) => RegisterHotKey(handle, 9000, 0x0002, 0x71);
        public static bool UnregisterHotKeyInternal(IntPtr handle) => UnregisterHotKey(handle, 9000);
    }

    class HiddenForm : Form
    {
        public event Action HotkeyPressed = delegate { };
        public HiddenForm()
        {
            ShowInTaskbar = false;
            WindowState = FormWindowState.Minimized;
            FormBorderStyle = FormBorderStyle.None;
            Load += (s, e) => Program.RegisterHotKeyInternal(this.Handle);
            FormClosed += (s, e) => Program.UnregisterHotKeyInternal(this.Handle);
        }
        protected override void WndProc(ref Message m)
        {
            if (m.Msg == 0x0312 && m.WParam.ToInt32() == 9000) HotkeyPressed();
            base.WndProc(ref m);
        }
    }
}