using System;
using System.IO;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Media;
using TERMINAL.Models;

namespace TERMINAL
{
    public partial class MainWindow
    {
        private async void StartButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning) { MessageBox.Show("Программа уже запущена."); return; }
            if (_batchRunning) { MessageBox.Show("Пакетный режим уже идёт."); return; }

            int activeJobs = _batchJobs.Count(j => j.IsEnabled);
            if (activeJobs == 0)
            {
                MessageBox.Show("Нет активных заданий в пакетном режиме. Добавьте хотя бы одно.");
                return;
            }

            _stopRequested = false;
            _batchStopRequested = false;
            _pauseRequested = false;

            await RunBatchAsync();
        }
        private void PauseButton_Click(object sender, RoutedEventArgs e)
        {
            if (!_isRunning) return;

            if (_pauseRequested)
            {
                _pauseRequested = false;
                PauseButton.Content = "ПАУЗА";
                PauseButton.Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50));
                SetStatus("Продолжение работы...");
            }
            else
            {
                _pauseRequested = true;
                PauseButton.Content = "ПРОДОЛЖИТЬ";
                PauseButton.Background = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xD0));
                SetStatusWarning("ПАУЗА");
            }
        }
        private async void StopButton_Click(object sender, RoutedEventArgs e)
        {
            if (_batchRunning)
            {
                _batchStopRequested = true;
                _stopRequested = true;
                SetStatusWarning("Пакетный режим прерывается...");
                return;
            }

            _stopRequested = true;
            _pauseRequested = false;

            ResetStepTimers();

            await SendCommandNoWaitAsync("set_mode=stop;");
            SetStatusError("АВАРИЙНАЯ ОСТАНОВКА");
        }
        private async void VentButton_Click(object sender, RoutedEventArgs e)
        {
            if (_batchRunning)
            {
                if (MessageBox.Show("Прервать пакетный режим и сбросить?",
                    "Подтверждение", MessageBoxButton.YesNo, MessageBoxImage.Warning)
                    != MessageBoxResult.Yes) return;

                _batchStopRequested = true;
                _stopRequested = true;
                SetStatusWarning("Пакетный режим прерывается...");
                return;
            }

            if (_isRunning)
            {
                if (MessageBox.Show("Прервать и сбросить?", "Подтверждение",
                    MessageBoxButton.YesNo, MessageBoxImage.Warning) != MessageBoxResult.Yes) return;

                _stopRequested = true;
                _pauseRequested = false;

                ResetStepTimers();
            }
            string? resp = await SendCommandAsync("set_mode=vent;");
            SetStatus($"Сброс отправлен: {resp}");
        }
        private async void CalibrateStocksButton_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning)
            {
                MessageBox.Show("Идёт градуировка/жамкать. Дождитесь завершения.");
                return;
            }

            if (MessageBox.Show(
                    "Запустить калибровку штоков АГК?\n\n" +
                    "Во время калибровки штоки будут двигаться в крайние положения.",
                    "Калибровка штоков",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Warning) != MessageBoxResult.Yes)
                return;

            CalibrateStocksButton.IsEnabled = false;
            try
            {
                SetStatus("Отправка команды калибровки штоков...", false);
                string? resp = await SendCommandAsync("set_cal_stocks=start;");

                if (resp != null && resp.Contains("ok", StringComparison.OrdinalIgnoreCase))
                    SetStatusSuccess($"Калибровка штоков запущена: {resp}");
                else if (resp != null)
                    SetStatusWarning($"Ответ АГК: {resp}");
                else
                    SetStatusError("Нет ответа от АГК на set_cal_stocks=start;");
            }
            finally
            {
                CalibrateStocksButton.IsEnabled = true;
            }
        }
        private async Task RunCalibrationModeAsync(int startStep)
        {
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure!, out var steps))
            {
                MessageBox.Show($"Для «{_selectedTargetPressure}» не заданы шаги.");
                return;
            }

            bool noConnection;
            lock (_streamLock) { noConnection = (_stream == null); }
            if (noConnection)
            {
                MessageBox.Show("Нет соединения с прибором.");
                return;
            }

            if (startStep < 1 || startStep > steps.Length)
            {
                MessageBox.Show($"Некорректный номер шага: {startStep}\n" +
                    $"Допустимо от 1 до {steps.Length}.");
                return;
            }

            bool longTimers = BatchTimerSetCombo.SelectedIndex == 1;

            _isRunning = true;
            _stopRequested = false;
            _pauseRequested = false;
            _batchStopRequested = false;
            _completedSteps = startStep - 1;
            _currentStepIndex = startStep - 1;

            PauseButton.IsEnabled = true;
            PauseButton.Content = "ПАУЗА";
            PauseButton.Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50));

            for (int i = 0; i < _stepItems.Count; i++)
            {
                if (i < startStep - 1)
                {
                    _stepItems[i].StatusMark = "☑";
                    _stepItems[i].TimerText = "0";
                    _stepItems[i].ArrowText = "  ";
                    _stepItems[i].KosaCounterText = _batchKosaEnabled ? "3/3" : "1/1";
                }
                else
                {
                    _stepItems[i].StatusMark = "☐";
                    _stepItems[i].TimerText = _stepItems[i].PlannedDuration.ToString();
                    _stepItems[i].ArrowText = "  ";
                    _stepItems[i].KosaCounterText = _batchKosaEnabled ? "0/3" : "0/1";
                }
            }
            UpdateStepCounter();

            _totalTimerStart = DateTime.Now;

            bool programCompleted = false;

            try
            {
                await RunSequenceAsync(steps, longTimers, startStep - 1);
                programCompleted = true;
            }
            catch (Exception ex)
            {
                StatusText.Text = $"Ошибка: {ex.Message}";
                StatusText.Foreground = Brushes.Red;
            }
            finally
            {
                _isRunning = false;
                _pauseRequested = false;

                PauseButton.IsEnabled = false;
                PauseButton.Content = "ПАУЗА";
                PauseButton.Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50));

                if (programCompleted && !_stopRequested)
                {
                    bool saved = await SaveDataCoreAsync(silent: true);

                    if (saved)
                    {
                        bool fileExists = await WaitForSavedFileAsync(_lastSavedFilePath, 15000);

                        if (fileExists)
                        {
                            SetStatusSuccess(
                                $"Файл подтверждён на диске: {Path.GetFileName(_lastSavedFilePath)}");

                            await ClearDataCoreAsync(silent: true);

                            // ─── Автозапуск Excel ───
                            bool autoExcelEnabled = AutoExcelCheckBox?.IsChecked == true;

                            if (autoExcelEnabled && !_stopRequested && !_batchStopRequested)
                            {
                                try
                                {
                                    AppendLog("Excel: автозапуск после градуировки", Brushes.Blue);
                                    RunExcelMacroInternal(silent: true);
                                }
                                catch (Exception ex)
                                {
                                    AppendLog($"Автозапуск Excel: {ex.Message}", Brushes.OrangeRed);
                                }
                            }
                            else if (!autoExcelEnabled)
                            {
                                AppendLog("Excel: автозапуск пропущен (галочка снята)", Brushes.Gray);
                            }
                            // ─────────────────────────
                        }
                        else
                        {
                            SetStatusWarning(
                                $"Файл не найден на диске: {_lastSavedFilePath} — очистка данных отменена");
                        }
                    }
                }

                ResetStepsToInitial();

                List<ConnectedClient> clientsSnapshot;
                lock (_clientsLock)
                    clientsSnapshot = _connectedClients.ToList();
                foreach (var c in clientsSnapshot)
                {
                    if (!c.IsOffline) c.SetStatusReady();
                }

                _batchRunDoneSignal?.TrySetResult(true);
            }
        }
        private async Task RunSequenceAsync(double[] steps, bool longTimers, int startIndex)
        {
            string unitsCode = GetUnitsCode(_selectedTargetPressure!);
            double atmoThreshold = GetAtmosphereThreshold(unitsCode);

            SetStatus($"Установка единиц: {unitsCode}");
            bool unitsOk = await SendWithRetryAsync($"set_units={unitsCode};", "ok;");
            if (!unitsOk) { SetStatus("Не удалось установить единицы"); return; }

            string unitsLabel = GetUnitsLabel(_selectedTargetPressure!);
            _currentPressureUnitsLabel = unitsLabel;

            for (int i = startIndex; i < steps.Length; i++)
            {
                if (_stopRequested) { SetStatus("Остановлено оператором"); return; }
                if (!await WaitIfPausedAsync()) { SetStatus("Остановлено оператором"); return; }

                _currentStepIndex = i;

                for (int k = 0; k < _stepItems.Count; k++)
                    _stepItems[k].ArrowText = "  ";
                if (i < _stepItems.Count)
                    _stepItems[i].ArrowText = "←";

                UpdateStepCounter();

                int human = i + 1;
                double v = steps[i];
                int duration = GetStepDuration(steps, i, longTimers);

                if (IsTestPressureRange())
                {
                    SetStatus($"Шаг {human}: ТЕСТ — проверка атмосферного давления");

                    await RefreshPressureAndSetPointAsync();
                    double? currentP = _lastKnownPressure;
                    if (!currentP.HasValue || Math.Abs(currentP.Value) >= atmoThreshold)
                    {
                        SetStatus($"Шаг {human}: ТЕСТ — сброс до атмосферы (vent)");
                        bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                        if (!ventOk) return;

                        SetStatus($"Шаг {human}: ТЕСТ — ожидание снижения давления");
                        bool lowOk = await WaitUntilPressureBelowAsync(atmoThreshold, 300_000);
                        if (!lowOk) return;
                    }

                    SetStatus($"Шаг {human}: ТЕСТ — таймер {duration} сек");
                    await RunTimerWithStepAsync(duration, i, waitForState: false);

                    SetStatus($"Шаг {human}: ТЕСТ — команда съёма");
                    bool qT = await TakePointForStepAsync(i, waitForState: false);
                    if (!qT) return;

                    if (i < _stepItems.Count)
                    {
                        _stepItems[i].StatusMark = "☑";
                        _stepItems[i].ArrowText = "  ";
                    }
                    _completedSteps = i + 1;
                    UpdateStepCounter();
                    continue;
                }

                // ─── Первый шаг ───
                if (i == 0)
                {
                    if (v == 0)
                    {
                        SetStatus("Шаг 1: сброс до атмосферы (vent)");
                        bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                        if (!ventOk) return;

                        SetStatus($"Шаг 1: измерение {duration} сек");
                        await RunTimerWithStepAsync(duration, i, waitForState: false);

                        SetStatus("Шаг 1: команда съёма");
                        bool q1 = await TakePointForStepAsync(i, waitForState: false);
                        if (!q1) return;
                    }
                    else
                    {
                        SetStatus("Шаг 1: set_mode=control (1)");
                        bool m1 = await SendWithRetryAsync("set_mode=control;", "ok;");
                        if (!m1) return;

                        await SafeDelayAsync(1000);

                        SetStatus("Шаг 1: set_mode=control (2)");
                        bool m2 = await SendWithRetryAsync("set_mode=control;", "ok;");
                        if (!m2) return;

                        SetStatus($"Шаг 1: установка {v}");
                        bool setOk = await SendWithRetryAsync($"set_point={v};", "ok;");
                        if (!setOk) return;

                        SetStatus("Шаг 1: ожидание стабилизации");
                        bool stable = await WaitForStateInPointAsync();
                        if (!stable) return;

                        SetStatus($"Шаг 1: измерение {duration} сек");
                        await RunTimerWithStepAsync(duration, i, waitForState: true);

                        SetStatus("Шаг 1: команда съёма");
                        bool q1 = await TakePointForStepAsync(i, waitForState: true);
                        if (!q1) return;
                    }

                    if (i < _stepItems.Count)
                    {
                        _stepItems[i].StatusMark = "☑";
                        _stepItems[i].ArrowText = "  ";
                    }
                    _completedSteps = 1;
                    UpdateStepCounter();
                    continue;
                }

                // ─── Последний шаг ───
                if (i == steps.Length - 1)
                {
                    if (v == 0)
                    {
                        SetStatus($"Шаг {human}: сброс до атмосферы (vent)");
                        bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                        if (!ventOk) return;

                        SetStatus($"Шаг {human}: ожидание снижения давления");
                        bool lowOk = await WaitUntilPressureBelowAsync(atmoThreshold, 300_000);
                        if (!lowOk) return;
                    }
                    else
                    {
                        SetStatus($"Шаг {human}: set_mode=control (1)");
                        bool m1 = await SendWithRetryAsync("set_mode=control;", "ok;");
                        if (!m1) return;

                        await SafeDelayAsync(1000);

                        SetStatus($"Шаг {human}: set_mode=control (2)");
                        bool m2 = await SendWithRetryAsync("set_mode=control;", "ok;");
                        if (!m2) return;

                        SetStatus($"Шаг {human}/{steps.Length}: установка {v}");
                        bool setOk = await SendWithRetryAsync($"set_point={v};", "ok;");
                        if (!setOk) return;

                        SetStatus($"Шаг {human}: ожидание стабилизации");
                        bool stable = await WaitForStateInPointAsync();
                        if (!stable) return;
                    }

                    SetStatus($"Шаг {human}: измерение {duration} сек");
                    await RunTimerWithStepAsync(duration, i, waitForState: v != 0);

                    SetStatus($"Шаг {human}: команда съёма");
                    bool qLast = await TakePointForStepAsync(i, waitForState: v != 0);
                    if (!qLast) return;

                    if (i < _stepItems.Count)
                    {
                        _stepItems[i].StatusMark = "☑";
                        _stepItems[i].ArrowText = "  ";
                    }
                    _completedSteps = i + 1;
                    UpdateStepCounter();
                    continue;
                }

                // ─── Обычный шаг (середина) ───
                SetStatus($"Шаг {human}: set_mode=control (1)");
                bool mm1 = await SendWithRetryAsync("set_mode=control;", "ok;");
                if (!mm1) return;

                await SafeDelayAsync(1000);

                SetStatus($"Шаг {human}: set_mode=control (2)");
                bool mm2 = await SendWithRetryAsync("set_mode=control;", "ok;");
                if (!mm2) return;

                SetStatus($"Шаг {human}/{steps.Length}: установка {v}");
                bool setOk2 = await SendWithRetryAsync($"set_point={v};", "ok;");
                if (!setOk2) return;

                SetStatus($"Шаг {human}: ожидание стабилизации");
                bool stable2 = await WaitForStateInPointAsync();
                if (!stable2) return;

                SetStatus($"Шаг {human}: измерение {duration} сек");
                await RunTimerWithStepAsync(duration, i, waitForState: v != 0);

                SetStatus($"Шаг {human}: команда съёма");
                bool q = await TakePointForStepAsync(i, waitForState: v != 0);
                if (!q) return;

                if (i < _stepItems.Count)
                {
                    _stepItems[i].StatusMark = "☑";
                    _stepItems[i].ArrowText = "  ";
                }
                _completedSteps = i + 1;
                UpdateStepCounter();
            }

            // ─── Финальный сброс ───
            // Если последнее давление в программе ≠ 0 — сброс до атмосферы + уставка 0
            double lastPressure = steps[steps.Length - 1];
            if (lastPressure != 0)
            {
                SetStatus("Программа завершена — финальный сброс до атмосферы");
                bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                if (ventOk)
                {
                    SetStatus("Ожидание снижения давления");
                    await WaitUntilPressureBelowAsync(atmoThreshold, 300_000);

                    SetStatus("Уставка → 0");
                    await SendWithRetryAsync("set_point=0;", "ok;");
                }
            }

            SetStatus("Программа завершена");
        }
        private async Task RunPumpSequenceAsync(double maxPressure, int totalCycles, string unitsCode, string unitsLabel, double atmoThreshold)
        {
            MaxArrow.Text = "  ";
            MinArrow.Text = "  ";

            SetStatus($"Установка единиц: {unitsCode}");
            bool unitsOk = await SendWithRetryAsync($"set_units={unitsCode};", "ok;");
            if (!unitsOk) return;

            _currentPressureUnitsLabel = unitsLabel;

            for (int cycle = 1; cycle <= totalCycles; cycle++)
            {
                if (_stopRequested) return;
                if (!await WaitIfPausedAsync()) return;

                MaxArrow.Text = "  ";
                MinArrow.Text = "  ";

                MaxTimerText.Text = "300";
                MinTimerText.Text = "300";

                CyclesText.Text = $"Цикл: {cycle}/{totalCycles}";

                SetStatus($"[Цикл {cycle}] set_mode=control (1)");
                bool m1 = await SendWithRetryAsync("set_mode=control;", "ok;");
                if (!m1) return;

                await SafeDelayAsync(1000);

                SetStatus($"[Цикл {cycle}] set_mode=control (2)");
                bool m2 = await SendWithRetryAsync("set_mode=control;", "ok;");
                if (!m2) return;

                SetStatus($"[Цикл {cycle}] установка {maxPressure}");
                bool setOk = await SendWithRetryAsync(
                    $"set_point={maxPressure.ToString(CultureInfo.InvariantCulture)};", "ok;");
                if (!setOk) return;

                MaxArrow.Text = "→";

                SetStatus($"[Цикл {cycle}] ожидание стабилизации");
                bool stable = await WaitForStateInPointAsync();
                if (!stable) return;

                SetStatus($"[Цикл {cycle}] Выдерживаем максимальное давление");
                await RunTimerWithRightBlockAsync(300, true);

                SetStatus($"[Цикл {cycle}] сброс (vent)");
                bool ventOk = await SendWithRetryAsync("set_mode=vent;", "ok;");
                if (!ventOk) return;

                MaxArrow.Text = "  ";
                MinArrow.Text = "→";

                SetStatus($"[Цикл {cycle}] ожидание снижения");
                bool lowOk = await WaitUntilPressureBelowAsync(atmoThreshold, 300_000);
                if (!lowOk) return;

                SetStatus($"[Цикл {cycle}] Выдерживаем атмосферное давление");
                await RunTimerWithRightBlockAsync(300, false);
            }

            MaxArrow.Text = "  ";
            MinArrow.Text = "  ";
            MaxTimerText.Text = "";
            MinTimerText.Text = "";

            SetStatus($"«Жамкать» завершено ({totalCycles} циклов)");
        }
        private async Task RunTimerWithRightBlockAsync(int seconds, bool isMax)
        {
            int remaining = seconds;

            if (!isMax)
            {
                SetStatus($"Атмосфера: таймер запущен на {remaining} сек");

                while (remaining > 0)
                {
                    await RefreshPressureAndSetPointAsync();
                    string? stateResponse = await SendCommandAsync("get_state_in_point;");

                    if (stateResponse != null && stateResponse.Contains("ok;", StringComparison.Ordinal))
                    {
                        remaining--;
                        MaxTimerText.Text = remaining.ToString();
                    }

                    SetStatus($"Максимум: {remaining} сек");
                    await SafeDelayAsync(1000);
                }

                MinTimerText.Text = "0";
                return;
            }

            SetStatus($"Максимум: таймер запущен на {remaining} сек");

            while (remaining > 0)
            {
                await RefreshPressureAndSetPointAsync();
                string? stateResponse = await SendCommandAsync("get_state_in_point;");

                if (stateResponse != null && stateResponse.Contains("ok;", StringComparison.Ordinal))
                {
                    remaining--;
                    MaxTimerText.Text = remaining.ToString();
                }

                SetStatus($"Максимум: {remaining} сек", log: false);
                await SafeDelayAsync(1000);
            }

            MaxTimerText.Text = "0";
        }
        private async Task RunTimerWithStepAsync(int seconds, int stepIndex, bool waitForState)
        {
            int remaining = seconds;

            if (!waitForState)
            {
                // Один раз пишем в журнал
                SetStatus($"До начала съёма данных: {remaining} сек — таймер запущен");

                while (remaining > 0)
                {
                    remaining--;
                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();

                    // Обновляем только статус, без журнала
                    SetStatus($"До начала съёма данных: {remaining} сек", log: false);
                    await SafeDelayAsync(1000);
                }

                if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                    _stepItems[stepIndex].TimerText = "0";
                return;
            }

            bool firstOkReceived = false;

            SetStatus($"До начала съёма данных: таймер запущен на {remaining} сек");

            while (remaining > 0)
            {
                await RefreshPressureAndSetPointAsync();

                string? unitsResponse = await SendCommandAsync("get_units;");
                if (unitsResponse != null) UpdateUnits(unitsResponse);

                string? stateResponse = await SendCommandAsync("get_state_in_point;");

                if (stateResponse != null && stateResponse.Contains("ok;", StringComparison.Ordinal))
                {
                    firstOkReceived = true;
                    remaining--;
                    if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                        _stepItems[stepIndex].TimerText = remaining.ToString();
                }
                else if (stateResponse != null && stateResponse.Contains("wait;", StringComparison.Ordinal))
                {
                    if (firstOkReceived)
                    {
                        remaining += 30;
                        SetStatus($"Срыв! Таймер +30 сек. Осталось: {remaining}");
                        if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                            _stepItems[stepIndex].TimerText = remaining.ToString();
                    }
                    else
                    {
                        SetStatus("Ожидание стабилизации давления...");
                    }

                    while (true)
                    {
                        SetStatus($"До начала съёма данных: {remaining} сек", log: false);
                        await SafeDelayAsync(1000);

                        string? s = await SendCommandAsync("get_state_in_point;");
                        if (s != null && s.Contains("ok;", StringComparison.Ordinal))
                        {
                            firstOkReceived = true;
                            break;
                        }
                    }
                    continue;
                }
                await SafeDelayAsync(1000);
            }

            if (stepIndex >= 0 && stepIndex < _stepItems.Count)
                _stepItems[stepIndex].TimerText = "0";
        }
        private string GetUnitsCode(string? tp)
        {
            if (string.IsNullOrEmpty(tp)) return "kgs";
            if (tp.Contains("кгс/см²")) return "kgs";
            if (tp.Contains("МПа")) return "MPa";
            if (tp.Contains("кПа")) return "kPa";
            if (tp.Contains("бар")) return "bar";
            if (tp.Contains("PSI")) return "psi";
            return "kgs";
        }
        private string GetUnitsLabel(string? tp)
        {
            if (string.IsNullOrEmpty(tp)) return "";
            if (tp.Contains("кгс/см²")) return "кгс/см²";
            if (tp.Contains("МПа")) return "МПа";
            if (tp.Contains("кПа")) return "кПа";
            if (tp.Contains("бар")) return "бар";
            if (tp.Contains("PSI")) return "PSI";
            return "";
        }
        private bool IsTestPressureRange()
        {
            return _selectedTargetPressure == "ТЕСТ";
        }
        private double GetAtmosphereThreshold(string unitsCode) =>
            unitsCode switch
            {
                "kgs" => 1.0,
                "MPa" => 0.1,
                "kPa" => 100.0,
                "bar" => 1.0,
                "psi" => 14.7,
                _ => 1.0
            };
        private int GetStepDuration(double[] steps, int index, bool longTimers)
        {
            // ─── 1. Если для диапазона заданы кастомные таймеры — берём из словаря ───
            if (_selectedTargetPressure != null &&
                _timersByTarget.TryGetValue(_selectedTargetPressure, out var customTimers))
            {
                if (index >= 0 && index < customTimers.Length)
                    return customTimers[index];
            }

            // ─── 2. Иначе — стандартная логика ───
            if (BatchTimerSetCombo.SelectedIndex == 2)
                return 10;

            if (index == 0) return 10;
            double cur = steps[index];
            double prev = steps[index - 1];
            if (cur == 0) return longTimers ? 500 : 300;
            if (cur > prev) return longTimers ? 220 : 120;
            return longTimers ? 330 : 180;
        }
    }
}