using System;
using System.Globalization;
using System.IO.Ports;
using System.Net;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TERMINAL.Notifications;

namespace TERMINAL.Windows
{
    public partial class DeviceSettingsWindow : Window
    {
        private int _lastSelectedProvider = 0;
        private bool _isLoadingData = false;

        public DeviceSettingsWindow(int initialTab = 0,
                                     bool q2viewEnabled = false,
                                     bool timerEnabled = true,
                                     int timerSeconds = 25,
                                     bool autoExcelEnabled = true,
                                     bool tickTimingEnabled = true,
                                     bool logSlowTicksEnabled = true,
                                     bool autoStopFurnace = false,
                                     string platinumSource = "mtk_termex",
                                     string platinumComPort = "COM1",
                                     bool readPlatinum = true,
                                     string platinumMtkCommand = "pass2 meas2? 2")
        {
            InitializeComponent();

            // Подписываемся на PreviewMouseLeftButtonDown для всех чекбоксов
            foreach (var cb in new[] { ChkSendToTelegram, ChkSendToYandex, ChkSendToMail, ChkSendToGmail, ChkSendToCustomEmail, ChkSendToVk, ChkSendToMax })
            {
                cb.PreviewMouseLeftButtonDown += (s, e) =>
                {
                    // Не даём ComboBoxItem перехватить клик и закрыть список
                    e.Handled = true;

                    // Переключаем галочку вручную
                    if (s is CheckBox checkbox)
                    {
                        checkbox.IsChecked = !(checkbox.IsChecked == true);
                    }
                };
            }

            // ─── Общие настройки ───
            GeneralQ2ViewCheck.IsChecked = q2viewEnabled;
            GeneralTimerBox.Text = timerSeconds.ToString(CultureInfo.InvariantCulture);
            GeneralTimerBox.IsEnabled = q2viewEnabled;
            GeneralAutoExcelCheck.IsChecked = autoExcelEnabled;
            GeneralTickTimingCheck.IsChecked = tickTimingEnabled;
            GeneralLogSlowTicksCheck.IsChecked = logSlowTicksEnabled;
            AutoStopFurnaceCheck.IsChecked = autoStopFurnace;

            GeneralQ2ViewCheck.Checked += (s, e) => GeneralTimerBox.IsEnabled = true;
            GeneralQ2ViewCheck.Unchecked += (s, e) => GeneralTimerBox.IsEnabled = false;

            GeneralTimerBox.PreviewTextInput += (s, e) =>
            {
                foreach (char c in e.Text)
                    if (!char.IsDigit(c)) { e.Handled = true; return; }
            };

            // ─── Вкладка по умолчанию ───
            if (initialTab < 0 || initialTab >= DeviceTabs.Items.Count)
                initialTab = 0;
            DeviceTabs.SelectedIndex = initialTab;

            PlatinumMtkCommandBox.Text = platinumMtkCommand;

            LoadCurrent();

            // ★ Применяем текущую тему из MainWindow
            ApplyThemeFromMainWindow();

            // ★ Подписываемся на смену темы в MainWindow
            Loaded += (s, e) =>
            {
                if (MainWindow.Current != null)
                    MainWindow.Current.ThemeChanged += OnMainThemeChanged;
            };
        }

        // ═══════════════════════════════════════════════════════════════════
        // Тема
        // ═══════════════════════════════════════════════════════════════════

        private void OnMainThemeChanged(bool isDark)
        {
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.Invoke(() => ApplyTheme(isDark));
                return;
            }
            ApplyTheme(isDark);
        }

        private void ApplyThemeFromMainWindow()
        {
            bool isDark = MainWindow.Current?.IsDarkTheme ?? false;
            ApplyTheme(isDark);
        }

        public void ApplyTheme(bool isDark)
        {
            var themeUri = new Uri(
                isDark ? "/TERMINAL;component/Themes/DarkTheme.xaml" : "/TERMINAL;component/Themes/LightTheme.xaml",
                UriKind.Relative);
            var themeDict = new ResourceDictionary { Source = themeUri };

            if (Resources.MergedDictionaries.Count > 0)
                Resources.MergedDictionaries[0] = themeDict;
            else
                Resources.MergedDictionaries.Add(themeDict);
        }

        protected override void OnClosed(EventArgs e)
        {
            if (MainWindow.Current != null)
                MainWindow.Current.ThemeChanged -= OnMainThemeChanged;
            base.OnClosed(e);
        }

        // ═══════════════════════════════════════════════════════════════════
        // Загрузка текущих настроек
        // ═══════════════════════════════════════════════════════════════════

        private void LoadCurrent()
        {
            var mw = MainWindow.Current;
            if (mw == null) return;

            // ST-351
            FurnaceIpBox.Text = mw.FurnaceIp;
            FurnacePortBox.Text = mw.FurnacePort.ToString();
            FurnaceSetPointRegBox.Text = mw.FurnaceSetPointRegister.ToString();
            FurnaceCurrentTempRegBox.Text = mw.FurnaceCurrentTempRegister.ToString();
            FurnaceModeRegBox.Text = mw.FurnaceModeRegister.ToString();
            FurnaceStabilizationToleranceBox.Text =
                mw.FurnaceStabilizationTolerance.ToString(CultureInfo.InvariantCulture);
            AutoStopFurnaceCheck.IsChecked = mw.AutoStopFurnace;
            ShowFurnaceCardCheck.IsChecked = mw.ShowFurnaceCard;

            // АГК
            AgkIpBox.Text = mw.DeviceIp;
            AgkPortBox.Text = mw.DevicePort.ToString();
            AgkPressureTimeoutBox.Text = mw.AgkPressureTimeoutMinutes.ToString();
            ShowAgkCardCheck.IsChecked = mw.ShowAgkCard;

            // СКТБ
            SktbFilterScriptCombo.SelectedIndex = mw.SktbFilterScript switch
            {
                "Фильтр_данных_ПДС" => 0,
                "Фильтр_данных_БИДТ" => 1,
                "Фильтр_РЧ" => 2,
                "Filter_for_KOSA_GUI" => 3,
                _ => 3
            };
            ShowSktbCardCheck.IsChecked = mw.ShowSktbCard;

            // Платина
            PlatinumSourceCombo.SelectedIndex = mw.PlatinumSource switch
            {
                "mit_803" => 1,
                "tercon" => 2,
                _ => 0
            };
            FillComPorts(mw.PlatinumComPort);
            ReadPlatinumCheck.IsChecked = mw.ReadPlatinum;
            PlatinumMtkCommandBox.Text = mw.PlatinumMtkCommand;
            PlatinumSourceCombo_SelectionChanged(null, null);

            // ═══════════════════════════════════════════════════════════════════
            // УВЕДОМЛЕНИЯ
            // ═══════════════════════════════════════════════════════════════════
            NotificationsEnabledCheck.IsChecked = mw.NotificationsEnabled;
            NotificationMethodCombo.SelectedIndex = mw.NotificationMethodIndex;
            NotificationMethodCombo_SelectionChanged(null, null);

            ChkSendToTelegram.IsChecked = mw.NotifyTgEnabled;
            ChkSendToYandex.IsChecked = mw.NotifyYandexEnabled;
            ChkSendToMail.IsChecked = mw.NotifyMailEnabled;
            ChkSendToGmail.IsChecked = mw.NotifyGmailEnabled;
            ChkSendToCustomEmail.IsChecked = mw.NotifyCustomEnabled;
            ChkSendToVk.IsChecked = mw.NotifyVkEnabled;
            ChkSendToMax.IsChecked = mw.NotifyMaxEnabled;

            // Telegram
            TelegramTokenBox.Text = mw.TelegramToken;
            TelegramChatIdBox.Text = mw.TelegramChatId;

            // ─── Уведомления: Email ───
            _isLoadingData = true;
            EmailProviderCombo.SelectedIndex = mw.EmailProviderIndex;
            _lastSelectedProvider = mw.EmailProviderIndex;
            _isLoadingData = false;

            switch (mw.EmailProviderIndex)
            {
                case 0:
                    EmailHostBox.Text = "smtp.yandex.ru";
                    EmailPortBox.Text = "587";
                    break;
                case 1:
                    EmailHostBox.Text = "smtp.mail.ru";
                    EmailPortBox.Text = "587";
                    break;
                case 2:
                    EmailHostBox.Text = "smtp.gmail.com";
                    EmailPortBox.Text = "587";
                    break;
                case 3:
                    EmailHostBox.Text = mw.EmailCustomHost;
                    EmailPortBox.Text = mw.EmailCustomPort.ToString();
                    break;
            }

            switch (mw.EmailProviderIndex)
            {
                case 0:
                    EmailUserBox.Text = mw.EmailYandexUser;
                    EmailPassBox.Password = mw.EmailYandexPass;
                    EmailToBox.Text = mw.EmailYandexTo;
                    break;
                case 1:
                    EmailUserBox.Text = mw.EmailMailUser;
                    EmailPassBox.Password = mw.EmailMailPass;
                    EmailToBox.Text = mw.EmailMailTo;
                    break;
                case 2:
                    EmailUserBox.Text = mw.EmailGmailUser;
                    EmailPassBox.Password = mw.EmailGmailPass;
                    EmailToBox.Text = mw.EmailGmailTo;
                    break;
                case 3:
                    EmailUserBox.Text = mw.EmailCustomUser;
                    EmailPassBox.Password = mw.EmailCustomPass;
                    EmailToBox.Text = mw.EmailCustomTo;
                    break;
            }

            // VK
            VkTokenBox.Text = mw.VkToken;
            VkPeerIdBox.Text = mw.VkPeerId.ToString();

            // MAX
            MaxTokenBox.Text = mw.MaxToken;
            MaxChatIdBox.Text = mw.MaxChatId;

            // SMS
            SmsLoginBox.Text = mw.SmsLogin;
            SmsPassBox.Password = mw.SmsPass;
            SmsPhoneBox.Text = mw.SmsPhone;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Сброс на значения по умолчанию
        // ═══════════════════════════════════════════════════════════════════

        private void Default_Click(object sender, RoutedEventArgs e)
        {
            FurnaceIpBox.Text = "192.168.0.167";
            FurnacePortBox.Text = "502";
            FurnaceSetPointRegBox.Text = "201";
            FurnaceCurrentTempRegBox.Text = "205";
            FurnaceModeRegBox.Text = "200";
            FurnaceStabilizationToleranceBox.Text = "0.1";
            AutoStopFurnaceCheck.IsChecked = false;
            ShowFurnaceCardCheck.IsChecked = true;

            AgkIpBox.Text = "192.168.0.159";
            AgkPortBox.Text = "8888";
            AgkPressureTimeoutBox.Text = "5";
            ShowAgkCardCheck.IsChecked = true;

            SktbFilterScriptCombo.SelectedIndex = 3;
            ShowSktbCardCheck.IsChecked = true;

            GeneralQ2ViewCheck.IsChecked = false;
            GeneralTimerBox.Text = "25";
            GeneralTimerBox.IsEnabled = false;
            GeneralAutoExcelCheck.IsChecked = true;
            GeneralTickTimingCheck.IsChecked = true;
            GeneralLogSlowTicksCheck.IsChecked = true;

            NotificationsEnabledCheck.IsChecked = false;
            TelegramTokenBox.Text = "";
            TelegramChatIdBox.Text = "";
            NotificationMethodCombo.SelectedIndex = 0;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Сохранение
        // ═══════════════════════════════════════════════════════════════════

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var mw = MainWindow.Current;
            if (mw == null)
            {
                DialogResult = false;
                Close();
                return;
            }

            try
            {
                string furnaceIp = FurnaceIpBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(furnaceIp))
                    furnaceIp = "192.168.0.167";

                int furnacePort = 502;
                if (!int.TryParse(FurnacePortBox.Text, out furnacePort) || furnacePort <= 0 || furnacePort > 65535)
                    furnacePort = 502;

                ushort furnaceSetPointReg = 201;
                if (!ushort.TryParse(FurnaceSetPointRegBox.Text, out furnaceSetPointReg))
                    furnaceSetPointReg = 201;

                ushort furnaceCurrentTempReg = 205;
                if (!ushort.TryParse(FurnaceCurrentTempRegBox.Text, out furnaceCurrentTempReg))
                    furnaceCurrentTempReg = 205;

                ushort furnaceModeReg = 200;
                if (!ushort.TryParse(FurnaceModeRegBox.Text, out furnaceModeReg))
                    furnaceModeReg = 200;

                double furnaceStabilizationTolerance = 0.1;
                if (!double.TryParse(FurnaceStabilizationToleranceBox.Text.Replace(',', '.'),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out furnaceStabilizationTolerance)
                    || furnaceStabilizationTolerance <= 0)
                {
                    furnaceStabilizationTolerance = 0.1;
                }

                bool autoStopFurnace = AutoStopFurnaceCheck.IsChecked == true;
                bool showFurnaceCard = ShowFurnaceCardCheck.IsChecked == true;

                string agkIp = AgkIpBox.Text.Trim();
                if (string.IsNullOrWhiteSpace(agkIp))
                    agkIp = "192.168.0.159";

                int agkPort = 8888;
                if (!int.TryParse(AgkPortBox.Text, out agkPort) || agkPort <= 0 || agkPort > 65535)
                    agkPort = 8888;

                int agkPressureTimeoutMinutes = 5;
                if (!int.TryParse(AgkPressureTimeoutBox.Text, out agkPressureTimeoutMinutes) ||
                    agkPressureTimeoutMinutes <= 0)
                    agkPressureTimeoutMinutes = 5;

                bool showAgkCard = ShowAgkCardCheck.IsChecked == true;

                string sktbFilterScript = "Filter_for_KOSA_GUI";
                switch (SktbFilterScriptCombo.SelectedIndex)
                {
                    case 0: sktbFilterScript = "Фильтр_данных_ПДС"; break;
                    case 1: sktbFilterScript = "Фильтр_данных_БИДТ"; break;
                    case 2: sktbFilterScript = "Фильтр_РЧ"; break;
                    case 3:
                    default: sktbFilterScript = "Filter_for_KOSA_GUI"; break;
                }

                bool showSktbCard = ShowSktbCardCheck.IsChecked == true;

                string platinumSource = PlatinumSourceCombo.SelectedIndex switch
                {
                    1 => "mit_803",
                    2 => "tercon",
                    _ => "mtk_termex"
                };

                string platinumComPort = PlatinumComPortCombo.SelectedItem as string ?? "COM1";
                bool readPlatinum = ReadPlatinumCheck.IsChecked == true;

                string platinumMtkCommand = (PlatinumMtkCommandBox.Text ?? "").Trim();
                if (string.IsNullOrEmpty(platinumMtkCommand))
                    platinumMtkCommand = "pass2 meas2? 2";

                bool q2viewEnabled = GeneralQ2ViewCheck.IsChecked == true;
                bool timerEnabled = q2viewEnabled;

                int timerSeconds = 5;
                if (!int.TryParse(GeneralTimerBox.Text, out timerSeconds) || timerSeconds <= 0)
                    timerSeconds = 5;

                bool autoExcelEnabled = GeneralAutoExcelCheck.IsChecked == true;
                bool tickTimingEnabled = GeneralTickTimingCheck.IsChecked == true;
                bool logSlowTicksEnabled = GeneralLogSlowTicksCheck.IsChecked == true;

                mw.ApplyDeviceSettings(
                    furnaceIp, furnacePort,
                    furnaceSetPointReg, furnaceCurrentTempReg, furnaceModeReg,
                    autoStopFurnace,
                    showFurnaceCard,
                    agkIp, agkPort, showAgkCard,
                    agkPressureTimeoutMinutes,
                    sktbFilterScript, showSktbCard,
                    platinumSource, platinumComPort, readPlatinum,
                    platinumMtkCommand,
                    furnaceStabilizationTolerance);

                mw.ApplyGeneralSettings(
                    q2viewEnabled,
                    timerEnabled,
                    timerSeconds,
                    autoExcelEnabled,
                    tickTimingEnabled,
                    logSlowTicksEnabled);

                SaveCurrentEmailFields(EmailProviderCombo.SelectedIndex);

                long vkId = long.TryParse(VkPeerIdBox.Text, out long vid) ? vid : 0;

                mw.ApplyNotificationSettings(
                    enabled: NotificationsEnabledCheck.IsChecked == true,

                    tgEnabled: ChkSendToTelegram.IsChecked == true,
                    yandexEnabled: ChkSendToYandex.IsChecked == true,
                    mailEnabled: ChkSendToMail.IsChecked == true,
                    gmailEnabled: ChkSendToGmail.IsChecked == true,
                    customEnabled: ChkSendToCustomEmail.IsChecked == true,
                    vkEnabled: ChkSendToVk.IsChecked == true,
                    maxEnabled: ChkSendToMax.IsChecked == true,

                    methodIndex: NotificationMethodCombo.SelectedIndex,

                    tgToken: TelegramTokenBox.Text.Trim(),
                    tgChatId: TelegramChatIdBox.Text.Trim(),

                    vkToken: VkTokenBox.Text.Trim(),
                    vkPeerId: vkId,

                    maxToken: MaxTokenBox.Text.Trim(),
                    maxChatId: MaxChatIdBox.Text.Trim(),

                    smsLogin: SmsLoginBox.Text.Trim(),
                    smsPass: SmsPassBox.Password,
                    smsPhone: SmsPhoneBox.Text.Trim()
                );

                Close();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this,
                    $"Ошибка при сохранении настроек:\n{ex.Message}\n\n{ex.StackTrace}",
                    "Ошибка",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            Close();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Тест уведомлений
        // ═══════════════════════════════════════════════════════════════════

        private async void TestNotificationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!NotificationsEnabledCheck.IsChecked == true)
            {
                MessageBox.Show(this, "Сначала поставьте галочку «Включить уведомления».", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            var mw = MainWindow.Current;
            if (mw == null) return;

            string testMessage = "🔔 Тестовое уведомление из T.E.R.M.I.N.A.L. Всё работает!";
            bool sentAtLeastOne = false;
            var errors = new List<string>();

            try
            {
                if (ChkSendToTelegram.IsChecked == true)
                {
                    string tgToken = TelegramTokenBox.Text.Trim();
                    string tgChatId = TelegramChatIdBox.Text.Trim();

                    if (!string.IsNullOrEmpty(tgToken) && long.TryParse(tgChatId, out long chatIdLong))
                    {
                        await new TelegramNotificationService(tgToken, chatIdLong).SendAsync(testMessage + " (Telegram)");
                        sentAtLeastOne = true;
                    }
                    else
                    {
                        errors.Add("Telegram: неверный Token или Chat ID.");
                    }
                }

                int emailPort = int.TryParse(EmailPortBox.Text, out int ep) ? ep : 587;

                if (ChkSendToYandex.IsChecked == true)
                {
                    if (!string.IsNullOrEmpty(mw.EmailYandexUser))
                        await new EmailNotificationService("smtp.yandex.ru", 587, mw.EmailYandexUser, mw.EmailYandexPass, mw.EmailYandexTo).SendAsync(testMessage + " (Яндекс)");
                    else
                        errors.Add("Яндекс: данные не заполнены.");
                    sentAtLeastOne = true;
                }

                if (ChkSendToMail.IsChecked == true)
                {
                    if (!string.IsNullOrEmpty(mw.EmailMailUser))
                        await new EmailNotificationService("smtp.mail.ru", 587, mw.EmailMailUser, mw.EmailMailPass, mw.EmailMailTo).SendAsync(testMessage + " (Mail.ru)");
                    else
                        errors.Add("Mail.ru: данные не заполнены.");
                    sentAtLeastOne = true;
                }

                if (ChkSendToGmail.IsChecked == true)
                {
                    if (!string.IsNullOrEmpty(mw.EmailGmailUser))
                        await new EmailNotificationService("smtp.gmail.com", 587, mw.EmailGmailUser, mw.EmailGmailPass, mw.EmailGmailTo).SendAsync(testMessage + " (Gmail)");
                    else
                        errors.Add("Gmail: данные не заполнены.");
                    sentAtLeastOne = true;
                }

                if (ChkSendToCustomEmail.IsChecked == true)
                {
                    if (!string.IsNullOrEmpty(mw.EmailCustomHost))
                        await new EmailNotificationService(mw.EmailCustomHost, mw.EmailCustomPort, mw.EmailCustomUser, mw.EmailCustomPass, mw.EmailCustomTo).SendAsync(testMessage + " (Другой Email)");
                    else
                        errors.Add("Другой Email: данные не заполнены.");
                    sentAtLeastOne = true;
                }

                if (ChkSendToVk.IsChecked == true)
                {
                    string vkToken = VkTokenBox.Text.Trim();
                    if (!string.IsNullOrEmpty(vkToken) && long.TryParse(VkPeerIdBox.Text.Trim(), out long vkPeerId))
                    {
                        await new VkNotificationService(vkToken, vkPeerId).SendAsync(testMessage + " (VK)");
                        sentAtLeastOne = true;
                    }
                    else
                    {
                        errors.Add("VK: неверный Token или ID.");
                    }
                }

                if (ChkSendToMax.IsChecked == true)
                {
                    string maxToken = MaxTokenBox.Text.Trim();
                    if (!string.IsNullOrEmpty(maxToken) && long.TryParse(MaxChatIdBox.Text.Trim(), out long maxChatId))
                    {
                        await new MaxNotificationService(maxToken, maxChatId).SendAsync(testMessage + " (MAX)");
                        sentAtLeastOne = true;
                    }
                    else
                    {
                        errors.Add("MAX: неверный Token или Chat ID.");
                    }
                }

                if (sentAtLeastOne && errors.Count == 0)
                {
                    MessageBox.Show(this, "Тестовые сообщения успешно отправлены во все выбранные каналы!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                else if (sentAtLeastOne && errors.Count > 0)
                {
                    MessageBox.Show(this, "Частично отправлено. Ошибки:\n" + string.Join("\n", errors), "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
                else if (!sentAtLeastOne && errors.Count > 0)
                {
                    MessageBox.Show(this, "Не удалось отправить. Ошибки:\n" + string.Join("\n", errors), "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
                }
                else
                {
                    MessageBox.Show(this, "Не выбран ни один канал для отправки (поставьте галочки в списке).", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка отправки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Валидация
        // ═══════════════════════════════════════════════════════════════════

        private bool Validate()
        {
            if (!IPAddress.TryParse(FurnaceIpBox.Text.Trim(), out _))
            {
                MessageBox.Show("ST-351: некорректный IP-адрес.");
                return false;
            }
            if (!int.TryParse(FurnacePortBox.Text.Trim(), out int fp) || fp < 1 || fp > 65535)
            {
                MessageBox.Show("ST-351: порт должен быть 1..65535.");
                return false;
            }
            if (!ushort.TryParse(FurnaceSetPointRegBox.Text.Trim(), out _))
            {
                MessageBox.Show("ST-351: некорректный регистр уставки.");
                return false;
            }
            if (!ushort.TryParse(FurnaceCurrentTempRegBox.Text.Trim(), out _))
            {
                MessageBox.Show("ST-351: некорректный регистр текущей температуры.");
                return false;
            }
            if (!ushort.TryParse(FurnaceModeRegBox.Text.Trim(), out _))
            {
                MessageBox.Show("ST-351: некорректный регистр режима.");
                return false;
            }

            if (!IPAddress.TryParse(AgkIpBox.Text.Trim(), out _))
            {
                MessageBox.Show("АГК: некорректный IP-адрес.");
                return false;
            }
            if (!int.TryParse(AgkPortBox.Text.Trim(), out int ap) || ap < 1 || ap > 65535)
            {
                MessageBox.Show("АГК: порт должен быть 1..65535.");
                return false;
            }

            return true;
        }

        // ═══════════════════════════════════════════════════════════════════
        // COM-порты
        // ═══════════════════════════════════════════════════════════════════

        private void FillComPorts(string current)
        {
            PlatinumComPortCombo.Items.Clear();

            string[] ports = System.IO.Ports.SerialPort.GetPortNames();

            if (ports.Length == 0)
            {
                PlatinumComPortCombo.Items.Add("Нет доступных портов");
                PlatinumComPortCombo.SelectedIndex = 0;
                return;
            }

            Array.Sort(ports, (x, y) =>
            {
                int nx = int.TryParse(x.Replace("COM", ""), out int a) ? a : 0;
                int ny = int.TryParse(y.Replace("COM", ""), out int b) ? b : 0;
                return nx.CompareTo(ny);
            });

            foreach (var port in ports)
                PlatinumComPortCombo.Items.Add(port);

            PlatinumComPortCombo.SelectedItem = current;

            if (PlatinumComPortCombo.SelectedItem == null && PlatinumComPortCombo.Items.Count > 0)
            {
                if (PlatinumComPortCombo.Items[0].ToString() != "Нет доступных портов")
                    PlatinumComPortCombo.SelectedIndex = 0;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Платина
        // ═══════════════════════════════════════════════════════════════════

        private void PlatinumSourceCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (PlatinumComPanel == null || PlatinumSourceCombo == null) return;

            bool isMit803 = PlatinumSourceCombo.SelectedIndex == 1;
            bool isMtk = PlatinumSourceCombo.SelectedIndex == 0;

            PlatinumComPanel.Visibility = isMit803
                ? Visibility.Visible
                : Visibility.Collapsed;

            if (PlatinumMtkCommandPanel != null)
                PlatinumMtkCommandPanel.Visibility = isMtk
                    ? Visibility.Visible
                    : Visibility.Collapsed;
        }

        // ═══════════════════════════════════════════════════════════════════
        // Уведомления — переключение панелей
        // ═══════════════════════════════════════════════════════════════════

        private void NotificationMethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TelegramPanel == null || EmailPanel == null || VkPanel == null || MaxPanel == null || SmsPanel == null)
                return;

            TelegramPanel.Visibility = Visibility.Collapsed;
            EmailPanel.Visibility = Visibility.Collapsed;
            VkPanel.Visibility = Visibility.Collapsed;
            MaxPanel.Visibility = Visibility.Collapsed;
            SmsPanel.Visibility = Visibility.Collapsed;

            switch (NotificationMethodCombo.SelectedIndex)
            {
                case 0:
                    TelegramPanel.Visibility = Visibility.Visible;
                    break;
                case 1:
                    EmailPanel.Visibility = Visibility.Visible;
                    break;
                case 2:
                    VkPanel.Visibility = Visibility.Visible;
                    break;
                case 3:
                    MaxPanel.Visibility = Visibility.Visible;
                    break;
                case 4:
                    SmsPanel.Visibility = Visibility.Visible;
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Email — провайдеры
        // ═══════════════════════════════════════════════════════════════════

        private void EmailProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmailHostBox == null || EmailPortBox == null) return;
            if (_isLoadingData) return;

            int newIndex = EmailProviderCombo.SelectedIndex;

            if (_lastSelectedProvider != newIndex)
            {
                SaveCurrentEmailFields(_lastSelectedProvider);
                _lastSelectedProvider = newIndex;
            }

            LoadEmailFields(newIndex);

            switch (newIndex)
            {
                case 0:
                    EmailHostBox.Text = "smtp.yandex.ru";
                    EmailPortBox.Text = "587";
                    break;
                case 1:
                    EmailHostBox.Text = "smtp.mail.ru";
                    EmailPortBox.Text = "587";
                    break;
                case 2:
                    EmailHostBox.Text = "smtp.gmail.com";
                    EmailPortBox.Text = "587";
                    break;
                case 3:
                    EmailHostBox.Text = MainWindow.Current?.EmailCustomHost ?? "";
                    EmailPortBox.Text = (MainWindow.Current?.EmailCustomPort ?? 587).ToString();
                    break;
            }
        }

        private void SaveCurrentEmailFields(int providerIndex)
        {
            if (MainWindow.Current == null) return;
            var mw = MainWindow.Current;

            switch (providerIndex)
            {
                case 0:
                    mw.EmailYandexUser = EmailUserBox.Text;
                    mw.EmailYandexPass = EmailPassBox.Password;
                    mw.EmailYandexTo = EmailToBox.Text;
                    break;
                case 1:
                    mw.EmailMailUser = EmailUserBox.Text;
                    mw.EmailMailPass = EmailPassBox.Password;
                    mw.EmailMailTo = EmailToBox.Text;
                    break;
                case 2:
                    mw.EmailGmailUser = EmailUserBox.Text;
                    mw.EmailGmailPass = EmailPassBox.Password;
                    mw.EmailGmailTo = EmailToBox.Text;
                    break;
                case 3:
                    mw.EmailCustomHost = EmailHostBox.Text;
                    mw.EmailCustomPort = int.TryParse(EmailPortBox.Text, out int p) ? p : 587;
                    mw.EmailCustomUser = EmailUserBox.Text;
                    mw.EmailCustomPass = EmailPassBox.Password;
                    mw.EmailCustomTo = EmailToBox.Text;
                    break;
            }
        }

        private void LoadEmailFields(int providerIndex)
        {
            if (MainWindow.Current == null) return;
            var mw = MainWindow.Current;

            switch (providerIndex)
            {
                case 0:
                    EmailUserBox.Text = mw.EmailYandexUser;
                    EmailPassBox.Password = mw.EmailYandexPass;
                    EmailToBox.Text = mw.EmailYandexTo;
                    break;
                case 1:
                    EmailUserBox.Text = mw.EmailMailUser;
                    EmailPassBox.Password = mw.EmailMailPass;
                    EmailToBox.Text = mw.EmailMailTo;
                    break;
                case 2:
                    EmailUserBox.Text = mw.EmailGmailUser;
                    EmailPassBox.Password = mw.EmailGmailPass;
                    EmailToBox.Text = mw.EmailGmailTo;
                    break;
                case 3:
                    EmailHostBox.Text = mw.EmailCustomHost;
                    EmailPortBox.Text = mw.EmailCustomPort.ToString();
                    EmailUserBox.Text = mw.EmailCustomUser;
                    EmailPassBox.Password = mw.EmailCustomPass;
                    EmailToBox.Text = mw.EmailCustomTo;
                    break;
            }
        }

        // ═══════════════════════════════════════════════════════════════════
        // Инструкция
        // ═══════════════════════════════════════════════════════════════════

        private void NotificationHelpButton_Click(object sender, RoutedEventArgs e)
        {
            int selectedIndex = NotificationMethodCombo?.SelectedIndex ?? 0;
            string instruction = "";

            switch (selectedIndex)
            {
                case 0: // Telegram
                    instruction =
        @"🤖 ИНСТРУКЦИЯ ДЛЯ TELEGRAM:

1. Получение Token:
   • Откройте Telegram и найдите бота @BotFather (у него должна быть галочка).
   • Отправьте ему команду /newbot.
   • Введите имя бота (например, 'Мой Терминал') и юзернейм (обязательно должен заканчиваться на 'bot', например 'my_terminal_bot').
   • BotFather пришлет вам длинный токен (вида 123456789:ABCdef...). Скопируйте его в поле 'Telegram Token'.

2. Получение Chat ID:
   • Найдите в Telegram только что созданного бота и нажмите 'Запустить' (Start).
   • Отправьте ему любое сообщение (например, 'привет').
   • В браузере перейдите по ссылке (заменив ТОКЕН на свой): 
     https://api.telegram.org/bot<ВАШ_ТОКЕН>/getUpdates
   • В ответе найдите текст ""chat"":{""id"": 123456789}. Число 123456789 — это ваш Chat ID. Скопируйте его.
   • Вставьте Chat ID в программу и нажмите 'Тест'.

⚠️ ВАЖНО: Если вы в России, Telegram API может быть заблокирован. Для корректной работы включите VPN (в программе Happ лучше использовать режим TUN или Системный прокси) перед отправкой сообщений.";
                    break;

                case 1: // Email (SMTP)
                    instruction =
        @"📧 ИНСТРУКЦИЯ ДЛЯ EMAIL:

1. Выберите почтового провайдера:
   • В выпадающем списке выберите вашу почту (Яндекс, Mail.ru, Gmail).
   • Сервер и порт подставятся автоматически. Для Mail.ru используется порт 587.

2. Логин и Пароль:
   • Введите ваш полный адрес электронной почты (например, user@yandex.ru).
   • ⚠️ ВАЖНО: Обычный пароль от почты не подойдет! Зайдите в настройки безопасности своей почты и создайте 'Пароль приложения' (App Password). Скопируйте этот сгенерированный пароль в поле 'Пароль'.

3. Кому отправлять:
   • Введите адрес почты, на который должны приходить уведомления.";
                    break;

                case 2: // VK
                    instruction =
        @"💬 ИНСТРУКЦИЯ ДЛЯ ВКОНТАКТЕ:

⚠️ ВАЖНО: В обычном Standalone-приложении (созданном на dev.vk.com) ВК часто блокирует отправку сообщений. Самый надежный способ — использовать сообщество (группу).

1. Создайте группу (сообщество) ВКонтакте.
2. Зайдите на главную страницу вашей группы (vk.ru/club...).
3. В ПРАВОМ меню (где написано «Настройки», «Подписчики», «Сообщения») выберите размел Дополнительно > «Работа с API».
4. Нажмите «Создать ключ» и выберите право «Управление сообщениями». Скопируйте этот токен в поле 'Access Token'.
5. В поле 'Ваш ID' введите ВАШ ЛИЧНЫЙ ID (число из адресной строки vk.ru/id12345678), а НЕ ID группы.
6. В левом меню Управления группой зайдите в «Сообщения» и включите «Сообщения сообщества».
7. ★ ВАЖНО: На главной странице вашей группы нажмите кнопку «Ещё» (рядом с кнопкой «Сообщение») и убедитесь, что там выбрано «Разрешить сообщения», а НЕ «Запретить сообщения».
8. ОБЯЗАТЕЛЬНО: Напишите любое сообщение в эту группу от своего личного профиля. Бот создаст диалог с вами (выглядит как беседа с ID, начинающимся на 2000000000). После этого он сможет отвечать вам.";
                    break;

                case 3: // MAX
                    instruction =
        @"🤖 ИНСТРУКЦИЯ ДЛЯ МАКС (max.ru):

⚠️ ВАЖНО: Ботов в MAX могут создавать только юридические лица, ИП или самозанятые, прошедшие верификацию на платформе «МАКС для партнеров». Обычный пользователь создать бота не может.

1. Получение токена: Зарегистрируйтесь на платформе для партнеров, создайте бота и получите его токен.
2. Установите сертификаты: Для работы API MAX на компьютере должны быть установлены российские TLS-сертификаты (Russian Trusted CA). Скачать их можно на портале Госуслуг (gosuslugi.ru/crt). Без этого программа выдаст ошибку SSL.
3. Вставьте токен в поле 'Токен бота MAX', укажите Chat ID и нажмите 'Тест'.";
                    break;

                case 4: // SMS
                    instruction =
        @"📱 ИНСТРУКЦИЯ ДЛЯ SMS (SMSC.ru):

1. Зарегистрируйтесь на сайте smsc.ru и пополните баланс.
2. Введите логин и пароль, которые вы указали при регистрации.
3. Введите номер телефона в формате +79991234567.

⚠️ ВАЖНО: 
   • SMS — это ПЛАТНЫЙ канал.
   • Программа не отправляет SMS о каждом шаге. SMS используются только для КРИТИЧЕСКИХ аварий (например, потеря связи с прибором во время работы), чтобы вы не тратили деньги впустую.";
                    break;
            }

            var helpWindow = new Window
            {
                Title = "Инструкция по настройке уведомлений",
                Width = 600,
                Height = 500,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.CanResize
            };

            var grid = new Grid { Margin = new Thickness(10) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) });
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto });

            var textBox = new TextBox
            {
                Text = instruction,
                IsReadOnly = true,
                TextWrapping = TextWrapping.Wrap,
                VerticalScrollBarVisibility = ScrollBarVisibility.Auto,
                FontFamily = new System.Windows.Media.FontFamily("Consolas"),
                FontSize = 12,
                Padding = new Thickness(5),
                Margin = new Thickness(0, 0, 0, 10)
            };
            Grid.SetRow(textBox, 1);

            var btnCopy = new Button
            {
                Content = "📋 Скопировать всё",
                Width = 150,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 10)
            };
            btnCopy.Click += (s, args) =>
            {
                Clipboard.SetText(instruction);
                MessageBox.Show("Текст скопирован в буфер обмена!", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            };
            Grid.SetRow(btnCopy, 0);

            var btnOk = new Button
            {
                Content = "ОК",
                Width = 100,
                Height = 30,
                HorizontalAlignment = HorizontalAlignment.Right
            };
            btnOk.Click += (s, args) => helpWindow.Close();
            Grid.SetRow(btnOk, 2);

            grid.Children.Add(btnCopy);
            grid.Children.Add(textBox);
            grid.Children.Add(btnOk);

            helpWindow.Content = grid;
            helpWindow.ShowDialog();
        }

        // ═══════════════════════════════════════════════════════════════════
        // Вспомогательное
        // ═══════════════════════════════════════════════════════════════════

        private void NotifyCheckBox_PreviewMouseDown(object sender, System.Windows.Input.MouseButtonEventArgs e)
        {
            e.Handled = true;

            if (sender is CheckBox cb)
                cb.IsChecked = !(cb.IsChecked == true);
        }
    }
}