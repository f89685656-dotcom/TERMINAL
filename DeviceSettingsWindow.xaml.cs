using System;
using System.Globalization;
using System.Net;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;
using System.Threading.Tasks;
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

            // ★ Применяем переданные значения платины ДО LoadCurrent,
            //   чтобы LoadCurrent мог их перезаписать из MainWindow.Current (актуальнее).
            //   Если MainWindow.Current == null — останутся переданные.
            PlatinumMtkCommandBox.Text = platinumMtkCommand;


            LoadCurrent();
        }

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
            // Уведомления
            // ═══════════════════════════════════════════════════════════════════
            NotificationsEnabledCheck.IsChecked = mw.NotificationsEnabled;

            // Выбор метода (0-Telegram, 1-Email, 2-VK, 3-SMS)
            NotificationMethodCombo.SelectedIndex = mw.NotificationMethodIndex;

            // Принудительно обновляем видимость панелей, так как событие SelectionChanged 
            // могло не сработать при программной установке SelectedIndex
            NotificationMethodCombo_SelectionChanged(null, null);

            // ★ ЗАГРУЖАЕМ ГАЛОЧКИ
            TelegramEnabledCheck.IsChecked = mw.NotifyTgEnabled;
            EmailEnabledCheck.IsChecked = mw.NotifyEmailEnabled;
            VkEnabledCheck.IsChecked = mw.NotifyVkEnabled;
            MaxEnabledCheck.IsChecked = mw.NotifyMaxEnabled;

            // Заполнение полей для всех каналов
            TelegramTokenBox.Text = mw.TelegramToken;
            TelegramChatIdBox.Text = mw.TelegramChatId;

            // ─── Уведомления: Email ───
            // 1. Включаем защиту от срабатывания события SelectionChanged
            _isLoadingData = true;
            EmailProviderCombo.SelectedIndex = mw.EmailProviderIndex;
            _isLoadingData = false;

            // 2. Вручную выставляем сервер и порт в зависимости от провайдера
            switch (mw.EmailProviderIndex)
            {
                case 0: // Яндекс
                    EmailHostBox.Text = "smtp.yandex.ru";
                    EmailPortBox.Text = "587";
                    break;
                case 1: // Mail.ru
                    EmailHostBox.Text = "smtp.mail.ru";
                    EmailPortBox.Text = "465";
                    break;
                case 2: // Gmail
                    EmailHostBox.Text = "smtp.gmail.com";
                    EmailPortBox.Text = "587";
                    break;
                case 3: // Другой
                    EmailHostBox.Text = mw.EmailCustomHost;
                    EmailPortBox.Text = mw.EmailCustomPort.ToString();
                    break;
            }

            // 3. Загружаем сохраненные логин/пароль/получателя для этого провайдера
            LoadEmailFields(mw.EmailProviderIndex);

            VkTokenBox.Text = mw.VkToken;
            VkPeerIdBox.Text = mw.VkPeerId.ToString();

            SmsLoginBox.Text = mw.SmsLogin;
            SmsPassBox.Password = mw.SmsPass;
            SmsPhoneBox.Text = mw.SmsPhone;
        }

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
            GeneralTimerBox.IsEnabled = false;   // таймер неактивен, пока q2view выключен
            GeneralAutoExcelCheck.IsChecked = true;
            GeneralTickTimingCheck.IsChecked = true;
            GeneralLogSlowTicksCheck.IsChecked = true;

            // ─── Уведомления ───
            NotificationsEnabledCheck.IsChecked = false;
            TelegramTokenBox.Text = "";
            TelegramChatIdBox.Text = "";
            NotificationMethodCombo.SelectedIndex = 0;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            var mw = MainWindow.Current;
            if (mw == null)
            {
                DialogResult = false;
                Close();
                return;
            }

            // ═══════════════════════════════════════════════════════════════
            // 1. Читаем и валидируем значения из полей ST-351
            // ═══════════════════════════════════════════════════════════════

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

            // ★ НОВОЕ: парсинг допуска
            double furnaceStabilizationTolerance = 0.1;
            if (!double.TryParse(FurnaceStabilizationToleranceBox.Text.Replace(',', '.'),
                NumberStyles.Float, CultureInfo.InvariantCulture, out furnaceStabilizationTolerance)
                || furnaceStabilizationTolerance <= 0)
            {
                furnaceStabilizationTolerance = 0.1;
            }

            bool autoStopFurnace = AutoStopFurnaceCheck.IsChecked == true;

            bool showFurnaceCard = ShowFurnaceCardCheck.IsChecked == true;



            // ═══════════════════════════════════════════════════════════════
            // 2. Читаем и валидируем значения из полей АГК
            // ═══════════════════════════════════════════════════════════════

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

            // ═══════════════════════════════════════════════════════════════
            // 3. Читаем значения СКТБ ЭлПА
            // ═══════════════════════════════════════════════════════════════

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

            // ═══════════════════════════════════════════════════════════════
            // 4. Читаем и валидируем общие настройки
            // ═══════════════════════════════════════════════════════════════

            bool q2viewEnabled = GeneralQ2ViewCheck.IsChecked == true;
            bool timerEnabled = q2viewEnabled;    // таймер включается вместе с q2view

            int timerSeconds = 5;
            if (!int.TryParse(GeneralTimerBox.Text, out timerSeconds) || timerSeconds <= 0)
                timerSeconds = 5;

            bool autoExcelEnabled = GeneralAutoExcelCheck.IsChecked == true;

            bool tickTimingEnabled = GeneralTickTimingCheck.IsChecked == true;

            bool logSlowTicksEnabled = GeneralLogSlowTicksCheck.IsChecked == true;

            // ═══════════════════════════════════════════════════════════════
            // 5. Применяем настройки приборов к MainWindow
            // ═══════════════════════════════════════════════════════════════

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


            // ═══════════════════════════════════════════════════════════════
            // 6. Применяем общие настройки к MainWindow
            // ═══════════════════════════════════════════════════════════════

            mw.ApplyGeneralSettings(
                q2viewEnabled,
                timerEnabled,
                timerSeconds,
                autoExcelEnabled,
                tickTimingEnabled,
                logSlowTicksEnabled);

            // ═══════════════════════════════════════════════════════════════
            // 6.5. Применяем настройки уведомлений
            // ═══════════════════════════════════════════════════════════════
            int emailPort = int.TryParse(EmailPortBox.Text, out int ep) ? ep : 587;
            long vkId = long.TryParse(VkPeerIdBox.Text, out long vid) ? vid : 0;

            mw.ApplyNotificationSettings(
                enabled: NotificationsEnabledCheck.IsChecked == true,

                // ★ ПЕРЕДАЕМ ГАЛОЧКИ АКТИВНОСТИ КАНАЛОВ
                tgEnabled: TelegramEnabledCheck.IsChecked == true,
                emailEnabled: EmailEnabledCheck.IsChecked == true,
                vkEnabled: VkEnabledCheck.IsChecked == true,
                maxEnabled: MaxEnabledCheck.IsChecked == true,

                methodIndex: NotificationMethodCombo.SelectedIndex,

                tgToken: TelegramTokenBox.Text.Trim(),
                tgChatId: TelegramChatIdBox.Text.Trim(),

                emailHost: EmailHostBox.Text.Trim(),
                emailPort: emailPort,
                emailUser: EmailUserBox.Text.Trim(),
                emailPass: EmailPassBox.Password,
                emailTo: EmailToBox.Text.Trim(),
                emailProviderIndex: EmailProviderCombo.SelectedIndex,

                vkToken: VkTokenBox.Text.Trim(),
                vkPeerId: vkId,

                maxToken: MaxTokenBox.Text.Trim(),
                maxChatId: MaxChatIdBox.Text.Trim(),

                smsLogin: SmsLoginBox.Text.Trim(),
                smsPass: SmsPassBox.Password,
                smsPhone: SmsPhoneBox.Text.Trim()
            );
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
        }

        private async void TestNotificationButton_Click(object sender, RoutedEventArgs e)
        {
            if (!NotificationsEnabledCheck.IsChecked == true)
            {
                MessageBox.Show(this, "Сначала поставьте галочку «Включить уведомления».", "Внимание", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }

            int method = NotificationMethodCombo.SelectedIndex;

            try
            {
                switch (method)
                {
                    case 0: // Telegram
                        string tgToken = TelegramTokenBox.Text.Trim();
                        string tgChatId = TelegramChatIdBox.Text.Trim();
                        if (string.IsNullOrEmpty(tgToken) || string.IsNullOrEmpty(tgChatId))
                        {
                            MessageBox.Show(this, "Заполните Token и Chat ID для Telegram.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        if (!long.TryParse(tgChatId, out long chatIdLong))
                        {
                            MessageBox.Show(this, "Chat ID должен быть числом.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        var tgService = new TelegramNotificationService(tgToken, chatIdLong);
                        await tgService.SendAsync("🔔 Тестовое уведомление из T.E.R.M.I.N.A.L (Telegram)");
                        break;

                    case 1: // Email
                        string emailHost = EmailHostBox.Text.Trim();
                        string emailUser = EmailUserBox.Text.Trim();
                        string emailPass = EmailPassBox.Password;
                        string emailTo = EmailToBox.Text.Trim();
                        int emailPort = int.TryParse(EmailPortBox.Text, out int ep) ? ep : 587;

                        if (string.IsNullOrEmpty(emailHost) || string.IsNullOrEmpty(emailUser) || string.IsNullOrEmpty(emailPass))
                        {
                            MessageBox.Show(this, "Заполните SMTP сервер, логин и пароль.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        var emailService = new EmailNotificationService(emailHost, emailPort, emailUser, emailPass, emailTo);
                        await emailService.SendAsync("🔔 Тестовое уведомление из T.E.R.M.I.N.A.L (Email)");
                        break;

                    case 2: // VK
                        string vkToken = VkTokenBox.Text.Trim();
                        string vkPeerIdStr = VkPeerIdBox.Text.Trim();
                        if (string.IsNullOrEmpty(vkToken) || !long.TryParse(vkPeerIdStr, out long vkPeerId))
                        {
                            MessageBox.Show(this, "Заполните Access Token и числовой ID для VK.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        var vkService = new VkNotificationService(vkToken, vkPeerId);
                        await vkService.SendAsync("🔔 Тестовое уведомление из T.E.R.M.I.N.A.L (VK)");
                        break;

                    case 3: // MAX
                        string maxToken = MaxTokenBox.Text.Trim();
                        string maxChatId = MaxChatIdBox.Text.Trim();
                        if (string.IsNullOrEmpty(maxToken) || !long.TryParse(maxChatId, out long maxId))
                        {
                            MessageBox.Show(this, "Заполните Token и числовой Chat ID для MAX.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        var maxService = new MaxNotificationService(maxToken, maxId);
                        await maxService.SendAsync("🔔 Тестовое уведомление из T.E.R.M.I.N.A.L (MAX)");
                        break;

                    case 4: // SMS (было 3)
                        string smsLogin = SmsLoginBox.Text.Trim();
                        string smsPass = SmsPassBox.Password;
                        string smsPhone = SmsPhoneBox.Text.Trim();
                        if (string.IsNullOrEmpty(smsLogin) || string.IsNullOrEmpty(smsPass) || string.IsNullOrEmpty(smsPhone))
                        {
                            MessageBox.Show(this, "Заполните логин, пароль и номер телефона для SMS.", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Warning);
                            return;
                        }
                        var smsService = new SmsNotificationService(smsLogin, smsPass, smsPhone);
                        await smsService.SendAsync("Тест SMS из TERMINAL");
                        break;
                }

                MessageBox.Show(this, "Сообщение отправлено! Проверьте выбранный канал.", "Успех", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"Ошибка отправки: {ex.Message}", "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

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

        private void FillComPorts(string current)
        {
            PlatinumComPortCombo.Items.Clear();

            // ★ Получаем реальные COM-порты, доступные в системе
            string[] ports = System.IO.Ports.SerialPort.GetPortNames();

            if (ports.Length == 0)
            {
                // Если портов нет вообще, можно добавить заглушку или оставить пустым
                PlatinumComPortCombo.Items.Add("Нет доступных портов");
                PlatinumComPortCombo.SelectedIndex = 0;
                return;
            }

            // Сортируем порты по алфавиту (COM1, COM2, COM10 и т.д.)
            Array.Sort(ports, (x, y) =>
            {
                int nx = int.TryParse(x.Replace("COM", ""), out int a) ? a : 0;
                int ny = int.TryParse(y.Replace("COM", ""), out int b) ? b : 0;
                return nx.CompareTo(ny);
            });

            foreach (var port in ports)
                PlatinumComPortCombo.Items.Add(port);

            // Пытаемся выбрать текущий порт. Если его нет в списке (например, отключен), выбираем первый доступный.
            PlatinumComPortCombo.SelectedItem = current;

            if (PlatinumComPortCombo.SelectedItem == null && PlatinumComPortCombo.Items.Count > 0)
            {
                // Если сохраненного порта нет, но есть другие — выбираем первый
                // Проверяем, что первый элемент не "Нет доступных портов"
                if (PlatinumComPortCombo.Items[0].ToString() != "Нет доступных портов")
                    PlatinumComPortCombo.SelectedIndex = 0;
            }
        }

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

        private void NotificationMethodCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (TelegramPanel == null || EmailPanel == null || VkPanel == null || MaxPanel == null || SmsPanel == null)
                return;

            // 1. Скрываем ВСЕ панели
            TelegramPanel.Visibility = Visibility.Collapsed;
            EmailPanel.Visibility = Visibility.Collapsed;
            VkPanel.Visibility = Visibility.Collapsed;
            MaxPanel.Visibility = Visibility.Collapsed;
            SmsPanel.Visibility = Visibility.Collapsed;

            // 2. Показываем нужную
            switch (NotificationMethodCombo.SelectedIndex)
            {
                case 0: // Telegram
                    TelegramPanel.Visibility = Visibility.Visible;
                    break;
                case 1: // Email
                    EmailPanel.Visibility = Visibility.Visible;
                    break;
                case 2: // VK
                    VkPanel.Visibility = Visibility.Visible;
                    break;
                case 3: // MAX
                    MaxPanel.Visibility = Visibility.Visible;
                    break;
                case 4: // SMS
                    SmsPanel.Visibility = Visibility.Visible;
                    break;
            }
        }

        private void EmailProviderCombo_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            if (EmailHostBox == null || EmailPortBox == null) return;

            // ★ Защита от срабатывания во время загрузки данных
            if (_isLoadingData) return;

            // ★ Берем данные того провайдера, с которого мы УХОДИМ.
            // Если список только инициализируется, e.RemovedItems может быть пустым.
            if (e.RemovedItems.Count > 0)
            {
                var oldItem = e.RemovedItems[0] as ComboBoxItem;
                if (oldItem != null)
                {
                    int oldIndex = EmailProviderCombo.Items.IndexOf(oldItem);
                    SaveCurrentEmailFields(oldIndex);
                }
            }

            // 2. Меняем сервер и порт для НОВОГО провайдера
            int newIndex = EmailProviderCombo.SelectedIndex;
            switch (newIndex)
            {
                case 0: // Яндекс
                    EmailHostBox.Text = "smtp.yandex.ru";
                    EmailPortBox.Text = "587";
                    break;
                case 1: // Mail.ru
                    EmailHostBox.Text = "smtp.mail.ru";
                    EmailPortBox.Text = "587";
                    break;
                case 2: // Gmail
                    EmailHostBox.Text = "smtp.gmail.com";
                    EmailPortBox.Text = "587";
                    break;
                case 3: // Другой
                        // Для "Другого" поля сервера и порта не трогаем, чтобы можно было ввести свои
                    break;
            }

            // 3. Загружаем данные для НОВОГО провайдера
            LoadEmailFields(newIndex);
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

            // Создаем кастомное окно для инструкции
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
    }
}