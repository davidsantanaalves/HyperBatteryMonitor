using System.ComponentModel;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Drawing.Text;
using System.Xml.Linq;
using System.Runtime.InteropServices;
using Microsoft.Win32;
using HyperXBatteryTray.Devices;

namespace HyperXBatteryTray.Settings;

public sealed partial class SettingsForm : Form
{
    private sealed class RestoreDefaultsDialog : Form
    {
        private readonly bool _dark;

        public RestoreDefaultsDialog(string title, string question, string yesText, string noText, bool dark)
        {
            _dark = dark;

            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ClientSize = new Size(390, 120);
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            // ShowDialog(settingsForm) keeps this modal above its owner only.
            DoubleBuffered = true;

            BackColor = dark ? DarkBackground : Color.White;
            ForeColor = dark ? Color.WhiteSmoke : LightText;

            PictureBox questionIcon = new()
            {
                Location = new Point(20, 18),
                Size = new Size(32, 32),
                SizeMode = PictureBoxSizeMode.CenterImage,
                Image = SystemIcons.Question.ToBitmap(),
                BackColor = Color.Transparent
            };

            Label questionLabel = new()
            {
                AutoSize = false,
                Location = new Point(62, 18),
                Size = new Size(305, 42),
                Text = question,
                ForeColor = ForeColor,
                BackColor = Color.Transparent,
                TextAlign = ContentAlignment.MiddleLeft
            };

            Panel buttonPanel = new()
            {
                Dock = DockStyle.Bottom,
                Height = 44,
                BackColor = dark ? Color.FromArgb(38, 41, 44) : Color.FromArgb(245, 246, 248)
            };

            Button yesButton = CreateButton(yesText, DialogResult.Yes, true);
            Button noButton = CreateButton(noText, DialogResult.No, false);

            yesButton.Location = new Point(216, 10);
            noButton.Location = new Point(300, 10);
            buttonPanel.Controls.Add(yesButton);
            buttonPanel.Controls.Add(noButton);

            Controls.Add(questionIcon);
            Controls.Add(questionLabel);
            Controls.Add(buttonPanel);

            AcceptButton = yesButton;
            CancelButton = noButton;

            Shown += (_, _) =>
            {
                ApplyDialogTitleBarTheme(_dark);
                yesButton.Focus();
            };
        }

        private Button CreateButton(string text, DialogResult result, bool primary)
        {
            Button button = new()
            {
                Text = text,
                DialogResult = result,
                Size = new Size(74, 24),
                FlatStyle = FlatStyle.Flat,
                BackColor = primary
                    ? (_dark ? Color.FromArgb(0, 122, 255) : Color.White)
                    : (_dark ? Color.FromArgb(52, 56, 60) : Color.White),
                ForeColor = primary
                    ? (_dark ? Color.White : Accent)
                    : (_dark ? Color.WhiteSmoke : LightText),
                Font = new Font("Segoe UI", 9f),
                UseVisualStyleBackColor = false,
                TabStop = true
            };

            button.FlatAppearance.BorderColor = primary
                ? Accent
                : (_dark ? Color.FromArgb(82, 87, 93) : Color.FromArgb(190, 196, 204));
            button.FlatAppearance.BorderSize = 1;

            return button;
        }

        private void ApplyDialogTitleBarTheme(bool dark)
        {
            if (!IsHandleCreated)
                return;

            try
            {
                int useDarkMode = dark ? 1 : 0;
                _ = DwmSetWindowAttribute(Handle, DwmwaUseImmersiveDarkMode, ref useDarkMode, sizeof(int));
            }
            catch
            {
                // Keep the dialog functional if the DWM attribute is unavailable.
            }
        }
    }
}
