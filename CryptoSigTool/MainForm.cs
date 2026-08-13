using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace CryptoSigTool;

internal sealed class MainForm : Form
{
    private readonly CryptoProService _cryptoPro = new();
    private readonly Label _statusLabel = new();
    private readonly RichTextBox _log = new();
    private readonly TabControl _tabs = new();

    private readonly ListView _containerCertificates = new()
    {
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
        ShowItemToolTips = true,
        Dock = DockStyle.Fill,
        Height = 280
    };
    private readonly Label _containerStatus = new() { AutoSize = true, ForeColor = Color.FromArgb(86, 99, 117) };
    private readonly Button _scanContainersButton = SecondaryButton("Обновить список");
    private readonly Button _installCertificatesButton = PrimaryButton("Добавить выбранные");

    private readonly TextBox _mergeContent = new();
    private readonly TextBox _mergeSig1 = new();
    private readonly TextBox _mergeSig2 = new();
    private readonly TextBox _mergeOutput = new();
    private readonly CheckBox _mergeBase64 = new() { Text = "Выход в Base64 (обычно не требуется)" };
    private readonly Button _mergeButton = PrimaryButton("Объединить и проверить");

    private readonly RadioButton _signFileMode = new() { Text = "Один файл", Checked = true, AutoSize = true };
    private readonly RadioButton _signFolderMode = new() { Text = "Папка", AutoSize = true };
    private readonly TextBox _signInput = new();
    private readonly TextBox _signOutputFolder = new();
    private readonly CheckBox _recursive = new() { Text = "Включая вложенные папки", AutoSize = true };
    private readonly CheckedListBox _certificates = new() { CheckOnClick = true, IntegralHeight = false };
    private readonly ComboBox _algorithm = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _signatureType = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _encoding = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly Button _signButton = PrimaryButton("Подписать и проверить");

    private readonly TextBox _verifyContent = new();
    private readonly TextBox _verifySignature = new();
    private readonly Button _verifyButton = PrimaryButton("Проверить подпись");
    private readonly RichTextBox _verifyDetails = new()
    {
        ReadOnly = true,
        BackColor = Color.FromArgb(248, 250, 252),
        BorderStyle = BorderStyle.FixedSingle,
        Font = new Font("Segoe UI", 9.5F),
        DetectUrls = false,
        Height = 250,
        Dock = DockStyle.Fill
    };

    private bool _busy;

    public MainForm()
    {
        Text = "CryptoSigTool 1.4 — подписи CryptoPro";
        using (var iconStream = typeof(MainForm).Assembly.GetManifestResourceStream("CryptoSigTool.AppIcon.ico"))
        {
            if (iconStream is not null)
            {
                using var embeddedIcon = new Icon(iconStream);
                Icon = (Icon)embeddedIcon.Clone();
            }
        }
        MinimumSize = new Size(850, 680);
        Size = new Size(980, 790);
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(245, 247, 250);
        AllowDrop = true;

        BuildUi();
        WireEvents();
        RefreshCertificates();
        Shown += async (_, _) => await RefreshContainerCertificatesAsync();

        var status = _cryptoPro.IsInstalled
            ? $"CryptoPro найден: {_cryptoPro.ToolPath}"
            : "CryptoPro не найден — подпись и проверка недоступны";
        SetStatus(status, _cryptoPro.IsInstalled ? Color.FromArgb(23, 121, 78) : Color.Firebrick);
        Log(status);
    }

    internal string TabOrderForTest => string.Join(" | ", _tabs.TabPages.Cast<TabPage>().Select(x => x.Text));

    private void BuildUi()
    {
        var root = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 4,
            Padding = new Padding(16),
            BackColor = BackColor
        };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 10) };
        header.Controls.Add(new Label
        {
            Text = "CryptoSigTool",
            Font = new Font("Segoe UI Semibold", 20F),
            AutoSize = true,
            ForeColor = Color.FromArgb(27, 42, 65)
        });
        header.Controls.Add(new Label
        {
            Text = "Объединение, создание и проверка электронных подписей CMS/PKCS#7",
            AutoSize = true,
            ForeColor = Color.FromArgb(86, 99, 117)
        });
        root.Controls.Add(header, 0, 0);

        _tabs.Dock = DockStyle.Fill;
        _tabs.Padding = new Point(18, 8);
        _tabs.TabPages.Add(BuildCertificateTab());
        _tabs.TabPages.Add(BuildSignTab());
        _tabs.TabPages.Add(BuildVerifyTab());
        _tabs.TabPages.Add(BuildMergeTab());
        root.Controls.Add(_tabs, 0, 1);

        _statusLabel.AutoSize = true;
        _statusLabel.Margin = new Padding(2, 10, 2, 6);
        root.Controls.Add(_statusLabel, 0, 2);

        _log.Dock = DockStyle.Fill;
        _log.ReadOnly = true;
        _log.BackColor = Color.FromArgb(27, 32, 41);
        _log.ForeColor = Color.FromArgb(220, 226, 235);
        _log.BorderStyle = BorderStyle.None;
        _log.Font = new Font("Cascadia Mono", 9F);
        _log.DetectUrls = false;
        root.Controls.Add(_log, 0, 3);

        Controls.Add(root);
    }

    private TabPage BuildMergeTab()
    {
        var page = NewPage("Объединить .sig");
        var layout = FormGrid();
        AddPathRow(layout, 0, "Исходный файл", _mergeContent, () => PickOpenFile(_mergeContent, "Все файлы|*.*"));
        AddPathRow(layout, 1, "Подпись № 1", _mergeSig1, () => PickOpenFile(_mergeSig1, SignatureFilter));
        AddPathRow(layout, 2, "Подпись № 2", _mergeSig2, () => PickOpenFile(_mergeSig2, SignatureFilter));
        AddPathRow(layout, 3, "Итоговый .sig", _mergeOutput, () => PickSaveFile(_mergeOutput, "SIG (*.sig)|*.sig|PKCS#7 (*.p7s)|*.p7s"));
        layout.Controls.Add(_mergeBase64, 1, 4);
        layout.SetColumnSpan(_mergeBase64, 2);
        layout.Controls.Add(Info("Обе исходные подписи сначала проверяются на выбранном файле. Итоговый контейнер содержит обоих подписантов и также проверяется CryptoPro."), 1, 5);
        layout.SetColumnSpan(layout.GetControlFromPosition(1, 5)!, 2);
        layout.Controls.Add(_mergeButton, 1, 6);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildCertificateTab()
    {
        var page = NewPage("Добавить сертификат");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(Info("Здесь отображаются сертификаты, найденные внутри контейнеров закрытых ключей CryptoPro, но отсутствующие в личном хранилище текущего пользователя. Закрытые ключи не копируются."), 0, 0);
        layout.Controls.Add(_containerStatus, 0, 1);

        _containerCertificates.Columns.Add("Владелец / организация", 260);
        _containerCertificates.Columns.Add("Тип ключа", 110);
        _containerCertificates.Columns.Add("Действителен до", 110);
        _containerCertificates.Columns.Add("Контейнер CryptoPro", 320);
        _containerCertificates.Columns.Add("Отпечаток SHA-1", 280);
        layout.Controls.Add(_containerCertificates, 0, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(_scanContainersButton);
        buttons.Controls.Add(_installCertificatesButton);
        layout.Controls.Add(buttons, 0, 3);
        layout.Controls.Add(Info("Установка выполняется только в «Сертификаты — текущий пользователь — Личное (CurrentUser\\My)» и не требует прав администратора."), 0, 4);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildSignTab()
    {
        var page = NewPage("Подписать");
        var layout = FormGrid();
        var modes = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill };
        modes.Controls.AddRange(new Control[] { _signFileMode, _signFolderMode, _recursive });
        layout.Controls.Add(new Label { Text = "Источник", AutoSize = true, Anchor = AnchorStyles.Left }, 0, 0);
        layout.Controls.Add(modes, 1, 0);
        layout.SetColumnSpan(modes, 2);
        AddPathRow(layout, 1, "Файл или папка", _signInput, BrowseSignInput);
        AddPathRow(layout, 2, "Папка результата", _signOutputFolder, () => PickFolder(_signOutputFolder));

        layout.Controls.Add(new Label { Text = "Сертификаты", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 7, 3, 3) }, 0, 3);
        _certificates.Dock = DockStyle.Fill;
        _certificates.Height = 125;
        layout.Controls.Add(_certificates, 1, 3);
        var certButtons = new FlowLayoutPanel { FlowDirection = FlowDirection.TopDown, AutoSize = true };
        var refresh = SecondaryButton("Обновить");
        refresh.Click += (_, _) => RefreshCertificates();
        certButtons.Controls.Add(refresh);
        certButtons.Controls.Add(new Label { Text = "Выберите 1 или 2", AutoSize = true, ForeColor = Color.DimGray, Margin = new Padding(3, 8, 3, 3) });
        layout.Controls.Add(certButtons, 2, 3);

        _algorithm.Items.AddRange(new object[] { "GOST12_256", "GOST12_512", "GOST94_256", "SHA1" });
        _algorithm.SelectedIndex = 0;
        AddControlRow(layout, 4, "Алгоритм хеша", _algorithm);

        _signatureType.Items.AddRange(new object[] { "Отсоединённая (.sig)", "Вложенная CMS/PKCS#7 (.p7s)" });
        _signatureType.SelectedIndex = 0;
        AddControlRow(layout, 5, "Тип подписи", _signatureType);

        _encoding.Items.AddRange(new object[] { "DER (двоичный, рекомендуется)", "Base64 (текстовый)" });
        _encoding.SelectedIndex = 0;
        AddControlRow(layout, 6, "Формат контейнера", _encoding);

        var note = Info("При двух сертификатах программа создаёт две подписи через CryptoPro и объединяет их в один контейнер. PIN-код запрашивает CryptoPro и программа его не сохраняет. Вложенная CMS — не визуальная подпись внутри PDF.");
        layout.Controls.Add(note, 1, 7);
        layout.SetColumnSpan(note, 2);
        layout.Controls.Add(_signButton, 1, 8);
        page.Controls.Add(layout);
        return page;
    }

    private TabPage BuildVerifyTab()
    {
        var page = NewPage("Проверить");
        var layout = FormGrid();
        AddPathRow(layout, 0, "Исходный файл", _verifyContent, () => PickOpenFile(_verifyContent, "Все файлы|*.*"));
        AddPathRow(layout, 1, "Подпись", _verifySignature, () => PickOpenFile(_verifySignature, SignatureFilter));
        var note = Info("Для отсоединённой подписи укажите оба файла. Для вложенной CMS/PKCS#7 поле «Исходный файл» можно оставить пустым.");
        layout.Controls.Add(note, 1, 2);
        layout.SetColumnSpan(note, 2);
        layout.Controls.Add(_verifyButton, 1, 3);
        var copyDetails = SecondaryButton("Скопировать данные");
        copyDetails.Click += (_, _) =>
        {
            if (!string.IsNullOrWhiteSpace(_verifyDetails.Text)) Clipboard.SetText(_verifyDetails.Text);
        };
        layout.Controls.Add(copyDetails, 2, 3);
        layout.Controls.Add(new Label { Text = "Данные подписи", AutoSize = true, Anchor = AnchorStyles.Left | AnchorStyles.Top, Margin = new Padding(3, 12, 3, 3) }, 0, 4);
        layout.Controls.Add(_verifyDetails, 1, 4);
        layout.SetColumnSpan(_verifyDetails, 2);
        page.Controls.Add(layout);
        return page;
    }

    private void WireEvents()
    {
        _scanContainersButton.Click += async (_, _) => await RefreshContainerCertificatesAsync();
        _installCertificatesButton.Click += async (_, _) => await InstallSelectedCertificatesAsync();
        _mergeButton.Click += async (_, _) => await MergeAsync();
        _signButton.Click += async (_, _) => await SignAsync();
        _verifyButton.Click += async (_, _) => await VerifyAsync();
        _signFileMode.CheckedChanged += (_, _) => _recursive.Enabled = _signFolderMode.Checked;
        _recursive.Enabled = false;
        _certificates.ItemCheck += (_, e) =>
        {
            if (e.NewValue != CheckState.Checked) return;
            var checkedAfter = _certificates.CheckedItems.Count + (_certificates.GetItemChecked(e.Index) ? 0 : 1);
            if (checkedAfter > 2)
            {
                e.NewValue = CheckState.Unchecked;
                BeginInvoke(() => MessageBox.Show(this, "Можно выбрать не более двух сертификатов.", "CryptoSigTool", MessageBoxButtons.OK, MessageBoxIcon.Information));
            }
        };
    }

    private async Task RefreshContainerCertificatesAsync()
    {
        if (_busy) return;
        await RunBusyAsync(async () =>
        {
            EnsureCryptoPro();
            _containerStatus.Text = "Поиск контейнеров CryptoPro...";
            var missing = await _cryptoPro.GetMissingContainerCertificatesAsync(message => _containerStatus.Text = message);
            PopulateContainerCertificates(missing);
            var text = missing.Count == 0
                ? "Все обнаруженные сертификаты уже находятся в личном хранилище пользователя."
                : $"Найдено сертификатов для добавления: {missing.Count}.";
            _containerStatus.Text = text;
            SetStatus(text, missing.Count == 0 ? Color.FromArgb(23, 121, 78) : Color.FromArgb(36, 99, 235));
            Log(text);
        });
    }

    private async Task InstallSelectedCertificatesAsync()
    {
        if (_busy) return;
        var selected = _containerCertificates.CheckedItems
            .Cast<ListViewItem>()
            .Select(x => (ContainerCertificateItem)x.Tag!)
            .ToArray();
        if (selected.Length == 0)
        {
            ShowError("Отметьте хотя бы один сертификат.");
            return;
        }
        var answer = MessageBox.Show(this,
            $"Добавить выбранные сертификаты ({selected.Length}) в личное хранилище текущего пользователя?",
            "CryptoSigTool", MessageBoxButtons.YesNo, MessageBoxIcon.Question);
        if (answer != DialogResult.Yes) return;

        await RunBusyAsync(async () =>
        {
            EnsureCryptoPro();
            var installed = 0;
            foreach (var certificate in selected)
            {
                _containerStatus.Text = $"Добавление: {certificate.DisplayName}";
                Log($"Установка сертификата из {certificate.ContainerName} ({certificate.KeyTypeDisplay})...");
                var result = await _cryptoPro.InstallContainerCertificateAsync(certificate);
                EnsureSuccess(result, $"Не удалось добавить сертификат «{certificate.DisplayName}»");
                if (!_cryptoPro.IsCurrentUserCertificateInstalled(certificate.Thumbprint))
                    throw new InvalidOperationException($"CryptoPro завершил установку без ошибки, но сертификат «{certificate.DisplayName}» не найден в CurrentUser\\My.");
                installed++;
                Log($"Добавлен: {certificate.DisplayName}, {certificate.Thumbprint}");
            }

            RefreshCertificates();
            var missing = await _cryptoPro.GetMissingContainerCertificatesAsync(message => _containerStatus.Text = message);
            PopulateContainerCertificates(missing);
            _containerStatus.Text = $"Добавлено сертификатов: {installed}. Осталось доступных для добавления: {missing.Count}.";
            SetStatus($"Сертификаты добавлены: {installed}", Color.FromArgb(23, 121, 78));
        });
    }

    private void PopulateContainerCertificates(IEnumerable<ContainerCertificateItem> certificates)
    {
        _containerCertificates.BeginUpdate();
        try
        {
            _containerCertificates.Items.Clear();
            foreach (var certificate in certificates)
            {
                var item = new ListViewItem(certificate.DisplayName) { Tag = certificate, ToolTipText = certificate.Subject };
                item.SubItems.Add(certificate.KeyTypeDisplay);
                item.SubItems.Add(certificate.NotAfter.ToString("dd.MM.yyyy"));
                item.SubItems.Add(certificate.ContainerName);
                item.SubItems.Add(certificate.Thumbprint);
                _containerCertificates.Items.Add(item);
            }
        }
        finally
        {
            _containerCertificates.EndUpdate();
        }
    }

    private async Task MergeAsync()
    {
        if (_busy) return;
        var content = _mergeContent.Text.Trim();
        var sig1 = _mergeSig1.Text.Trim();
        var sig2 = _mergeSig2.Text.Trim();
        if (!RequireFiles(content, sig1, sig2)) return;
        var output = _mergeOutput.Text.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            output = content + ".2signers.sig";
            _mergeOutput.Text = output;
        }

        await RunBusyAsync(async () =>
        {
            EnsureCryptoPro();
            Log("Проверка первой подписи...");
            EnsureSuccess(await _cryptoPro.VerifyDetachedAsync(content, sig1), "Первая подпись недействительна");
            Log("Проверка второй подписи...");
            EnsureSuccess(await _cryptoPro.VerifyDetachedAsync(content, sig2), "Вторая подпись недействительна");

            Log("Объединение контейнеров...");
            var merged = CmsMerger.Merge(new[] { sig1, sig2 }, output, _mergeBase64.Checked);
            if (!merged.Detached) throw new InvalidDataException("На вкладке объединения ожидаются отсоединённые подписи.");
            Log($"Контейнер создан: подписантов {merged.Signers}, сертификатов {merged.Certificates}.");

            var result = await _cryptoPro.VerifyDetachedAsync(content, output);
            EnsureSuccess(result, "Итоговая подпись не прошла проверку");
            SetStatus("Готово: объединённая подпись создана и проверена", Color.FromArgb(23, 121, 78));
            Log($"УСПЕШНО: {output}");
        });
    }

    private async Task SignAsync()
    {
        if (_busy) return;
        var input = _signInput.Text.Trim();
        var outputRoot = _signOutputFolder.Text.Trim();
        if ((_signFileMode.Checked && !File.Exists(input)) || (_signFolderMode.Checked && !Directory.Exists(input)))
        {
            ShowError("Выберите существующий файл или папку.");
            return;
        }
        if (string.IsNullOrWhiteSpace(outputRoot))
        {
            outputRoot = _signFileMode.Checked ? Path.GetDirectoryName(Path.GetFullPath(input))! : Path.Combine(input, "подписано");
            _signOutputFolder.Text = outputRoot;
        }
        var selected = _certificates.CheckedItems.Cast<CertificateItem>().ToArray();
        if (selected.Length is < 1 or > 2)
        {
            ShowError("Выберите один или два сертификата.");
            return;
        }
        if (selected.Any(x => !x.HasPrivateKey))
        {
            ShowError("У выбранного сертификата нет доступного закрытого ключа.");
            return;
        }

        var detached = _signatureType.SelectedIndex == 0;
        var base64 = _encoding.SelectedIndex == 1;
        var algorithm = _algorithm.SelectedItem!.ToString()!;

        await RunBusyAsync(async () =>
        {
            EnsureCryptoPro();
            Directory.CreateDirectory(outputRoot);
            var files = EnumerateInputs(input, outputRoot).ToArray();
            if (files.Length == 0) throw new InvalidOperationException("В выбранной папке нет файлов для подписи.");
            Log($"К подписи подготовлено файлов: {files.Length}.");

            var succeeded = 0;
            foreach (var file in files)
            {
                var relative = _signFolderMode.Checked ? Path.GetRelativePath(input, file) : Path.GetFileName(file);
                var outputDir = Path.Combine(outputRoot, Path.GetDirectoryName(relative) ?? "");
                Directory.CreateDirectory(outputDir);
                var extension = detached ? ".sig" : ".p7s";
                var output = GetAvailablePath(Path.Combine(outputDir, Path.GetFileName(relative) + extension));
                Log($"Подпись: {file}");

                if (selected.Length == 1)
                {
                    EnsureSuccess(await _cryptoPro.SignAsync(file, output, selected[0], algorithm, detached, base64), $"Не удалось подписать {file}");
                }
                else
                {
                    var tempDir = Path.Combine(Path.GetTempPath(), "CryptoSigTool", Guid.NewGuid().ToString("N"));
                    Directory.CreateDirectory(tempDir);
                    try
                    {
                        var temp1 = Path.Combine(tempDir, "one.p7s");
                        var temp2 = Path.Combine(tempDir, "two.p7s");
                        EnsureSuccess(await _cryptoPro.SignAsync(file, temp1, selected[0], algorithm, detached, false), $"Не удалось подписать первым сертификатом: {file}");
                        EnsureSuccess(await _cryptoPro.SignAsync(file, temp2, selected[1], algorithm, detached, false), $"Не удалось подписать вторым сертификатом: {file}");
                        var merged = CmsMerger.Merge(new[] { temp1, temp2 }, output, base64);
                        if (merged.Signers != 2) throw new InvalidDataException("Итоговый контейнер не содержит двух подписантов.");
                    }
                    finally
                    {
                        try { Directory.Delete(tempDir, true); } catch { }
                    }
                }

                var verify = detached
                    ? await _cryptoPro.VerifyDetachedAsync(file, output)
                    : await _cryptoPro.VerifyAttachedAsync(output);
                EnsureSuccess(verify, $"Проверка созданной подписи не пройдена: {file}");
                Log($"Проверено: {output} (подписантов: {CmsMerger.GetSignerCount(output)})");
                succeeded++;
            }
            SetStatus($"Готово: подписано и проверено файлов — {succeeded}", Color.FromArgb(23, 121, 78));
        });
    }

    private async Task VerifyAsync()
    {
        if (_busy) return;
        var content = _verifyContent.Text.Trim();
        var signature = _verifySignature.Text.Trim();
        if (!File.Exists(signature))
        {
            ShowError("Выберите существующий файл подписи.");
            return;
        }
        if (!string.IsNullOrWhiteSpace(content) && !File.Exists(content))
        {
            ShowError("Исходный файл не найден.");
            return;
        }
        _verifyDetails.Clear();

        await RunBusyAsync(async () =>
        {
            EnsureCryptoPro();
            var result = string.IsNullOrWhiteSpace(content)
                ? await _cryptoPro.VerifyAttachedAsync(signature)
                : await _cryptoPro.VerifyDetachedAsync(content, signature);
            Log(result.Output.Trim());
            EnsureSuccess(result, "Подпись не прошла проверку");
            var inspection = CmsMerger.Inspect(signature);
            _verifyDetails.Text = FormatInspection(inspection);
            SetStatus($"Подпись действительна. Подписантов в контейнере: {inspection.Signers.Count}", Color.FromArgb(23, 121, 78));
            Log(string.Join("; ", inspection.Signers.Select(x => $"Подписант {x.Number}: {x.DisplayName}, {x.DigestAlgorithm}")));
        });
    }

    private static string FormatInspection(SignatureInspection inspection)
    {
        var lines = new List<string>
        {
            "ПРОВЕРКА CRYPTOPRO: ПОДПИСЬ ДЕЙСТВИТЕЛЬНА",
            $"Тип контейнера: {(inspection.Detached ? "отсоединённая подпись" : "вложенная CMS/PKCS#7")}",
            $"Тип содержимого: {inspection.ContentType}",
            $"Подписантов: {inspection.Signers.Count}; сертификатов в контейнере: {inspection.CertificateCount}",
            ""
        };

        foreach (var signer in inspection.Signers)
        {
            var signingTime = signer.SigningTime is null
                ? "не указано"
                : signer.SigningTime.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss zzz");
            var certificatePeriod = signer.CertificateNotBefore is null || signer.CertificateNotAfter is null
                ? "—"
                : $"{signer.CertificateNotBefore:dd.MM.yyyy HH:mm:ss} — {signer.CertificateNotAfter:dd.MM.yyyy HH:mm:ss}";
            var certificateNow = signer.CertificateNotBefore <= DateTime.Now && signer.CertificateNotAfter >= DateTime.Now
                ? "действует на текущую дату"
                : "не действует на текущую дату";

            lines.Add($"ПОДПИСАНТ № {signer.Number}: {signer.DisplayName}");
            lines.Add($"Время подписания (заявленное): {signingTime}");
            var timestamp = signer.TimestampTime is null
                ? (signer.HasTimestampToken ? "атрибут присутствует, время не извлечено" : "отсутствует")
                : signer.TimestampTime.Value.ToLocalTime().ToString("dd.MM.yyyy HH:mm:ss zzz");
            lines.Add($"Штамп времени УЦ: {timestamp}");
            lines.Add($"Алгоритм хеша: {signer.DigestAlgorithm}");
            lines.Add($"Алгоритм подписи: {signer.SignatureAlgorithm}");
            lines.Add($"Хеш подписанного содержимого: {signer.MessageDigest}");
            lines.Add($"Субъект сертификата: {signer.Subject}");
            lines.Add($"Издатель сертификата: {signer.Issuer}");
            lines.Add($"Серийный номер: {signer.SerialNumber}");
            lines.Add($"Отпечаток SHA-1: {signer.Thumbprint}");
            lines.Add($"Срок сертификата: {certificatePeriod} ({certificateNow})");
            lines.Add($"Идентификатор подписанта: {signer.SignerIdentifier}");
            lines.Add("");
        }

        lines.Add("Примечание: «время подписания» — значение атрибута signingTime, указанное подписантом. Криптографически подтверждённое время требует отдельного штампа времени УЦ.");
        return string.Join(Environment.NewLine, lines);
    }

    private IEnumerable<string> EnumerateInputs(string input, string outputRoot)
    {
        if (_signFileMode.Checked) return new[] { input };
        var option = _recursive.Checked ? SearchOption.AllDirectories : SearchOption.TopDirectoryOnly;
        var outputFull = Path.GetFullPath(outputRoot).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
        return Directory.EnumerateFiles(input, "*", option)
            .Where(path => !Path.GetFullPath(path).StartsWith(outputFull, StringComparison.OrdinalIgnoreCase))
            .Where(path => !new[] { ".sig", ".p7s" }.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .ToArray();
    }

    private static string GetAvailablePath(string desired)
    {
        if (!File.Exists(desired)) return desired;
        var directory = Path.GetDirectoryName(desired)!;
        var extension = Path.GetExtension(desired);
        var stem = Path.GetFileNameWithoutExtension(desired);
        for (var index = 2; ; index++)
        {
            var candidate = Path.Combine(directory, $"{stem} ({index}){extension}");
            if (!File.Exists(candidate)) return candidate;
        }
    }

    private async Task RunBusyAsync(Func<Task> action)
    {
        _busy = true;
        ToggleActions(false);
        UseWaitCursor = true;
        try
        {
            await action();
        }
        catch (Exception ex)
        {
            SetStatus("Операция завершилась ошибкой", Color.Firebrick);
            Log("ОШИБКА: " + ex.Message);
            MessageBox.Show(this, ex.Message, "CryptoSigTool", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally
        {
            UseWaitCursor = false;
            ToggleActions(true);
            _busy = false;
        }
    }

    private void RefreshCertificates()
    {
        _certificates.Items.Clear();
        var certificates = _cryptoPro.GetCertificates();
        foreach (var cert in certificates) _certificates.Items.Add(cert);
        Log($"Найдено сертификатов в хранилищах Windows: {certificates.Count}; с закрытым ключом: {certificates.Count(x => x.HasPrivateKey)}.");
    }

    private void BrowseSignInput()
    {
        if (_signFolderMode.Checked) PickFolder(_signInput);
        else PickOpenFile(_signInput, "Все файлы|*.*");
    }

    private void PickOpenFile(TextBox target, string filter)
    {
        using var dialog = new OpenFileDialog { Filter = filter, CheckFileExists = true };
        if (File.Exists(target.Text)) dialog.FileName = target.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private void PickSaveFile(TextBox target, string filter)
    {
        using var dialog = new SaveFileDialog { Filter = filter, AddExtension = true, OverwritePrompt = true };
        if (!string.IsNullOrWhiteSpace(target.Text)) dialog.FileName = target.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.FileName;
    }

    private void PickFolder(TextBox target)
    {
        using var dialog = new FolderBrowserDialog { UseDescriptionForTitle = true, Description = "Выберите папку" };
        if (Directory.Exists(target.Text)) dialog.SelectedPath = target.Text;
        if (dialog.ShowDialog(this) == DialogResult.OK) target.Text = dialog.SelectedPath;
    }

    private void EnsureCryptoPro()
    {
        if (!_cryptoPro.IsInstalled) throw new InvalidOperationException("CryptoPro CSP не найден. Установите CryptoPro CSP 5.");
    }

    private static void EnsureSuccess(ProcessResult result, string title)
    {
        if (!result.Success) throw new InvalidOperationException($"{title}.\r\n\r\n{result.Output.Trim()}");
    }

    private bool RequireFiles(params string[] paths)
    {
        var missing = paths.FirstOrDefault(path => !File.Exists(path));
        if (missing is null) return true;
        ShowError(string.IsNullOrWhiteSpace(missing) ? "Заполните все поля выбора файлов." : $"Файл не найден: {missing}");
        return false;
    }

    private void ShowError(string text) => MessageBox.Show(this, text, "CryptoSigTool", MessageBoxButtons.OK, MessageBoxIcon.Warning);

    private void ToggleActions(bool enabled)
    {
        _scanContainersButton.Enabled = enabled;
        _installCertificatesButton.Enabled = enabled;
        _mergeButton.Enabled = enabled;
        _signButton.Enabled = enabled;
        _verifyButton.Enabled = enabled;
    }

    private void Log(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        _log.AppendText($"[{DateTime.Now:HH:mm:ss}] {text.Trim()}\r\n");
        _log.SelectionStart = _log.TextLength;
        _log.ScrollToCaret();
    }

    private void SetStatus(string text, Color color)
    {
        _statusLabel.Text = text;
        _statusLabel.ForeColor = color;
    }

    private static TabPage NewPage(string text) => new() { Text = text, BackColor = Color.White, Padding = new Padding(18), AutoScroll = true };

    private static TableLayoutPanel FormGrid()
    {
        var layout = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 3, RowCount = 10 };
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 155));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        layout.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        return layout;
    }

    private static void AddPathRow(TableLayoutPanel layout, int row, string label, TextBox box, Action browse)
    {
        box.Dock = DockStyle.Fill;
        box.Margin = new Padding(3, 5, 8, 5);
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left, Margin = new Padding(3, 8, 3, 3) }, 0, row);
        layout.Controls.Add(box, 1, row);
        var button = SecondaryButton("Обзор...");
        button.Click += (_, _) => browse();
        layout.Controls.Add(button, 2, row);
    }

    private static void AddControlRow(TableLayoutPanel layout, int row, string label, Control control)
    {
        control.Dock = DockStyle.Fill;
        control.Margin = new Padding(3, 5, 8, 5);
        layout.Controls.Add(new Label { Text = label, AutoSize = true, Anchor = AnchorStyles.Left }, 0, row);
        layout.Controls.Add(control, 1, row);
        layout.SetColumnSpan(control, 2);
    }

    private static Label Info(string text) => new()
    {
        Text = text,
        AutoSize = true,
        MaximumSize = new Size(650, 0),
        ForeColor = Color.FromArgb(86, 99, 117),
        Margin = new Padding(3, 10, 3, 10)
    };

    private static Button PrimaryButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.FromArgb(36, 99, 235),
        ForeColor = Color.White,
        Padding = new Padding(12, 5, 12, 5),
        Margin = new Padding(3, 10, 3, 3),
        Cursor = Cursors.Hand
    };

    private static Button SecondaryButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.System,
        Margin = new Padding(3, 3, 3, 3)
    };

    private const string SignatureFilter = "Подписи (*.sig;*.p7s)|*.sig;*.p7s|Все файлы|*.*";
}
