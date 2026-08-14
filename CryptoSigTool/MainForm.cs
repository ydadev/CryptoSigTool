using System.Diagnostics;
using System.Security.Cryptography.X509Certificates;

namespace CryptoSigTool;

internal sealed class MainForm : Form
{
    private readonly CryptoProService _cryptoPro = new();
    private readonly PdfViewerService _pdfViewer = new();
    private readonly PdfSigningService _pdfSigning;
    private readonly Label _statusLabel = new();
    private readonly RichTextBox _log = new();
    private readonly TabControl _tabs = new();
    private TableLayoutPanel? _rootLayout;
    private Control? _headerPanel;
    private TabPage? _pdfTab;
    private TabPage? _removeCertificateTab;

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

    private readonly ListView _removableCertificates = new()
    {
        View = View.Details,
        CheckBoxes = true,
        FullRowSelect = true,
        GridLines = true,
        HideSelection = false,
        ShowItemToolTips = true,
        Dock = DockStyle.Fill,
        Height = 300
    };
    private readonly Label _removableCertificateStatus = new() { AutoSize = true, ForeColor = Color.FromArgb(86, 99, 117) };
    private readonly Button _refreshRemovableCertificatesButton = SecondaryButton("Обновить список");
    private readonly Button _removeCertificatesButton = DangerButton("Удалить выбранные");

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

    private readonly PdfPageCanvas _pdfCanvas = new();
    private readonly TextBox _pdfInput = new();
    private readonly TextBox _pdfOutput = new();
    private readonly ComboBox _pdfCertificates = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _pdfAlgorithm = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly ComboBox _pdfStampDesign = new() { DropDownStyle = ComboBoxStyle.DropDownList };
    private readonly CheckBox _pdfVisualize63Fz = new() { Text = "Визуализация в соответствии с 63-ФЗ", Checked = true, AutoSize = true };
    private readonly CheckBox _pdfShowDate = new() { Text = "Добавить дату и время в штамп", Checked = false, AutoSize = true };
    private readonly CheckBox _pdfTimestamp = new() { Text = "Добавить штамп времени TSP (CAdES-T)", AutoSize = true };
    private readonly CheckBox _pdfEvidence = new() { Text = "Добавить доказательства подлинности (CAdES-XLT1)", AutoSize = true };
    private readonly ComboBox _pdfTspAddress = new() { DropDownStyle = ComboBoxStyle.DropDown };
    private readonly TextBox _pdfLogo = new();
    private readonly TextBox _pdfReason = new() { Text = "Подписание документа" };
    private readonly TextBox _pdfLocation = new();
    private readonly Label _pdfPageLabel = new() { AutoSize = true, Text = "Страница — / —", Margin = new Padding(8, 8, 8, 3) };
    private readonly Label _pdfSelectionLabel = new() { AutoSize = true, ForeColor = Color.FromArgb(86, 99, 117), MaximumSize = new Size(350, 0) };
    private readonly Label _pdfFeatureStatus = new() { AutoSize = true, MaximumSize = new Size(350, 0) };
    private readonly Button _pdfOpenButton = SecondaryButton("Открыть PDF...");
    private readonly Button _pdfPreviousButton = SecondaryButton("◀");
    private readonly Button _pdfNextButton = SecondaryButton("▶");
    private readonly Button _pdfZoomOutButton = SecondaryButton("−");
    private readonly Button _pdfZoomInButton = SecondaryButton("+");
    private readonly Button _pdfFitButton = SecondaryButton("По размеру окна");
    private readonly Button _pdfSignButton = PrimaryButton("Подписать PDF");
    private uint _pdfPageIndex;

    private bool _busy;

    public MainForm()
    {
        _pdfSigning = new PdfSigningService(_cryptoPro);
        Text = "CryptoSigTool 1.8.0 — подписи CryptoPro";
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
        WindowState = FormWindowState.Maximized;
        StartPosition = FormStartPosition.CenterScreen;
        Font = new Font("Segoe UI", 10F);
        BackColor = Color.FromArgb(245, 247, 250);
        AllowDrop = true;

        BuildUi();
        WireEvents();
        RefreshCertificates();
        RefreshRemovableCertificates();
        Shown += async (_, _) => await RefreshContainerCertificatesAsync();

        var status = _cryptoPro.IsInstalled
            ? $"CryptoPro найден: {_cryptoPro.ToolPath}"
            : "CryptoPro не найден — подпись и проверка недоступны";
        SetStatus(status, _cryptoPro.IsInstalled ? Color.FromArgb(23, 121, 78) : Color.Firebrick);
        Log(status);
    }

    internal string TabOrderForTest => string.Join(" | ", _tabs.TabPages.Cast<TabPage>().Select(x => x.Text));
    internal bool PdfShowDateDefaultForTest => _pdfShowDate.Checked;

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
        _rootLayout = root;
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 65));
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 35));

        var header = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, Margin = new Padding(0, 0, 0, 10) };
        _headerPanel = header;
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
        _removeCertificateTab = BuildRemoveCertificateTab();
        _tabs.TabPages.Add(_removeCertificateTab);
        _tabs.TabPages.Add(BuildSignTab());
        _tabs.TabPages.Add(BuildVerifyTab());
        _tabs.TabPages.Add(BuildMergeTab());
        _pdfTab = BuildPdfTab();
        _tabs.TabPages.Add(_pdfTab);
        _tabs.TabPages.Add(BuildDisclaimerTab());
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

    private TabPage BuildDisclaimerTab()
    {
        var page = NewPage("Дисклеймер");
        var layout = new TableLayoutPanel
        {
            Dock = DockStyle.Fill,
            ColumnCount = 1,
            RowCount = 3,
            Padding = new Padding(24),
            BackColor = Color.White
        };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.Controls.Add(new Label
        {
            Text = DisclaimerContent.Title,
            AutoSize = true,
            Font = new Font("Segoe UI Semibold", 18F),
            ForeColor = Color.FromArgb(27, 42, 65),
            Margin = new Padding(0, 0, 0, 16)
        }, 0, 0);
        layout.Controls.Add(new RichTextBox
        {
            Text = DisclaimerContent.Text,
            ReadOnly = true,
            Dock = DockStyle.Fill,
            BorderStyle = BorderStyle.None,
            BackColor = Color.White,
            ForeColor = Color.FromArgb(45, 55, 72),
            Font = new Font("Segoe UI", 11F),
            DetectUrls = false,
            TabStop = false
        }, 0, 1);
        var repositoryLink = new LinkLabel
        {
            Text = "Документация и исходный код: github.com/ydadev/CryptoSigTool",
            AutoSize = true,
            Margin = new Padding(0, 16, 0, 0)
        };
        repositoryLink.LinkClicked += (_, _) => Process.Start(new ProcessStartInfo(
            "https://github.com/ydadev/CryptoSigTool") { UseShellExecute = true });
        layout.Controls.Add(repositoryLink, 0, 2);
        page.Controls.Add(layout);
        return page;
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

    private TabPage BuildPdfTab()
    {
        var page = NewPage("PDF 63-ФЗ");
        page.Padding = new Padding(0);
        page.AutoScroll = false;

        var root = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 2, BackColor = Color.FromArgb(245, 247, 250) };
        root.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        root.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

        var toolbar = new FlowLayoutPanel
        {
            Dock = DockStyle.Fill,
            AutoSize = true,
            Padding = new Padding(8, 5, 8, 5),
            BackColor = Color.White,
            WrapContents = false
        };
        toolbar.Controls.AddRange(new Control[]
        {
            _pdfOpenButton, _pdfPreviousButton, _pdfNextButton, _pdfPageLabel,
            _pdfZoomOutButton, _pdfZoomInButton, _pdfFitButton
        });
        _pdfPreviousButton.Enabled = false;
        _pdfNextButton.Enabled = false;
        root.Controls.Add(toolbar, 0, 0);

        var split = new SplitContainer
        {
            Size = new Size(1200, 700),
            Dock = DockStyle.Fill,
            Orientation = Orientation.Vertical,
            FixedPanel = FixedPanel.Panel2,
            Panel2MinSize = 360,
            SplitterWidth = 5,
            SplitterDistance = 800
        };
        split.Panel1.Controls.Add(_pdfCanvas);

        var sideScroll = new Panel { Dock = DockStyle.Fill, AutoScroll = true, BackColor = Color.White, Padding = new Padding(14) };
        var side = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 1, RowCount = 1 };
        side.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        var row = 0;

        void AddControl(Control control, int top = 5)
        {
            control.Dock = DockStyle.Top;
            control.Margin = new Padding(0, top, 0, 4);
            side.Controls.Add(control, 0, row++);
        }

        void AddCaption(string text)
        {
            AddControl(new Label
            {
                Text = text,
                AutoSize = true,
                Font = new Font("Segoe UI Semibold", 10F),
                ForeColor = Color.FromArgb(27, 42, 65)
            }, row == 0 ? 0 : 12);
        }

        void AddLabeled(string label, Control control)
        {
            AddControl(new Label { Text = label, AutoSize = true, ForeColor = Color.FromArgb(86, 99, 117) }, 6);
            AddControl(control, 0);
        }

        AddCaption("Документ");
        _pdfInput.ReadOnly = true;
        AddLabeled("Исходный PDF", _pdfInput);
        var outputRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        outputRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pdfOutput.Dock = DockStyle.Fill;
        outputRow.Controls.Add(_pdfOutput, 0, 0);
        var outputBrowse = SecondaryButton("Обзор...");
        outputBrowse.Click += (_, _) => PickSaveFile(_pdfOutput, "PDF (*.pdf)|*.pdf");
        outputRow.Controls.Add(outputBrowse, 1, 0);
        AddLabeled("Подписанная копия", outputRow);

        AddCaption("Подпись");
        var certificateRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        certificateRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        certificateRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pdfCertificates.Dock = DockStyle.Fill;
        certificateRow.Controls.Add(_pdfCertificates, 0, 0);
        var refreshCertificates = SecondaryButton("↻");
        refreshCertificates.Click += (_, _) => RefreshCertificates();
        certificateRow.Controls.Add(refreshCertificates, 1, 0);
        AddLabeled("Сертификат", certificateRow);

        _pdfAlgorithm.Items.AddRange(new object[] { "GOST12_256", "GOST12_512", "GOST94_256" });
        _pdfAlgorithm.SelectedIndex = 0;
        AddLabeled("Алгоритм", _pdfAlgorithm);
        AddControl(_pdfVisualize63Fz, 8);
        _pdfStampDesign.Items.AddRange(PdfStampDesignCatalog.Items.Cast<object>().ToArray());
        _pdfStampDesign.SelectedIndex = 0;
        AddLabeled("Дизайн штампа", _pdfStampDesign);
        AddControl(_pdfShowDate, 0);
        AddControl(_pdfSelectionLabel, 3);

        var logoRow = new TableLayoutPanel { Dock = DockStyle.Top, AutoSize = true, ColumnCount = 2 };
        logoRow.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100));
        logoRow.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));
        _pdfLogo.Dock = DockStyle.Fill;
        logoRow.Controls.Add(_pdfLogo, 0, 0);
        var logoBrowse = SecondaryButton("Обзор...");
        logoBrowse.Click += (_, _) => PickOpenFile(_pdfLogo, "Изображения (*.jpg;*.jpeg;*.png)|*.jpg;*.jpeg;*.png|Все файлы|*.*");
        logoRow.Controls.Add(logoBrowse, 1, 0);
        AddLabeled("Логотип (необязательно)", logoRow);
        AddLabeled("Причина подписания", _pdfReason);
        AddLabeled("Место подписания", _pdfLocation);

        AddCaption("Доверенное время и доказательства");
        AddControl(_pdfTimestamp, 5);
        AddControl(_pdfEvidence, 0);
        _pdfTspAddress.Enabled = false;
        _pdfTspAddress.Items.AddRange(TspServiceStore.Load().Cast<object>().ToArray());
        AddLabeled("Адрес службы TSP", _pdfTspAddress);
        _pdfFeatureStatus.ForeColor = PdfSigningService.EnhancedCadesAvailable
            ? Color.FromArgb(23, 121, 78)
            : Color.Firebrick;
        _pdfFeatureStatus.Text = PdfSigningService.EnhancedCadesAvailable
            ? "Компоненты CryptoPro CAdES обнаружены: доступны CAdES-T и CAdES-XLT1 при наличии действующей службы TSP/OCSP."
            : "Компоненты CryptoPro CAdES не найдены. Базовая PDF-подпись доступна, TSP и XLT1 отключены.";
        _pdfTimestamp.Enabled = PdfSigningService.EnhancedCadesAvailable;
        _pdfEvidence.Enabled = PdfSigningService.EnhancedCadesAvailable;
        AddControl(_pdfFeatureStatus, 4);
        AddControl(Info("Выделите область штампа мышью на странице. Изображение штампа — только визуальное представление; юридическую силу обеспечивает встроенная криптографическая подпись PDF."), 8);
        AddControl(_pdfSignButton, 8);

        sideScroll.Controls.Add(side);
        split.Panel2.Controls.Add(sideScroll);
        root.Controls.Add(split, 0, 1);
        page.Controls.Add(root);
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

    private TabPage BuildRemoveCertificateTab()
    {
        var page = NewPage("Удалить сертификаты");
        var layout = new TableLayoutPanel { Dock = DockStyle.Fill, ColumnCount = 1, RowCount = 5 };
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));
        layout.RowStyles.Add(new RowStyle(SizeType.AutoSize));

        layout.Controls.Add(Info("Здесь отображаются все сертификаты из личного хранилища текущего пользователя (CurrentUser\\My). Действующие показаны чёрным, истёкшие и ещё не вступившие в силу — красным."), 0, 0);
        layout.Controls.Add(_removableCertificateStatus, 0, 1);

        _removableCertificates.Columns.Add("Владелец / организация", 300);
        _removableCertificates.Columns.Add("Статус", 135);
        _removableCertificates.Columns.Add("Действителен с", 110);
        _removableCertificates.Columns.Add("Действителен до", 110);
        _removableCertificates.Columns.Add("Закрытый ключ", 110);
        _removableCertificates.Columns.Add("Отпечаток SHA-1", 300);
        layout.Controls.Add(_removableCertificates, 0, 2);

        var buttons = new FlowLayoutPanel { AutoSize = true, Dock = DockStyle.Fill, Margin = new Padding(0, 8, 0, 0) };
        buttons.Controls.Add(_refreshRemovableCertificatesButton);
        buttons.Controls.Add(_removeCertificatesButton);
        layout.Controls.Add(buttons, 0, 3);
        layout.Controls.Add(Info("Удаляется только публичный сертификат из CurrentUser\\My. Контейнер закрытого ключа на токене или другом носителе не удаляется; при необходимости сертификат можно позднее добавить снова из контейнера CryptoPro."), 0, 4);
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
        _refreshRemovableCertificatesButton.Click += (_, _) => RefreshRemovableCertificates();
        _removeCertificatesButton.Click += async (_, _) => await RemoveSelectedCertificatesAsync();
        _mergeButton.Click += async (_, _) => await MergeAsync();
        _signButton.Click += async (_, _) => await SignAsync();
        _verifyButton.Click += async (_, _) => await VerifyAsync();
        _pdfOpenButton.Click += async (_, _) => await OpenPdfAsync();
        _pdfPreviousButton.Click += async (_, _) => await ChangePdfPageAsync(-1);
        _pdfNextButton.Click += async (_, _) => await ChangePdfPageAsync(1);
        _pdfZoomOutButton.Click += (_, _) => _pdfCanvas.SetZoom(_pdfCanvas.Zoom / 1.2F);
        _pdfZoomInButton.Click += (_, _) => _pdfCanvas.SetZoom(_pdfCanvas.Zoom * 1.2F);
        _pdfFitButton.Click += (_, _) => _pdfCanvas.FitToWindow();
        _pdfSignButton.Click += async (_, _) => await SignPdfAsync();
        _pdfCanvas.SelectionChanged += (_, _) => UpdatePdfSelectionLabel();
        _pdfTimestamp.CheckedChanged += (_, _) =>
        {
            _pdfTspAddress.Enabled = _pdfTimestamp.Checked || _pdfEvidence.Checked;
            if (!_pdfTimestamp.Checked && _pdfEvidence.Checked) _pdfEvidence.Checked = false;
        };
        _pdfEvidence.CheckedChanged += (_, _) =>
        {
            if (_pdfEvidence.Checked) _pdfTimestamp.Checked = true;
            _pdfTspAddress.Enabled = _pdfTimestamp.Checked || _pdfEvidence.Checked;
        };
        _pdfVisualize63Fz.CheckedChanged += (_, _) =>
        {
            _pdfCanvas.Cursor = _pdfVisualize63Fz.Checked ? Cursors.Cross : Cursors.Default;
            _pdfStampDesign.Enabled = _pdfVisualize63Fz.Checked;
            UpdatePdfSelectionLabel();
        };
        _tabs.SelectedIndexChanged += (_, _) => UpdateWorkspaceMode();
        FormClosed += (_, _) => _pdfViewer.Dispose();
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

    private async Task OpenPdfAsync()
    {
        using var dialog = new OpenFileDialog { Filter = "PDF (*.pdf)|*.pdf", CheckFileExists = true };
        if (File.Exists(_pdfInput.Text)) dialog.FileName = _pdfInput.Text;
        if (dialog.ShowDialog(this) != DialogResult.OK) return;

        await RunBusyAsync(async () =>
        {
            await _pdfViewer.OpenAsync(dialog.FileName);
            if (_pdfViewer.PageCount == 0) throw new InvalidDataException("PDF не содержит страниц.");
            _pdfInput.Text = dialog.FileName;
            _pdfOutput.Text = GetAvailablePath(Path.Combine(
                Path.GetDirectoryName(dialog.FileName)!,
                Path.GetFileNameWithoutExtension(dialog.FileName) + ".signed.pdf"));
            _pdfPageIndex = 0;
            await RenderCurrentPdfPageAsync();
            SetStatus($"PDF открыт: страниц {_pdfViewer.PageCount}", Color.FromArgb(23, 121, 78));
            Log($"Открыт PDF для визуальной подписи: {dialog.FileName}");
        });
    }

    private async Task ChangePdfPageAsync(int delta)
    {
        if (_pdfViewer.PageCount == 0) return;
        var next = Math.Clamp((long)_pdfPageIndex + delta, 0, (long)_pdfViewer.PageCount - 1);
        if ((uint)next == _pdfPageIndex) return;
        _pdfPageIndex = (uint)next;
        await RunBusyAsync(RenderCurrentPdfPageAsync);
    }

    private async Task RenderCurrentPdfPageAsync()
    {
        var image = await _pdfViewer.RenderPageAsync(_pdfPageIndex);
        _pdfCanvas.SetPage(image);
        _pdfPageLabel.Text = $"Страница {_pdfPageIndex + 1} / {_pdfViewer.PageCount}";
        _pdfPreviousButton.Enabled = _pdfPageIndex > 0;
        _pdfNextButton.Enabled = _pdfPageIndex + 1 < _pdfViewer.PageCount;
        UpdatePdfSelectionLabel();
    }

    private void UpdatePdfSelectionLabel()
    {
        if (!_pdfVisualize63Fz.Checked)
        {
            _pdfSelectionLabel.Text = "Визуальный штамп отключён: будет создано невидимое поле PDF-подписи.";
            return;
        }

        var selection = _pdfCanvas.SelectionNormalized;
        _pdfSelectionLabel.Text = selection.IsEmpty
            ? "Область штампа не выбрана. Проведите мышью по нужному месту страницы."
            : $"Область: X {selection.X:P0}, Y {selection.Y:P0}, ширина {selection.Width:P0}, высота {selection.Height:P0}.";
    }

    private async Task SignPdfAsync()
    {
        if (_busy) return;
        if (!File.Exists(_pdfInput.Text))
        {
            ShowError("Сначала откройте PDF-файл.");
            return;
        }
        if (_pdfCertificates.SelectedItem is not CertificateItem certificate)
        {
            ShowError("Выберите сертификат для PDF-подписи.");
            return;
        }
        if (_pdfVisualize63Fz.Checked && _pdfCanvas.SelectionNormalized.IsEmpty)
        {
            ShowError("Выделите мышью область для визуального штампа.");
            return;
        }
        if ((_pdfTimestamp.Checked || _pdfEvidence.Checked) && !PdfSigningService.EnhancedCadesAvailable)
        {
            ShowError("Для CAdES-T/XLT1 не найдены компоненты CryptoPro CAdES/TSP/OCSP.");
            return;
        }

        var output = _pdfOutput.Text.Trim();
        if (string.IsNullOrWhiteSpace(output))
        {
            output = GetAvailablePath(Path.Combine(
                Path.GetDirectoryName(_pdfInput.Text)!,
                Path.GetFileNameWithoutExtension(_pdfInput.Text) + ".signed.pdf"));
            _pdfOutput.Text = output;
        }

        var request = new PdfSignatureRequest(
            _pdfInput.Text,
            output,
            certificate,
            _pdfAlgorithm.SelectedItem?.ToString() ?? "GOST12_256",
            checked((int)_pdfPageIndex),
            _pdfCanvas.SelectionNormalized,
            new PdfStampSettings(
                _pdfVisualize63Fz.Checked,
                _pdfShowDate.Checked,
                string.IsNullOrWhiteSpace(_pdfLogo.Text) ? null : _pdfLogo.Text.Trim(),
                _pdfReason.Text.Trim(),
                _pdfLocation.Text.Trim(),
                DateTime.Now,
                (_pdfStampDesign.SelectedItem as PdfStampDesignItem)?.Value ?? PdfStampDesign.AcrobatBlack),
            _pdfTimestamp.Checked,
            _pdfEvidence.Checked,
            string.IsNullOrWhiteSpace(_pdfTspAddress.Text) ? null : _pdfTspAddress.Text.Trim());

        await RunBusyAsync(async () =>
        {
            EnsureCryptoPro();
            Log($"Формирование встроенной PDF-подписи: {request.InputPath}");
            await _pdfSigning.SignAsync(request);
            if (!File.Exists(output) || new FileInfo(output).Length == 0)
                throw new InvalidDataException("Подписанный PDF не был создан.");
            var verifications = await PdfSignatureVerifier.VerifyAllAsync(output, _cryptoPro);
            if (request.AddTimestamp && request.TspAddress is not null)
                TspServiceStore.Remember(request.TspAddress);
            SetStatus("PDF успешно подписан", Color.FromArgb(23, 121, 78));
            Log($"Создан и проверен подписанный PDF: {output}; встроенных подписей: {verifications.Count}; последняя digest: {verifications[^1].DigestAlgorithm}.");
            MessageBox.Show(this,
                $"Подписанный PDF сохранён:\r\n{output}\r\n\r\nПроверьте подпись в CryptoPro PDF или другом средстве, поддерживающем ГОСТ PDF-подписи.",
                "CryptoSigTool", MessageBoxButtons.OK, MessageBoxIcon.Information);
        });
    }

    private void UpdateWorkspaceMode()
    {
        if (_rootLayout is null || _headerPanel is null || _pdfTab is null) return;
        if (ReferenceEquals(_tabs.SelectedTab, _removeCertificateTab) && !_busy)
            RefreshRemovableCertificates();
        var pdfMode = ReferenceEquals(_tabs.SelectedTab, _pdfTab);
        _headerPanel.Visible = !pdfMode;
        _statusLabel.Visible = !pdfMode;
        _log.Visible = !pdfMode;
        _rootLayout.Padding = pdfMode ? new Padding(0) : new Padding(16);
        _rootLayout.RowStyles[0].SizeType = pdfMode ? SizeType.Absolute : SizeType.AutoSize;
        _rootLayout.RowStyles[0].Height = pdfMode ? 0 : 0;
        _rootLayout.RowStyles[1].SizeType = SizeType.Percent;
        _rootLayout.RowStyles[1].Height = pdfMode ? 100 : 65;
        _rootLayout.RowStyles[2].SizeType = pdfMode ? SizeType.Absolute : SizeType.AutoSize;
        _rootLayout.RowStyles[2].Height = pdfMode ? 0 : 0;
        _rootLayout.RowStyles[3].SizeType = pdfMode ? SizeType.Absolute : SizeType.Percent;
        _rootLayout.RowStyles[3].Height = pdfMode ? 0 : 35;
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

    private void RefreshRemovableCertificates()
    {
        var certificates = _cryptoPro.GetCurrentUserPersonalCertificates();
        _removableCertificates.BeginUpdate();
        try
        {
            _removableCertificates.Items.Clear();
            foreach (var certificate in certificates)
            {
                var item = new ListViewItem(certificate.DisplayName)
                {
                    Tag = certificate,
                    ToolTipText = certificate.Subject,
                    ForeColor = certificate.IsValidNow ? Color.Black : Color.Firebrick
                };
                item.SubItems.Add(certificate.Status);
                item.SubItems.Add(certificate.NotBefore.ToString("dd.MM.yyyy"));
                item.SubItems.Add(certificate.NotAfter.ToString("dd.MM.yyyy"));
                item.SubItems.Add(certificate.HasPrivateKey ? "Доступен" : "Нет");
                item.SubItems.Add(certificate.Thumbprint);
                _removableCertificates.Items.Add(item);
            }
        }
        finally
        {
            _removableCertificates.EndUpdate();
        }

        var valid = certificates.Count(certificate => certificate.IsValidNow);
        var expired = certificates.Count(certificate => certificate.IsExpired);
        var notYetValid = certificates.Count(certificate => certificate.IsNotYetValid);
        _removableCertificateStatus.Text =
            $"Всего: {certificates.Count}; действующих: {valid}; истёкших: {expired}; ещё не действуют: {notYetValid}.";
    }

    private async Task RemoveSelectedCertificatesAsync()
    {
        if (_busy) return;
        var selected = _removableCertificates.CheckedItems
            .Cast<ListViewItem>()
            .Select(item => (UserPersonalCertificateItem)item.Tag!)
            .ToArray();
        if (selected.Length == 0)
        {
            ShowError("Отметьте хотя бы один сертификат для удаления.");
            return;
        }

        var preview = string.Join("\r\n", selected.Take(6).Select(certificate => $"• {certificate.DisplayName} — {certificate.Status}"));
        if (selected.Length > 6) preview += $"\r\n• и ещё {selected.Length - 6}";
        var answer = MessageBox.Show(this,
            $"Вы уверены, что хотите удалить выбранные сертификаты ({selected.Length}) из личного хранилища текущего пользователя?\r\n\r\n{preview}\r\n\r\nЗакрытые ключи и контейнеры CryptoPro не удаляются.",
            "Подтверждение удаления сертификатов",
            MessageBoxButtons.YesNo,
            MessageBoxIcon.Warning,
            MessageBoxDefaultButton.Button2);
        if (answer != DialogResult.Yes) return;

        await RunBusyAsync(() =>
        {
            var removed = 0;
            foreach (var certificate in selected)
            {
                removed += _cryptoPro.RemoveCurrentUserPersonalCertificate(certificate.Thumbprint);
                Log($"Удалён сертификат из CurrentUser\\My: {certificate.DisplayName}, {certificate.Thumbprint}");
            }

            RefreshCertificates();
            RefreshRemovableCertificates();
            SetStatus($"Удалено сертификатов: {removed}", Color.FromArgb(23, 121, 78));
            MessageBox.Show(this,
                $"Удалено сертификатов: {removed}.\r\n\r\nЗакрытые ключи и контейнеры CryptoPro не изменялись.",
                "CryptoSigTool", MessageBoxButtons.OK, MessageBoxIcon.Information);
            return Task.CompletedTask;
        });
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
        var selectedPdfThumbprint = (_pdfCertificates.SelectedItem as CertificateItem)?.Thumbprint;
        _certificates.Items.Clear();
        _pdfCertificates.Items.Clear();
        var certificates = _cryptoPro.GetCertificates();
        foreach (var cert in certificates)
        {
            _certificates.Items.Add(cert);
            _pdfCertificates.Items.Add(cert);
        }
        if (_pdfCertificates.Items.Count > 0)
        {
            var selectedIndex = certificates.FindIndex(x => string.Equals(x.Thumbprint, selectedPdfThumbprint, StringComparison.OrdinalIgnoreCase));
            _pdfCertificates.SelectedIndex = selectedIndex >= 0 ? selectedIndex : 0;
        }
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
        _refreshRemovableCertificatesButton.Enabled = enabled;
        _removeCertificatesButton.Enabled = enabled;
        _mergeButton.Enabled = enabled;
        _signButton.Enabled = enabled;
        _verifyButton.Enabled = enabled;
        _pdfOpenButton.Enabled = enabled;
        _pdfSignButton.Enabled = enabled;
        _pdfZoomInButton.Enabled = enabled;
        _pdfZoomOutButton.Enabled = enabled;
        _pdfFitButton.Enabled = enabled;
        _pdfPreviousButton.Enabled = enabled && _pdfViewer.PageCount > 0 && _pdfPageIndex > 0;
        _pdfNextButton.Enabled = enabled && _pdfViewer.PageCount > 0 && _pdfPageIndex + 1 < _pdfViewer.PageCount;
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

    private static Button DangerButton(string text) => new()
    {
        Text = text,
        AutoSize = true,
        FlatStyle = FlatStyle.Flat,
        BackColor = Color.Firebrick,
        ForeColor = Color.White,
        Padding = new Padding(12, 5, 12, 5),
        Margin = new Padding(3, 3, 3, 3),
        Cursor = Cursors.Hand
    };

    private const string SignatureFilter = "Подписи (*.sig;*.p7s)|*.sig;*.p7s|Все файлы|*.*";
}
