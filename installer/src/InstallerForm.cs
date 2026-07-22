using System;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows.Forms;

namespace BoplEight.Installer
{
    internal sealed class InstallerForm : Form
    {
        private static readonly Color BackgroundColor = Color.FromArgb(20, 24, 33);
        private static readonly Color PanelColor = Color.FromArgb(31, 37, 49);
        private static readonly Color BorderColor = Color.FromArgb(65, 75, 94);
        private static readonly Color PrimaryColor = Color.FromArgb(99, 210, 177);
        private static readonly Color PrimaryHoverColor = Color.FromArgb(121, 225, 194);
        private static readonly Color TextColor = Color.FromArgb(240, 243, 248);
        private static readonly Color MutedTextColor = Color.FromArgb(165, 174, 190);
        private static readonly Color WarningColor = Color.FromArgb(244, 190, 82);
        private static readonly Color ErrorColor = Color.FromArgb(241, 105, 112);

        private readonly TextBox pathTextBox;
        private readonly Label statusLabel;
        private readonly Button installButton;
        private readonly Button uninstallButton;
        private readonly ProgressBar progressBar;

        internal InstallerForm()
        {
            Text = "BoplEight Setup";
            ClientSize = new Size(680, 440);
            MinimumSize = new Size(680, 440);
            MaximumSize = new Size(680, 440);
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            BackColor = BackgroundColor;
            ForeColor = TextColor;
            Font = new Font("Segoe UI", 9F, FontStyle.Regular, GraphicsUnit.Point);

            var header = new Panel
            {
                BackColor = PanelColor,
                Location = new Point(0, 0),
                Size = new Size(ClientSize.Width, 112),
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right
            };
            Controls.Add(header);

            var accent = new Panel
            {
                BackColor = PrimaryColor,
                Location = new Point(0, 0),
                Size = new Size(7, header.Height),
                Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left
            };
            header.Controls.Add(accent);

            var title = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 24F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = TextColor,
                Location = new Point(31, 18),
                Text = "BoplEight"
            };
            header.Controls.Add(title);

            var subtitle = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = MutedTextColor,
                Location = new Point(35, 70),
                Text = "2-8 player setup for Bopl Battle  |  Includes BepInEx 5.4.23.2"
            };
            header.Controls.Add(subtitle);

            var version = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 9F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = PrimaryColor,
                Location = new Point(590, 25),
                Text = "v" + InstallerCore.InstallerVersion
            };
            header.Controls.Add(version);

            var folderLabel = new Label
            {
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
                ForeColor = TextColor,
                Location = new Point(34, 139),
                Text = "Bopl Battle folder"
            };
            Controls.Add(folderLabel);

            pathTextBox = new TextBox
            {
                BackColor = PanelColor,
                ForeColor = TextColor,
                BorderStyle = BorderStyle.FixedSingle,
                Font = new Font("Segoe UI", 10F, FontStyle.Regular, GraphicsUnit.Point),
                Location = new Point(37, 168),
                Size = new Size(505, 29)
            };
            pathTextBox.TextChanged += delegate { RefreshValidation(); };
            Controls.Add(pathTextBox);

            Button browseButton = CreateSecondaryButton("Browse", new Point(555, 166), new Size(91, 32));
            browseButton.Click += BrowseButtonClick;
            Controls.Add(browseButton);

            var statusPanel = new Panel
            {
                BackColor = PanelColor,
                Location = new Point(37, 218),
                Size = new Size(609, 74)
            };
            statusPanel.Paint += delegate(object sender, PaintEventArgs args)
            {
                using (var pen = new Pen(BorderColor))
                {
                    args.Graphics.DrawRectangle(pen, 0, 0, statusPanel.Width - 1, statusPanel.Height - 1);
                }
            };
            Controls.Add(statusPanel);

            statusLabel = new Label
            {
                AutoEllipsis = true,
                Font = new Font("Segoe UI", 9.5F, FontStyle.Regular, GraphicsUnit.Point),
                ForeColor = MutedTextColor,
                Location = new Point(18, 13),
                Size = new Size(573, 48),
                TextAlign = ContentAlignment.MiddleLeft,
                Text = "Looking for Bopl Battle in your Steam libraries..."
            };
            statusPanel.Controls.Add(statusLabel);

            progressBar = new ProgressBar
            {
                Location = new Point(37, 309),
                Size = new Size(609, 5),
                Style = ProgressBarStyle.Marquee,
                MarqueeAnimationSpeed = 20,
                Visible = false
            };
            Controls.Add(progressBar);

            installButton = CreatePrimaryButton("Install BoplEight", new Point(37, 337), new Size(210, 48));
            installButton.Click += InstallButtonClick;
            installButton.Enabled = false;
            Controls.Add(installButton);

            uninstallButton = CreateSecondaryButton("Uninstall", new Point(261, 337), new Size(132, 48));
            uninstallButton.Click += UninstallButtonClick;
            uninstallButton.Enabled = false;
            Controls.Add(uninstallButton);

            Button closeButton = CreateSecondaryButton("Close", new Point(514, 337), new Size(132, 48));
            closeButton.Click += delegate { Close(); };
            Controls.Add(closeButton);

            var footer = new Label
            {
                AutoSize = true,
                ForeColor = MutedTextColor,
                Location = new Point(37, 408),
                Text = "All friends need this same installer version. Launch the game normally through Steam afterward."
            };
            Controls.Add(footer);

            Shown += InstallerFormShown;
        }

        private void InstallerFormShown(object sender, EventArgs args)
        {
            try
            {
                System.Collections.Generic.IList<string> directories = InstallerCore.DiscoverGameDirectories();
                if (directories.Count > 0)
                {
                    pathTextBox.Text = directories[0];
                    return;
                }

                statusLabel.ForeColor = WarningColor;
                statusLabel.Text = "Bopl Battle was not found automatically. Click Browse, then select the folder containing BoplBattle.exe.";
            }
            catch (Exception exception)
            {
                statusLabel.ForeColor = WarningColor;
                statusLabel.Text = "Automatic detection was unavailable: " + exception.Message + " Use Browse to select the game folder.";
            }
        }

        private void BrowseButtonClick(object sender, EventArgs args)
        {
            using (var dialog = new FolderBrowserDialog())
            {
                dialog.Description = "Select the Bopl Battle folder containing BoplBattle.exe";
                dialog.ShowNewFolderButton = false;
                if (Directory.Exists(pathTextBox.Text))
                {
                    dialog.SelectedPath = pathTextBox.Text;
                }

                if (dialog.ShowDialog(this) == DialogResult.OK)
                {
                    pathTextBox.Text = dialog.SelectedPath;
                }
            }
        }

        private void InstallButtonClick(object sender, EventArgs args)
        {
            if (!EnsureGameIsClosed())
            {
                return;
            }

            SetBusy(true, "Installing BepInEx and BoplEight...");
            try
            {
                using (Stream payload = Assembly.GetExecutingAssembly().GetManifestResourceStream("BoplEight.Payload.zip"))
                {
                    if (payload == null)
                    {
                        throw new InvalidOperationException("The embedded installation payload is missing.");
                    }
                    InstallerCore.Install(pathTextBox.Text, payload);
                }

                statusLabel.ForeColor = PrimaryColor;
                statusLabel.Text = "Installation complete. Launch Bopl Battle normally through Steam and invite friends who installed this same version.";
                MessageBox.Show(
                    "BoplEight and BepInEx are installed.\n\nLaunch Bopl Battle normally through Steam.",
                    "Installation complete",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (UnauthorizedAccessException)
            {
                ShowError("Windows blocked access to the game folder. Right-click this installer and choose Run as administrator.");
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
            finally
            {
                SetBusy(false, null);
                RefreshValidation();
            }
        }

        private void UninstallButtonClick(object sender, EventArgs args)
        {
            if (!EnsureGameIsClosed())
            {
                return;
            }

            DialogResult confirmation = MessageBox.Show(
                "Remove BoplEight from this Bopl Battle installation?\n\nBepInEx will remain because other mods may use it.",
                "Uninstall BoplEight",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirmation != DialogResult.Yes)
            {
                return;
            }

            SetBusy(true, "Removing BoplEight...");
            try
            {
                InstallerCore.Uninstall(pathTextBox.Text);
                statusLabel.ForeColor = PrimaryColor;
                statusLabel.Text = "BoplEight was removed. Shared BepInEx files were left in place.";
            }
            catch (Exception exception)
            {
                ShowError(exception.Message);
            }
            finally
            {
                SetBusy(false, null);
                RefreshValidation();
            }
        }

        private bool EnsureGameIsClosed()
        {
            if (Process.GetProcessesByName("BoplBattle").Length == 0)
            {
                return true;
            }

            MessageBox.Show(
                "Close Bopl Battle before installing or uninstalling BoplEight.",
                "Bopl Battle is running",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
            return false;
        }

        private void RefreshValidation()
        {
            string reason;
            bool valid = InstallerCore.ValidateGameDirectory(pathTextBox.Text, out reason);
            bool installed = valid && InstallerCore.IsInstalled(pathTextBox.Text);
            installButton.Enabled = valid;
            uninstallButton.Enabled = installed;
            installButton.Text = installed ? "Repair / Update" : "Install BoplEight";

            if (valid)
            {
                statusLabel.ForeColor = installed ? PrimaryColor : TextColor;
                statusLabel.Text = installed
                    ? "Supported game build detected. BoplEight is installed and can be repaired or updated."
                    : "Supported game build detected. Ready to install BepInEx and BoplEight.";
            }
            else
            {
                statusLabel.ForeColor = string.IsNullOrWhiteSpace(pathTextBox.Text) ? MutedTextColor : ErrorColor;
                statusLabel.Text = reason;
            }
        }

        private void SetBusy(bool busy, string message)
        {
            UseWaitCursor = busy;
            progressBar.Visible = busy;
            pathTextBox.Enabled = !busy;
            installButton.Enabled = !busy && installButton.Enabled;
            uninstallButton.Enabled = !busy && uninstallButton.Enabled;
            if (!string.IsNullOrEmpty(message))
            {
                statusLabel.ForeColor = TextColor;
                statusLabel.Text = message;
                statusLabel.Refresh();
            }
        }

        private void ShowError(string message)
        {
            statusLabel.ForeColor = ErrorColor;
            statusLabel.Text = message;
            MessageBox.Show(message, "BoplEight Setup", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }

        private static Button CreatePrimaryButton(string text, Point location, Size size)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = PrimaryColor,
                ForeColor = Color.FromArgb(13, 31, 29),
                Font = new Font("Segoe UI Semibold", 10F, FontStyle.Bold, GraphicsUnit.Point),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = PrimaryHoverColor;
            return button;
        }

        private static Button CreateSecondaryButton(string text, Point location, Size size)
        {
            var button = new Button
            {
                Text = text,
                Location = location,
                Size = size,
                FlatStyle = FlatStyle.Flat,
                BackColor = PanelColor,
                ForeColor = TextColor,
                Font = new Font("Segoe UI Semibold", 9.5F, FontStyle.Bold, GraphicsUnit.Point),
                Cursor = Cursors.Hand
            };
            button.FlatAppearance.BorderColor = BorderColor;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.MouseOverBackColor = Color.FromArgb(43, 51, 66);
            return button;
        }
    }
}
