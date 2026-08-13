using System.Diagnostics;

namespace CryptoSigTool.Installer;

internal sealed class InstallerForm : Form
{
    private readonly TextBox _path = new() { Text = InstallPaths.DefaultInstallDirectory, Dock = DockStyle.Fill };
    private readonly CheckBox _desktopShortcut = new() { Text = "Создать ярлык на рабочем столе", Checked = true, AutoSize = true };
    private readonly CheckBox _launch = new() { Text = "Запустить CryptoSigTool после установки", Checked = true, AutoSize = true };
    private readonly Button _install = new() { Text = "Установить", AutoSize = true, Padding = new Padding(18, 6, 18, 6) };
    private readonly Label _status = new() { AutoSize = true, ForeColor = Color.DimGray };

    public InstallerForm()
    {
        Text = "Установка CryptoSigTool 1.4";
        StartPosition = FormStartPosition.CenterScreen;
        FormBorderStyle = FormBorderStyle.FixedDialog;
        MaximizeBox = false;
        MinimizeBox = false;
        ClientSize = new Size(660, 320);
        Font = new Font("Segoe UI", 10F);
        Icon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!);

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, Padding = new Padding(24), ColumnCount = 1, RowCount = 8 };
        root.Controls.Add(new Label { Text = "CryptoSigTool", AutoSize = true, Font = new Font("Segoe UI Semibold", 22F), ForeColor = Color.FromArgb(27, 42, 65) });
        root.Controls.Add(new Label { Text = "Системная установка для всех пользователей компьютера", AutoSize = true, ForeColor = Color.FromArgb(86, 99, 117) });
        root.Controls.Add(new Label { Text = "Папка установки", AutoSize = true, Margin = new Padding(0, 18, 0, 4) });

        var pathRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        pathRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        pathRow.Controls.Add(_path, 0, 0);
        var browse = new Button { Text = "Обзор...", AutoSize = true };
        browse.Click += (_, _) => Browse();
        pathRow.Controls.Add(browse, 1, 0);
        root.Controls.Add(pathRow);
        root.Controls.Add(_desktopShortcut);
        root.Controls.Add(_launch);
        root.Controls.Add(_status);
        root.Controls.Add(_install);
        Controls.Add(root);

        _install.Click += (_, _) => Install();
        AcceptButton = _install;
    }

    private void Browse()
    {
        using var dialog = new FolderBrowserDialog { Description = "Выберите папку установки", UseDescriptionForTitle = true, SelectedPath = _path.Text };
        if (dialog.ShowDialog(this) == DialogResult.OK) _path.Text = Path.Combine(dialog.SelectedPath, InstallPaths.ProductName);
    }

    private void Install()
    {
        _install.Enabled = false;
        UseWaitCursor = true;
        try
        {
            _status.Text = "Установка...";
            Application.DoEvents();
            InstallerEngine.Install(_path.Text.Trim(), _desktopShortcut.Checked);
            _status.Text = "Установка завершена.";
            if (_launch.Checked)
                Process.Start(new ProcessStartInfo { FileName = Path.Combine(Path.GetFullPath(_path.Text.Trim()), "CryptoSigTool.exe"), UseShellExecute = true });
            MessageBox.Show("CryptoSigTool успешно установлен.", "CryptoSigTool", MessageBoxButtons.OK, MessageBoxIcon.Information);
            Close();
        }
        catch (Exception ex)
        {
            _status.Text = "Ошибка установки.";
            MessageBox.Show(ex.Message, "Установка CryptoSigTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
            _install.Enabled = true;
        }
        finally
        {
            UseWaitCursor = false;
        }
    }
}
