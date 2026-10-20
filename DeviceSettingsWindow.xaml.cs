using System;
using System.Globalization;
using System.Net;
using System.IO.Ports;
using System.Windows;
using System.Windows.Controls;

namespace TERMINAL.Windows
{
    public partial class DeviceSettingsWindow : Window
    {
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

            PlatinumSourceCombo.SelectedIndex = mw.PlatinumSource switch
            {
                "mit_803" => 1,
                "tercon" => 2,
                _ => 0
            };

            FillComPorts(mw.PlatinumComPort);
            ReadPlatinumCheck.IsChecked = mw.ReadPlatinum;
            PlatinumMtkCommandBox.Text = mw.PlatinumMtkCommand;

            // После установки индекса — пересчитать видимость панели COM
            PlatinumSourceCombo_SelectionChanged(null, null);

            // ─── Общие настройки ───
            // Конструктор уже заполнил General* значениями, переданными из MainWindow.
            // Здесь ничего делать не нужно.
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
            // 7. Закрываем окно
            // ═══════════════════════════════════════════════════════════════

            DialogResult = true;
            Close();
        }

        private void Cancel_Click(object sender, RoutedEventArgs e)
        {
            DialogResult = false;
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
    }
}