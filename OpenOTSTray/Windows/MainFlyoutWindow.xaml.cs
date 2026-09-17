using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using Color = System.Windows.Media.Color;
using OpenOTSTray.Models;
using OpenOTSTray.Services;

namespace OpenOTSTray.Windows;

public partial class MainFlyoutWindow : Window
{
    private enum ViewMode { History, Generate, Quick, Settings }

    private const int HistoryPageSize = 5;
    private const int HistoryMaxItems = 25;

    private static readonly SolidColorBrush ActiveTabBrush = new(Color.FromRgb(0x25, 0x63, 0xEB));
    private static readonly SolidColorBrush InactiveTabBrush = new(Color.FromRgb(0x6B, 0x72, 0x80));

    private readonly SettingsService _settingsService;
    private readonly HistoryStorageService _historyStorage = new();
    private readonly OneTimeSecretClient _client = new();
    private readonly List<PasswordResultItem> _history = new();
    private readonly ObservableCollection<PasswordResultItem> _historyPage = new();
    private AppSettings _settings;
    private int _historyCurrentPage;

    public MainFlyoutWindow(SettingsService settingsService)
    {
        InitializeComponent();
        _settingsService = settingsService;
        _settings = settingsService.Load();

        HistoryList.ItemsSource = _historyPage;
        SetRegionComboBox.ItemsSource = AppSettings.SupportedRegions;

        LoadPersistedHistory();

        Loaded += (_, _) => RepositionNearTray();
        SizeChanged += (_, _) => RepositionNearTray();

        ShowView(ViewMode.Quick);
    }

    /// <summary>
    /// Restores prior-session history: metadata in the clear, the link decrypted via
    /// DPAPI (empty if it can't be — moved to another machine/user, or predates this
    /// field). Password and Passphrase are never persisted, so they come back empty by
    /// design; the email body is rebuilt from the current template using only the link.
    /// </summary>
    private void LoadPersistedHistory()
    {
        var restored = _historyStorage.Load();
        foreach (var item in restored)
        {
            item.EmailBody = string.IsNullOrEmpty(item.Link)
                ? ""
                : EmailTemplateBuilder.BuildBody(_settings.EmailBodyTemplate, item.Link, null, null);
        }
        _history.AddRange(restored);
    }

    public void ToggleVisibility()
    {
        if (IsVisible)
        {
            Hide();
            return;
        }

        ShowView(ViewMode.Quick);
        Show();
        RepositionNearTray();
        Activate();

        _ = RefreshReceiptStatusesAsync();
    }

    /// <summary>
    /// Best-effort check, run every time the flyout opens, for whether any not-yet-opened
    /// secrets in the history have since been viewed/burned (flips the lock icon to open).
    /// </summary>
    private async Task RefreshReceiptStatusesAsync()
    {
        var pending = _history.Where(i => i.Succeeded && !i.IsOpened && !string.IsNullOrEmpty(i.ReceiptIdentifier)).ToList();
        if (pending.Count == 0)
            return;

        var anyChanged = false;
        foreach (var item in pending)
        {
            try
            {
                var state = await _client.GetReceiptStateAsync(item.Region, item.ReceiptIdentifier);
                if (state is "revealed" or "burned")
                {
                    item.IsOpened = true;
                    item.Icon = "\U0001F513";
                    anyChanged = true;
                }
            }
            catch
            {
                // best-effort refresh; leave this item's lock icon as-is and try again next time
            }
        }

        if (anyChanged)
        {
            _historyStorage.Save(_history);
            if (HistoryView.Visibility == Visibility.Visible)
                ShowHistoryPage(_historyCurrentPage);
        }
    }

    private void RepositionNearTray()
    {
        if (ActualWidth <= 0 || ActualHeight <= 0)
            return;

        var workingArea = System.Windows.Forms.Screen.PrimaryScreen!.WorkingArea;
        var source = PresentationSource.FromVisual(this);
        double dpiX = 1, dpiY = 1;
        if (source?.CompositionTarget != null)
        {
            dpiX = source.CompositionTarget.TransformToDevice.M11;
            dpiY = source.CompositionTarget.TransformToDevice.M22;
        }

        const double margin = 8;
        Left = (workingArea.Right / dpiX) - ActualWidth - margin;
        Top = (workingArea.Bottom / dpiY) - ActualHeight - margin;
    }

    private void Window_Deactivated(object sender, EventArgs e)
    {
        if (IsVisible)
            Hide();
    }

    private void Window_PreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (GearDropdown.Visibility != Visibility.Visible)
            return;

        if (e.OriginalSource is DependencyObject source &&
            (IsDescendantOf(source, GearDropdown) || IsDescendantOf(source, GearButton)))
            return;

        GearDropdown.Visibility = Visibility.Collapsed;
    }

    private static bool IsDescendantOf(DependencyObject child, DependencyObject ancestor)
    {
        var current = child;
        while (current != null)
        {
            if (ReferenceEquals(current, ancestor))
                return true;
            current = VisualTreeHelper.GetParent(current);
        }
        return false;
    }

    private void GearButton_Click(object sender, RoutedEventArgs e)
    {
        GearDropdown.Visibility = GearDropdown.Visibility == Visibility.Visible ? Visibility.Collapsed : Visibility.Visible;
    }

    private void SettingsMenuItem_Click(object sender, RoutedEventArgs e) => ShowView(ViewMode.Settings);

    private void AboutMenuItem_Click(object sender, RoutedEventArgs e)
    {
        GearDropdown.Visibility = Visibility.Collapsed;
        Hide();
        new AboutWindow().ShowDialog();
    }

    private void ExitMenuItem_Click(object sender, RoutedEventArgs e) => Application.Current.Shutdown();

    private void ShowHistoryView_Click(object sender, RoutedEventArgs e) => ShowView(ViewMode.History);
    private void ShowGenerateView_Click(object sender, RoutedEventArgs e) => ShowView(ViewMode.Generate);
    private void ShowQuickView_Click(object sender, RoutedEventArgs e) => ShowView(ViewMode.Quick);

    private void ShowView(ViewMode mode)
    {
        GearDropdown.Visibility = Visibility.Collapsed;

        HistoryView.Visibility = mode == ViewMode.History ? Visibility.Visible : Visibility.Collapsed;
        GenerateView.Visibility = mode == ViewMode.Generate ? Visibility.Visible : Visibility.Collapsed;
        QuickView.Visibility = mode == ViewMode.Quick ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = mode == ViewMode.Settings ? Visibility.Visible : Visibility.Collapsed;

        SetActiveTab(mode);

        switch (mode)
        {
            case ViewMode.History:
                ShowHistoryPage(_historyCurrentPage);
                break;
            case ViewMode.Generate:
                GenDefaultsText.Text = $"Password length: {_settings.PasswordLength} · Region: {_settings.Region}";
                GenerateErrorText.Visibility = Visibility.Collapsed;
                break;
            case ViewMode.Quick:
                QuickErrorText.Visibility = Visibility.Collapsed;
                Dispatcher.BeginInvoke(new Action(() => QuickSecretTextBox.Focus()), System.Windows.Threading.DispatcherPriority.Input);
                break;
            case ViewMode.Settings:
                SetPasswordLengthTextBox.Text = _settings.PasswordLength.ToString();
                SetRegionComboBox.SelectedItem = _settings.Region;
                SetStartWithWindowsCheckBox.IsChecked = _settings.StartWithWindows;
                SetEmailSubjectTextBox.Text = _settings.EmailSubject;
                SetEmailBodyTextBox.Text = _settings.EmailBodyTemplate;
                SettingsErrorText.Visibility = Visibility.Collapsed;
                break;
        }
    }

    private void SetActiveTab(ViewMode mode)
    {
        ResetTab(LatestTabButton);
        ResetTab(GenerateTabButton);
        ResetTab(QuickTabButton);

        var active = mode switch
        {
            ViewMode.History => LatestTabButton,
            ViewMode.Generate => GenerateTabButton,
            ViewMode.Quick => QuickTabButton,
            _ => null,
        };
        if (active != null)
        {
            active.Foreground = ActiveTabBrush;
            active.FontWeight = FontWeights.Bold;
        }
    }

    private static void ResetTab(Button button)
    {
        button.Foreground = InactiveTabBrush;
        button.FontWeight = FontWeights.Normal;
    }

    private int HistoryTotalPages => Math.Max(1, (int)Math.Ceiling(_history.Count / (double)HistoryPageSize));

    private void AddToHistory(PasswordResultItem item)
    {
        _history.Insert(0, item);
        if (_history.Count > HistoryMaxItems)
            _history.RemoveAt(_history.Count - 1);

        _historyStorage.Save(_history);
    }

    private void ShowHistoryPage(int page)
    {
        _historyCurrentPage = Math.Clamp(page, 0, HistoryTotalPages - 1);

        _historyPage.Clear();
        foreach (var item in _history.Skip(_historyCurrentPage * HistoryPageSize).Take(HistoryPageSize))
            _historyPage.Add(item);

        EmptyHistoryText.Visibility = _history.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPaginationPanel.Visibility = HistoryTotalPages > 1 ? Visibility.Visible : Visibility.Collapsed;
        HistoryPageIndicatorText.Text = $"Page {_historyCurrentPage + 1} of {HistoryTotalPages}";
        HistoryPrevPageButton.IsEnabled = _historyCurrentPage > 0;
        HistoryNextPageButton.IsEnabled = _historyCurrentPage < HistoryTotalPages - 1;
    }

    private void HistoryPrevPage_Click(object sender, RoutedEventArgs e) => ShowHistoryPage(_historyCurrentPage - 1);
    private void HistoryNextPage_Click(object sender, RoutedEventArgs e) => ShowHistoryPage(_historyCurrentPage + 1);

    private void EditTitle_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PasswordResultItem item })
            return;

        item.TitleDisplayVisibility = Visibility.Collapsed;
        item.TitleEditVisibility = Visibility.Visible;
        ShowHistoryPage(_historyCurrentPage);
    }

    private void TitleEditBox_IsVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox { Visibility: Visibility.Visible } textBox)
            return;

        textBox.Focus();
        textBox.SelectAll();
    }

    private void TitleEditBox_KeyDown(object sender, KeyEventArgs e)
    {
        if (sender is not TextBox textBox)
            return;

        if (e.Key == Key.Enter)
            CommitTitleEdit(textBox);
        else if (e.Key == Key.Escape)
            CancelTitleEdit(textBox);
    }

    private void TitleEditBox_LostFocus(object sender, RoutedEventArgs e)
    {
        if (sender is TextBox textBox)
            CommitTitleEdit(textBox);
    }

    private void CommitTitleEdit(TextBox textBox)
    {
        if (textBox.Tag is not PasswordResultItem item)
            return;

        var newTitle = textBox.Text.Trim();
        if (!string.IsNullOrEmpty(newTitle))
        {
            item.TitleLabel = newTitle;
            _historyStorage.Save(_history);
        }

        item.TitleDisplayVisibility = Visibility.Visible;
        item.TitleEditVisibility = Visibility.Collapsed;
        ShowHistoryPage(_historyCurrentPage);
    }

    private void CancelTitleEdit(TextBox textBox)
    {
        if (textBox.Tag is not PasswordResultItem item)
            return;

        item.TitleDisplayVisibility = Visibility.Visible;
        item.TitleEditVisibility = Visibility.Collapsed;
        ShowHistoryPage(_historyCurrentPage);
    }

    private static readonly SolidColorBrush CopiedBackground = new(Color.FromRgb(0xDC, 0xFC, 0xE7));
    private static readonly SolidColorBrush CopiedForeground = new(Color.FromRgb(0x15, 0x80, 0x3D));
    private static readonly TimeSpan ClipboardClearDelay = TimeSpan.FromSeconds(45);

    private void CopyPassword_Click(object sender, RoutedEventArgs e) => CopyTagToClipboard(sender);
    private void CopyLink_Click(object sender, RoutedEventArgs e) => CopyTagToClipboard(sender);
    private void CopyPassphrase_Click(object sender, RoutedEventArgs e) => CopyTagToClipboard(sender);

    private static async void CopyTagToClipboard(object sender)
    {
        if (sender is not Button { Tag: string text } button || string.IsNullOrEmpty(text))
            return;

        SetClipboardTextSecurely(text);

        var originalContent = button.Content;
        var originalBackground = button.Background;
        var originalForeground = button.Foreground;

        button.Content = "Copied!";
        button.Background = CopiedBackground;
        button.Foreground = CopiedForeground;

        await Task.Delay(1200);

        button.Content = originalContent;
        button.Background = originalBackground;
        button.Foreground = originalForeground;

        _ = ClearClipboardAfterDelayAsync(text);
    }

    /// <summary>
    /// Copies text while opting out of Windows Clipboard History and Cloud Clipboard sync,
    /// so a copied password/link/passphrase doesn't linger in Win+V history or silently
    /// roam to the user's other devices via the Microsoft account clipboard sync.
    /// </summary>
    private static void SetClipboardTextSecurely(string text)
    {
        var data = new DataObject();
        data.SetData(DataFormats.UnicodeText, text);
        data.SetData("CanIncludeInClipboardHistory", false);
        data.SetData("CanUploadToCloudClipboard", false);
        Clipboard.SetDataObject(data, true);
    }

    /// <summary>
    /// Best-effort auto-clear so a copied secret doesn't sit in the clipboard indefinitely.
    /// Only clears if the clipboard still holds exactly what we put there (i.e. the user
    /// hasn't copied something else over it in the meantime).
    /// </summary>
    private static async Task ClearClipboardAfterDelayAsync(string expectedText)
    {
        await Task.Delay(ClipboardClearDelay);
        try
        {
            if (Clipboard.ContainsText() && Clipboard.GetText() == expectedText)
                Clipboard.Clear();
        }
        catch
        {
            // the clipboard can be transiently locked by another process; best-effort only
        }
    }

    private void SendEmail_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: PasswordResultItem item } || !item.Succeeded)
            return;

        var subject = Uri.EscapeDataString(_settings.EmailSubject);
        var body = Uri.EscapeDataString(item.EmailBody);
        var mailto = $"mailto:?subject={subject}&body={body}";

        try
        {
            Process.Start(new ProcessStartInfo(mailto) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open your email client: {ex.Message}", "Open OTS Tray",
                MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void GenerateSubmit_Click(object sender, RoutedEventArgs e)
    {
        GenerateErrorText.Visibility = Visibility.Collapsed;

        var quantityText = GenQuantityTextBox.Text.Trim();
        if (string.IsNullOrEmpty(quantityText))
            quantityText = "1";
        if (!int.TryParse(quantityText, out int quantity) || quantity < 1 || quantity > 100)
        {
            ShowGenerateError("Quantity must be a number between 1 and 100.");
            return;
        }

        var length = _settings.PasswordLength;

        var ttlText = GenTtlTextBox.Text.Trim();
        if (string.IsNullOrEmpty(ttlText))
            ttlText = "7";
        if (!int.TryParse(ttlText, out int ttlDays) || ttlDays < 1)
        {
            ShowGenerateError("TTL must be a positive number of days.");
            return;
        }
        var ttl = ttlDays * 86400;
        var region = _settings.Region;

        var passphrase = string.IsNullOrWhiteSpace(GenPassphraseTextBox.Text) ? null : GenPassphraseTextBox.Text;

        GenerateSubmitButton.IsEnabled = false;
        GenerateLoadingPanel.Visibility = Visibility.Visible;

        for (int i = 0; i < quantity; i++)
        {
            GenerateLoadingText.Text = $"Generating password {i + 1} of {quantity}...";

            var password = PasswordGenerator.Generate(length);
            var item = new PasswordResultItem
            {
                Kind = "Password",
                TitleLabel = $"Password {PreviewText(password)}",
                Password = password,
                Passphrase = passphrase ?? "",
                Region = region,
            };

            try
            {
                var created = await _client.CreateSecretLinkAsync(password, region, passphrase, ttl);
                item.Link = created.Link;
                item.ReceiptIdentifier = created.ReceiptIdentifier;
                item.EmailBody = EmailTemplateBuilder.BuildBody(_settings.EmailBodyTemplate, item.Link, password, passphrase);
                item.Subtitle = $"{region} · expires in {TtlFormatter.Format(ttl)}";
            }
            catch (Exception ex)
            {
                item.ErrorMessage = ex.Message;
                item.ErrorVisibility = Visibility.Visible;
                item.Subtitle = "Failed to generate link";
            }

            AddToHistory(item);
        }

        GenerateLoadingPanel.Visibility = Visibility.Collapsed;
        GenerateSubmitButton.IsEnabled = true;
        GenQuantityTextBox.Text = "1";
        GenPassphraseTextBox.Text = "";

        ShowView(ViewMode.History);
    }

    private void ShowGenerateError(string message)
    {
        GenerateErrorText.Text = message;
        GenerateErrorText.Visibility = Visibility.Visible;
    }

    private void GenEditDefaults_Click(object sender, RoutedEventArgs e) => ShowView(ViewMode.Settings);

    private async void QuickSubmit_Click(object sender, RoutedEventArgs e)
    {
        QuickErrorText.Visibility = Visibility.Collapsed;

        var secret = QuickSecretTextBox.Text;
        if (string.IsNullOrWhiteSpace(secret))
        {
            QuickErrorText.Text = "Please enter some content to share.";
            QuickErrorText.Visibility = Visibility.Visible;
            return;
        }

        QuickSubmitButton.IsEnabled = false;
        QuickLoadingPanel.Visibility = Visibility.Visible;

        var item = new PasswordResultItem
        {
            Kind = "Quick Secret",
            TitleLabel = $"Quick Secret {PreviewText(secret)}",
            Region = _settings.Region,
        };

        try
        {
            var created = await _client.CreateSecretLinkAsync(secret, _settings.Region);
            item.Link = created.Link;
            item.ReceiptIdentifier = created.ReceiptIdentifier;
            item.EmailBody = EmailTemplateBuilder.BuildBody(_settings.EmailBodyTemplate, item.Link, null, null);
            item.Subtitle = $"{_settings.Region} · expires in {TtlFormatter.Format(604800)}";
        }
        catch (Exception ex)
        {
            item.ErrorMessage = ex.Message;
            item.ErrorVisibility = Visibility.Visible;
            item.Subtitle = "Failed to generate link";
        }

        AddToHistory(item);

        QuickLoadingPanel.Visibility = Visibility.Collapsed;
        QuickSubmitButton.IsEnabled = true;
        QuickSecretTextBox.Text = "";

        ShowView(ViewMode.History);
    }

    private static string PreviewText(string value, int length = 6)
    {
        var singleLine = value.Replace("\r", "").Replace("\n", " ").Trim();
        if (string.IsNullOrEmpty(singleLine))
            return "";
        return singleLine.Length <= length ? singleLine : singleLine[..length] + "…";
    }

    private void SettingsSave_Click(object sender, RoutedEventArgs e)
    {
        SettingsErrorText.Visibility = Visibility.Collapsed;

        if (!int.TryParse(SetPasswordLengthTextBox.Text.Trim(), out int length) ||
            length < AppSettings.MinPasswordLength || length > AppSettings.MaxPasswordLength)
        {
            ShowSettingsError($"Password length must be a number between {AppSettings.MinPasswordLength} and {AppSettings.MaxPasswordLength}.");
            return;
        }

        if (SetRegionComboBox.SelectedItem is not string region)
        {
            ShowSettingsError("Please select a region.");
            return;
        }

        if (string.IsNullOrWhiteSpace(SetEmailSubjectTextBox.Text))
        {
            ShowSettingsError("Email subject cannot be empty.");
            return;
        }

        var emailBodyTemplate = SetEmailBodyTextBox.Text;
        if (!EmailTemplateBuilder.HasRequiredTokens(emailBodyTemplate))
        {
            ShowSettingsError("Email body template must include the {{link}} token.");
            return;
        }

        var updated = new AppSettings
        {
            PasswordLength = length,
            Region = region,
            StartWithWindows = SetStartWithWindowsCheckBox.IsChecked == true,
            EmailSubject = SetEmailSubjectTextBox.Text.Trim(),
            EmailBodyTemplate = emailBodyTemplate,
        };

        try
        {
            _settingsService.Save(updated);
            _settingsService.SetStartWithWindows(updated.StartWithWindows);
        }
        catch (Exception ex)
        {
            ShowSettingsError($"Failed to save settings: {ex.Message}");
            return;
        }

        _settings = updated;
        ShowView(ViewMode.History);
    }

    private void SettingsCancel_Click(object sender, RoutedEventArgs e) => ShowView(ViewMode.History);

    private void ResetTemplate_Click(object sender, RoutedEventArgs e)
    {
        SetEmailSubjectTextBox.Text = AppSettings.DefaultEmailSubject;
        SetEmailBodyTextBox.Text = AppSettings.DefaultEmailBodyTemplate;
    }

    private void ShowSettingsError(string message)
    {
        SettingsErrorText.Text = message;
        SettingsErrorText.Visibility = Visibility.Visible;
    }

    private void ClearHistory_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "This permanently deletes the saved link history (titles, status, and links) from this computer. " +
            "Passwords and passphrases were never stored, so this only affects what you see in \"Latest Link\".\n\n" +
            "This cannot be undone. Continue?",
            "Clear saved link history",
            MessageBoxButton.YesNo,
            MessageBoxImage.Warning,
            MessageBoxResult.No);

        if (result != MessageBoxResult.Yes)
            return;

        _history.Clear();
        _historyStorage.Clear();
        ShowView(ViewMode.History);
    }
}
