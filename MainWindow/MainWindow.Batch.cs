using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Threading;
using TERMINAL.Models;

namespace TERMINAL
{
    public partial class MainWindow
    {
        private void AddBatchJob_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) return;

            var job = new TemperatureJob
            {
                SetPointText = "25",
                StabilizationMinutesText = "120",
                CyclesText = "10",
                StartStepText = "1",
                IsEnabled = true,
                IsCalibrationMode = true
            };
            _batchJobs.Add(job);
            RenumberBatchJobs();
            SaveSettings();
            UpdateStartButtonMode();
        }

        private void RemoveBatchJob_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) return;

            if (sender is System.Windows.Controls.Button btn &&
                btn.Tag is TemperatureJob job)
            {
                job.PropertyChanged -= BatchJob_PropertyChanged;
                _batchJobs.Remove(job);
                RenumberBatchJobs();
                SaveSettings();
                UpdateStartButtonMode();
            }
        }

        private void ClearBatch_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning) return;

            foreach (var j in _batchJobs)
                j.PropertyChanged -= BatchJob_PropertyChanged;

            _batchJobs.Clear();
            RenumberBatchJobs();
            SaveSettings();
            UpdateStartButtonMode();
        }

        private void RenumberBatchJobs()
        {
            for (int i = 0; i < _batchJobs.Count; i++)
            {
                _batchJobs[i].IndexText = (i + 1) + ".";

                _batchJobs[i].PropertyChanged -= BatchJob_PropertyChanged;
                _batchJobs[i].PropertyChanged += BatchJob_PropertyChanged;
            }
        }

        private void BatchJob_PropertyChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (!_uiLoaded) return;

            if (e.PropertyName == nameof(TemperatureJob.IsEnabled) ||
                e.PropertyName == nameof(TemperatureJob.IsCalibrationMode))
            {
                UpdateStartButtonMode();
            }

            SaveSettings();
        }

        private void UpdateStartButtonMode()
        {
            if (StartButton == null) return;

            int activeJobs = _batchJobs.Count(j => j.IsEnabled);

            if (activeJobs > 0)
            {
                StartButton.Content = "СТАРТ (пакет)";
                StartButton.Background = StartButtonBatchBrush;
                StartButton.IsEnabled = true;
            }
            else
            {
                StartButton.Content = "СТАРТ (нет заданий)";
                StartButton.Background = StartButtonBatchBrush;
                StartButton.IsEnabled = true;
            }
        }

        private async Task RunBatchAsync()
        {
            if (_batchJobs.Count == 0)
            {
                MessageBox.Show("Список температур пуст. Добавьте хотя бы одну.");
                return;
            }

            _batchStopRequested = false;
            _stopRequested = false;

            // Валидация активных заданий
            for (int i = 0; i < _batchJobs.Count; i++)
            {
                var job = _batchJobs[i];
                if (!job.IsEnabled) continue;

                if (!float.TryParse(job.SetPointText?.Replace(',', '.'),
                    NumberStyles.Float, CultureInfo.InvariantCulture, out float sp))
                {
                    MessageBox.Show($"Задание #{i + 1}: некорректная уставка «{job.SetPointText}»");
                    return;
                }

                if (!int.TryParse(job.StabilizationMinutesText?.Trim(), out int mins) || mins < 0)
                {
                    MessageBox.Show($"Задание #{i + 1}: некорректное время стабилизации " +
                                    $"«{job.StabilizationMinutesText}» (нужно неотрицательное число минут)");
                    return;
                }

                job.SetPoint = sp;
                job.StabilizationMinutes = mins;

                if (!job.IsCalibrationMode)
                {
                    if (!int.TryParse(job.CyclesText?.Trim(), out int cycles) || cycles <= 0)
                    {
                        MessageBox.Show($"Задание #{i + 1}: некорректное число циклов " +
                                        $"«{job.CyclesText}» (нужно положительное число)");
                        return;
                    }
                    job.Cycles = cycles;
                    job.StartStep = 1;
                }
                else
                {
                    if (!int.TryParse(job.StartStepText?.Trim(), out int startStep) || startStep < 1)
                    {
                        MessageBox.Show($"Задание #{i + 1}: некорректный стартовый шаг " +
                                        $"«{job.StartStepText}» (нужно положительное число)");
                        return;
                    }

                    int stepsCount = 0;
                    if (_selectedTargetPressure != null &&
                        _stepsByTarget.TryGetValue(_selectedTargetPressure, out var steps))
                    {
                        stepsCount = steps.Length;
                    }

                    if (stepsCount > 0 && startStep > stepsCount)
                    {
                        MessageBox.Show($"Задание #{i + 1}: стартовый шаг {startStep} " +
                                        $"больше числа шагов в диапазоне «{_selectedTargetPressure}» ({stepsCount})");
                        return;
                    }

                    job.StartStep = startStep;
                    job.Cycles = 0;
                }
            }

            int enabledCount = _batchJobs.Count(j => j.IsEnabled);
            if (enabledCount == 0)
            {
                MessageBox.Show("Все задания отключены. Включите хотя бы одно.");
                return;
            }

            _batchRunning = true;
            StartBatchStatusTimer();

            SetExternalButtonsEnabled(false);
            StartButton.IsEnabled = false;
            AddBatchJobButton.IsEnabled = false;
            ClearBatchButton.IsEnabled = false;
            BatchList.IsEnabled = false;
            BatchTargetPressureCombo.IsEnabled = false;
            BatchTimerSetCombo.IsEnabled = false;
            BatchKosaCheckBox.IsEnabled = false;
            BatchKosaTimerBox.IsEnabled = false;

            foreach (var job in _batchJobs)
                job.IsEditable = false;

            try
            {
                int completedCount = 0;

                for (int i = 0; i < _batchJobs.Count; i++)
                {
                    if (_batchStopRequested || _stopRequested)
                    {
                        SetStatusWarning("Пакетный режим остановлен оператором");
                        return;
                    }

                    var job = _batchJobs[i];

                    if (!job.IsEnabled)
                    {
                        job.StatusText = "пропущено";
                        job.StatusColor = Brushes.Gray;
                        continue;
                    }

                    completedCount++;

                    // 1) Запись уставки
                    job.StatusText = "запись уставки";
                    job.StatusColor = Brushes.Orange;

                    SetStatus($"[Пакет {completedCount}/{enabledCount}] запись уставки " +
                              $"{job.SetPoint.ToString("0.##", CultureInfo.InvariantCulture)} °C в ST-351...");

                    bool writeOk = await WriteFurnaceSetPointAsync(
                        job.SetPoint, FurnaceIp, FurnacePort, FurnaceSetPointRegister);

                    if (!writeOk)
                    {
                        job.StatusText = "ошибка записи";
                        job.StatusColor = Brushes.Red;
                        SetStatusError($"[Пакет {completedCount}] не удалось записать уставку");
                        return;
                    }

                    _lastValidFurnaceSetPoint = job.SetPoint;
                    await RefreshFurnaceSetPointAsync();

                    ResetStabilizationTimer();

                    // 2) Стабилизация
                    if (job.StabilizationMinutes > 0)
                    {
                        job.StatusText = $"стабилизация {job.StabilizationMinutes} мин";
                        job.StatusColor = Brushes.Orange;

                        bool stable = await WaitForBatchStabilizationAsync(
                            job.StabilizationMinutes, completedCount, enabledCount);

                        if (!stable)
                        {
                            job.StatusText = "стабилизация прервана";
                            job.StatusColor = Brushes.Red;
                            SetStatusWarning($"[Пакет {completedCount}] стабилизация прервана");
                            return;
                        }
                    }
                    else
                    {
                        job.StatusText = "проверка допуска";
                        job.StatusColor = Brushes.Orange;

                        SetStatus($"[Пакет {completedCount}/{enabledCount}] стабилизация 0 мин — " +
                                  $"проверка попадания в допуск...");

                        bool inTolerance = await WaitForBatchTemperatureInToleranceAsync(
                            completedCount, enabledCount);

                        if (!inTolerance)
                        {
                            job.StatusText = "проверка прервана";
                            job.StatusColor = Brushes.Red;
                            SetStatusWarning($"[Пакет {completedCount}] проверка допуска прервана");
                            return;
                        }

                        SetStatus($"[Пакет {completedCount}/{enabledCount}] температура в допуске, " +
                                  $"стабилизация пропущена");
                    }

                    // 3) Запуск нужного режима
                    job.StatusText = "Идёт съём данных";
                    job.StatusColor = Brushes.Blue;

                    string modeName = job.IsCalibrationMode ? "Градуировка" : "Жамкать";
                    SetStatus($"[Пакет {completedCount}/{enabledCount}] запуск «{modeName}» " +
                              $"на {job.SetPoint.ToString("0.##", CultureInfo.InvariantCulture)} °C");

                    _batchRunDoneSignal = new TaskCompletionSource<bool>(
                        TaskCreationOptions.RunContinuationsAsynchronously);

                    if (job.IsCalibrationMode)
                    {
                        int startStep = job.StartStep;
                        _ = Dispatcher.InvokeAsync(() => RunCalibrationModeAsync(startStep));
                    }
                    else
                    {
                        int cycles = job.Cycles;
                        _ = Dispatcher.InvokeAsync(() => RunPumpForBatchAsync(cycles));
                    }

                    await _batchRunDoneSignal.Task;
                    _batchRunDoneSignal = null;

                    if (_batchStopRequested || _stopRequested)
                    {
                        SetStatusWarning("Пакетный режим остановлен оператором");
                        return;
                    }

                    job.StatusText = "завершено";
                    job.StatusColor = Brushes.Green;

                    SetStatus($"[Пакет {completedCount}/{enabledCount}] завершён");
                }

                SetStatusSuccess($"Пакетный режим завершён: обработано {completedCount} температур");
            }
            finally
            {
                _batchRunning = false;
                StopBatchStatusTimer();
                _batchRunDoneSignal = null;
                _batchStopRequested = false;

                StopBatchStatusTimer();

                StartButton.IsEnabled = true;
                AddBatchJobButton.IsEnabled = true;
                ClearBatchButton.IsEnabled = true;
                BatchTargetPressureCombo.IsEnabled = true;
                BatchTimerSetCombo.IsEnabled = true;
                BatchKosaCheckBox.IsEnabled = true;
                BatchKosaTimerBox.IsEnabled = true;
                SetExternalButtonsEnabled(true);
                BatchList.IsEnabled = true;

                foreach (var job in _batchJobs)
                    job.IsEditable = true;

                BatchProgressText.Text = "";
                UpdateStartButtonMode();

                UpdateStartButtonMode();
            }
        }

        private async Task RunPumpForBatchAsync(int cycles)
        {
            if (BatchTargetPressureCombo.SelectedItem == null)
            {
                MessageBox.Show("Не выбран диапазон давлений для «Жамкать»");
                return;
            }

            _selectedTargetPressure = BatchTargetPressureCombo.SelectedItem.ToString();

            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure!, out var steps))
            {
                MessageBox.Show($"Шаги не заданы для «{_selectedTargetPressure}»");
                return;
            }

            bool noConnection;
            lock (_streamLock) { noConnection = (_stream == null); }
            if (noConnection)
            {
                MessageBox.Show("Нет соединения с прибором.");
                return;
            }

            double maxPressure = steps.Max();
            string unitsCode = GetUnitsCode(_selectedTargetPressure!);
            string unitsLabel = GetUnitsLabel(_selectedTargetPressure);
            double atmoThreshold = GetAtmosphereThreshold(unitsCode);

            _isRunning = true;
            _stopRequested = false;
            _pauseRequested = false;

            _totalTimerStart = DateTime.Now;

            try
            {
                await RunPumpSequenceAsync(maxPressure, cycles, unitsCode, unitsLabel, atmoThreshold);
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

                _batchRunDoneSignal?.TrySetResult(true);
            }
        }

        private async Task<bool> WaitForBatchStabilizationAsync(int requiredMinutes, int jobIndex, int totalJobs)
        {
            var inRangeSince = DateTime.MinValue;
            var outOfRangeSince = DateTime.MinValue;

            while (true)
            {
                if (_batchStopRequested || _stopRequested) return false;

                await RefreshFurnaceCurrentTempAsync();
                await RefreshFurnaceSetPointAsync();

                string spTxt = FurnaceSetPointBox?.Text?.Replace(',', '.') ?? "";
                string tTxt = FurnaceCurrentTempText?.Text ?? "";

                int idx = tTxt.IndexOf(" °C", StringComparison.Ordinal);
                if (idx >= 0) tTxt = tTxt.Substring(0, idx);
                tTxt = tTxt.Trim().TrimStart('+').Replace(',', '.');

                if (!double.TryParse(spTxt, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double setPoint) ||
                    !double.TryParse(tTxt, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double currentTemp))
                {
                    await Task.Delay(1000);
                    continue;
                }

                bool inRange = Math.Abs(currentTemp - setPoint) <= FurnaceStabilizationTolerance;

                if (inRange)
                {
                    outOfRangeSince = DateTime.MinValue;

                    if (inRangeSince == DateTime.MinValue)
                        inRangeSince = DateTime.Now;

                    var elapsed = DateTime.Now - inRangeSince;
                    var remaining = TimeSpan.FromMinutes(requiredMinutes) - elapsed;
                    if (remaining < TimeSpan.Zero) remaining = TimeSpan.Zero;

                    BatchProgressText.Text =
                        $"Задание {jobIndex}/{totalJobs}: стабилизация " +
                        $"{setPoint.ToString("0.##", CultureInfo.InvariantCulture)} °C, " +
                        $"осталось {remaining.Hours:D2}:{remaining.Minutes:D2}:{remaining.Seconds:D2}";

                    if (elapsed.TotalMinutes >= requiredMinutes)
                        return true;
                }
                else
                {
                    if (outOfRangeSince == DateTime.MinValue)
                        outOfRangeSince = DateTime.Now;

                    var outOfRange = DateTime.Now - outOfRangeSince;
                    if (outOfRange.TotalSeconds >= StabilizationOutOfRangeGraceSeconds)
                    {
                        inRangeSince = DateTime.MinValue;
                        outOfRangeSince = DateTime.MinValue;
                        BatchProgressText.Text =
                            $"Задание {jobIndex}/{totalJobs}: " +
                            $"температура вне диапазона — таймер стабилизации сброшен";
                    }
                    else
                    {
                        BatchProgressText.Text =
                            $"Задание {jobIndex}/{totalJobs}: " +
                            $"температура вне диапазона " +
                            $"({outOfRange.TotalSeconds:F0} сек из {StabilizationOutOfRangeGraceSeconds})";
                    }
                }

                await Task.Delay(1000);
            }
        }

        private async Task<bool> WaitForBatchTemperatureInToleranceAsync(int jobIndex, int totalJobs)
        {
            BatchProgressText.Text =
                $"Задание {jobIndex}/{totalJobs}: ожидание попадания в допуск...";

            while (true)
            {
                if (_batchStopRequested || _stopRequested) return false;

                await RefreshFurnaceCurrentTempAsync();
                await RefreshFurnaceSetPointAsync();

                string spTxt = FurnaceSetPointBox?.Text?.Replace(',', '.') ?? "";
                string tTxt = FurnaceCurrentTempText?.Text ?? "";

                int idx = tTxt.IndexOf(" °C", StringComparison.Ordinal);
                if (idx >= 0) tTxt = tTxt.Substring(0, idx);
                tTxt = tTxt.Trim().TrimStart('+').Replace(',', '.');

                if (!double.TryParse(spTxt, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double setPoint) ||
                    !double.TryParse(tTxt, NumberStyles.Float,
                        CultureInfo.InvariantCulture, out double currentTemp))
                {
                    BatchProgressText.Text =
                        $"Задание {jobIndex}/{totalJobs}: ожидание данных от печи...";
                    await Task.Delay(1000);
                    continue;
                }

                bool inRange = Math.Abs(currentTemp - setPoint) <= FurnaceStabilizationTolerance;

                if (inRange)
                {
                    BatchProgressText.Text =
                        $"Задание {jobIndex}/{totalJobs}: температура в допуске " +
                        $"({currentTemp.ToString("0.000", CultureInfo.InvariantCulture)} °C)";
                    return true;
                }

                BatchProgressText.Text =
                    $"Задание {jobIndex}/{totalJobs}: ожидание допуска — " +
                    $"текущая {currentTemp.ToString("0.000", CultureInfo.InvariantCulture)} °C, " +
                    $"уставка {setPoint.ToString("0.##", CultureInfo.InvariantCulture)} °C";

                await Task.Delay(1000);
            }
        }

        private void BatchKosaCheckBox_Changed(object sender, RoutedEventArgs e)
        {
            if (!_uiLoaded) return;
            _batchKosaEnabled = BatchKosaCheckBox.IsChecked == true;

            foreach (var item in _stepItems)
            {
                if (item.KosaCounterText == "0/3" || item.KosaCounterText == "0/1")
                    item.KosaCounterText = _batchKosaEnabled ? "0/3" : "0/1";
            }

            UpdateKosaBadge();
            SaveSettings();
        }

        private void BatchKosaTimerBox_PreviewTextInput(object sender, System.Windows.Input.TextCompositionEventArgs e)
        {
            e.Handled = !e.Text.All(char.IsDigit);
        }

        private void BatchKosaTimerBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (BatchKosaTimerBox == null) return;

            string val = BatchKosaTimerBox.Text?.Trim() ?? "";
            if (int.TryParse(val, out int v) && v > 0)
            {
                _batchKosaTimerSeconds = v;
                UpdateKosaBadge();
                SaveSettings();
            }
        }

        private void BatchTargetPressureCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (BatchTargetPressureCombo == null) return;
            if (StepsList == null) return;

            if (BatchTargetPressureCombo.SelectedItem == null)
            {
                _selectedTargetPressure = null;
                StepsList.ItemsSource = null;
                return;
            }

            _selectedTargetPressure = BatchTargetPressureCombo.SelectedItem.ToString();
            _currentStepIndex = 0;
            _completedSteps = 0;
            ShowSteps();
            UpdateMaxMinFromRange();
            SaveSettings();
        }

        private void BatchTimerSetCombo_SelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            if (BatchTimerSetCombo == null) return;

            RecalcStepTimers();
            SaveSettings();
        }

        private void ShowSteps()
        {
            if (_selectedTargetPressure == null) return;
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure, out var values))
            {
                _stepItems.Clear();
                StepsList.ItemsSource = null;
                return;
            }

            bool longTimers = BatchTimerSetCombo.SelectedIndex == 1;
            string unitsLabel = GetUnitsLabel(_selectedTargetPressure);

            _stepItems = new ObservableCollection<StepItem>();

            for (int i = 0; i < values.Length; i++)
            {
                int dur = GetStepDuration(values, i, longTimers);
                _stepItems.Add(new StepItem
                {
                    IndexText = (i + 1) + ".",
                    ValueNumber = values[i].ToString(CultureInfo.InvariantCulture),
                    ValueUnits = unitsLabel,
                    TimerText = dur.ToString(),
                    ArrowText = "  ",
                    StatusMark = "☐",
                    KosaCounterText = _batchKosaEnabled ? "0/3" : "0/1",
                    PlannedDuration = dur
                });
            }

            bool isLong = unitsLabel.Contains("кгс");

            StepsList.ItemTemplate = (DataTemplate)FindResource(
                isLong ? "LongUnitsTemplate" : "ShortUnitsTemplate");

            StepsHeaderShort.Visibility = isLong ? Visibility.Collapsed : Visibility.Visible;
            StepsHeaderLong.Visibility = isLong ? Visibility.Visible : Visibility.Collapsed;

            StepsList.ItemsSource = _stepItems;
            UpdateStepCounter();
        }

        private void RecalcStepTimers()
        {
            if (_selectedTargetPressure == null) return;
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure, out var values)) return;
            if (_stepItems.Count != values.Length) return;

            bool longTimers = BatchTimerSetCombo.SelectedIndex == 1;
            for (int i = 0; i < _stepItems.Count; i++)
            {
                int dur = GetStepDuration(values, i, longTimers);
                _stepItems[i].TimerText = dur.ToString();
                _stepItems[i].PlannedDuration = dur;
            }
        }

        private void UpdateMaxMinFromRange()
        {
            if (_selectedTargetPressure == null) return;
            if (!_stepsByTarget.TryGetValue(_selectedTargetPressure, out var steps))
                return;

            string unitsLabel = GetUnitsLabel(_selectedTargetPressure);
            double maxValue = steps.Max();
            double minValue = 0;

            MaxValueText.Text = maxValue.ToString(CultureInfo.InvariantCulture);
            MaxUnitsText.Text = unitsLabel;
            MinValueText.Text = minValue.ToString(CultureInfo.InvariantCulture);
            MinUnitsText.Text = unitsLabel;
        }

        private void ResetStepsToInitial()
        {
            _completedSteps = 0;
            _currentStepIndex = 0;

            foreach (var item in _stepItems)
            {
                item.StatusMark = "☐";
                item.ArrowText = "  ";
                item.TimerText = item.PlannedDuration.ToString();
                item.KosaCounterText = _batchKosaEnabled ? "0/3" : "0/1";
            }

            UpdateStepCounter();
        }

        private void ResetStepTimers()
        {
            foreach (var item in _stepItems)
            {
                item.TimerText = item.PlannedDuration.ToString();
                item.ArrowText = "  ";
                item.KosaCounterText = _batchKosaEnabled ? "0/3" : "0/1";
            }
            UpdateStepCounter();
        }

        private void UpdateStepCounter()
        {
            if (_selectedTargetPressure == null || _stepItems.Count == 0)
            {
                StepCounterText.Text = "Шаг: 0/0";
                return;
            }

            int currentStep = 0;
            for (int i = 0; i < _stepItems.Count; i++)
            {
                if (_stepItems[i].ArrowText == "←")
                {
                    currentStep = i + 1;
                    break;
                }
            }

            if (currentStep == 0)
            {
                int completed = _stepItems.Count(x => x.StatusMark == "☑");
                StepCounterText.Text = $"Шаг: {completed}/{_stepItems.Count}";
            }
            else
            {
                StepCounterText.Text = $"Шаг: {currentStep}/{_stepItems.Count}";
            }
        }

        private void AskForStartupMode()
        {
            var dlg = new Window
            {
                Title = "Режим запуска",
                Width = 420,
                Height = 200,
                WindowStartupLocation = WindowStartupLocation.CenterScreen,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow,
                Owner = this,
                Background = new SolidColorBrush(Color.FromRgb(0xF0, 0xF0, 0xF0))
            };

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var title = new System.Windows.Controls.TextBlock
            {
                Text = "В каком режиме запустить программу?",
                FontSize = 14,
                FontWeight = FontWeights.Bold,
                Margin = new Thickness(0, 0, 0, 10)
            };
            System.Windows.Controls.Grid.SetRow(title, 0);
            grid.Children.Add(title);

            var info = new System.Windows.Controls.TextBlock
            {
                Text = "Рабочая — порт 9990\n" +
                       "Тестовая — порт 9999 (для параллельного запуска)",
                FontSize = 12,
                Foreground = Brushes.Gray,
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 15)
            };
            System.Windows.Controls.Grid.SetRow(info, 1);
            grid.Children.Add(info);

            var buttonsPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var workBtn = new System.Windows.Controls.Button
            {
                Content = "Рабочая (9990)",
                Width = 140,
                Height = 32,
                Margin = new Thickness(0, 0, 8, 0),
                Background = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xD0)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            var testBtn = new System.Windows.Controls.Button
            {
                Content = "Тестовая (9999)",
                Width = 140,
                Height = 32,
                Background = new SolidColorBrush(Color.FromRgb(0xB0, 0xA0, 0x50)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };

            bool isWork = true;

            workBtn.Click += (s, e) => { isWork = true; dlg.Close(); };
            testBtn.Click += (s, e) => { isWork = false; dlg.Close(); };

            buttonsPanel.Children.Add(workBtn);
            buttonsPanel.Children.Add(testBtn);
            System.Windows.Controls.Grid.SetRow(buttonsPanel, 2);
            grid.Children.Add(buttonsPanel);

            dlg.Content = grid;
            dlg.ShowDialog();

            BroadcastPort = isWork ? 9990 : 9999;
        }

        private void UpdateKosaBadge()
        {
            if (KosaBadge == null || KosaBadgeText == null) return;

            if (_batchKosaEnabled)
            {
                KosaBadge.Visibility = Visibility.Visible;
                KosaBadgeText.Text = $"КОСА 3× ({_batchKosaTimerSeconds} сек)";
                KosaBadge.ToolTip = $"КОСА включена: 3 съёма на точку, пауза {_batchKosaTimerSeconds} сек";
            }
            else
            {
                KosaBadge.Visibility = Visibility.Collapsed;
            }
        }

        private void StartBatchStatusTimer()
        {
            if (_batchStatusTimer != null) return;

            _batchStatusTimer = new DispatcherTimer
            {
                Interval = TimeSpan.FromMilliseconds(500)
            };
            _batchStatusTimer.Tick += (s, e) => UpdateBatchStatusPanel();
            _batchStatusTimer.Start();

            // Показать панель
            if (BatchStatusPanel != null)
                BatchStatusPanel.Visibility = Visibility.Visible;

            // Сбросить цвета
            if (BatchCurrentJobText != null) BatchCurrentJobText.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x4A, 0x6B));
            if (BatchCurrentTempText != null) BatchCurrentTempText.Foreground = new SolidColorBrush(Color.FromRgb(0x2E, 0x4A, 0x6B));
            if (BatchDeltaText != null) BatchDeltaText.Foreground = Brushes.Gray;
        }
        private void StopBatchStatusTimer()
        {
            _batchStatusTimer?.Stop();
            _batchStatusTimer = null;

            if (BatchStatusPanel != null)
                BatchStatusPanel.Visibility = Visibility.Collapsed;

            if (BatchCurrentJobText != null) BatchCurrentJobText.Text = "—";
            if (BatchCurrentTempText != null) BatchCurrentTempText.Text = "— °C";
            if (BatchDeltaText != null) BatchDeltaText.Text = "—";
        }
        private void UpdateBatchStatusPanel()
        {
            if (!_batchRunning) return;
            if (BatchCurrentJobText == null || BatchCurrentTempText == null || BatchDeltaText == null) return;

            // 1. Номер текущего задания
            int activeIndex = -1;
            int activeCount = 0;
            int totalActive = 0;

            for (int i = 0; i < _batchJobs.Count; i++)
            {
                if (!_batchJobs[i].IsEnabled) continue;
                totalActive++;

                // Задание считается текущим, если оно в статусе работы
                string st = _batchJobs[i].StatusText ?? "";
                if (st.Contains("запись") || st.Contains("стабилизация") ||
                    st.Contains("проверка") || st.Contains("Идёт съём"))
                {
                    if (activeIndex < 0)
                    {
                        activeIndex = i;
                        activeCount = totalActive;
                    }
                }
            }

            if (activeIndex >= 0)
            {
                var job = _batchJobs[activeIndex];
                string sp = job.SetPointText ?? "—";
                BatchCurrentJobText.Text = $"{activeCount}/{totalActive}  →  {sp} °C";
            }
            else
            {
                BatchCurrentJobText.Text = "—";
            }

            // 2. Текущая температура
            string tTxt = FurnaceCurrentTempText?.Text ?? "";
            int idx = tTxt.IndexOf(" °C", StringComparison.Ordinal);
            if (idx >= 0) tTxt = tTxt.Substring(0, idx);
            tTxt = tTxt.Trim().TrimStart('+').Replace(',', '.');

            if (!double.TryParse(tTxt, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double currentTemp))
            {
                BatchCurrentTempText.Text = "— °C";
                BatchDeltaText.Text = "—";
                BatchDeltaText.Foreground = Brushes.Gray;
                return;
            }

            BatchCurrentTempText.Text = currentTemp.ToString("0.00", CultureInfo.InvariantCulture) + " °C";

            // 3. Цель — из активного задания (парсим SetPointText)
            double? setPoint = null;
            if (activeIndex >= 0)
            {
                string spTxt = _batchJobs[activeIndex].SetPointText?.Replace(',', '.') ?? "";
                if (double.TryParse(spTxt, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double sp))
                {
                    setPoint = sp;
                }
            }

            if (!setPoint.HasValue)
            {
                // Если активного задания нет — берём уставку из печи
                string spBox = FurnaceSetPointBox?.Text?.Replace(',', '.') ?? "";
                if (double.TryParse(spBox, NumberStyles.Float,
                    CultureInfo.InvariantCulture, out double spBoxV))
                {
                    setPoint = spBoxV;
                }
            }

            if (!setPoint.HasValue)
            {
                BatchDeltaText.Text = "—";
                BatchDeltaText.Foreground = Brushes.Gray;
                return;
            }

            // 4. Δ
            double delta = currentTemp - setPoint.Value;
            string sign = delta >= 0 ? "+" : "";
            BatchDeltaText.Text = $"{sign}{delta.ToString("0.00", CultureInfo.InvariantCulture)} °C";

            // Цветовая индикация
            double abs = Math.Abs(delta);
            if (abs <= FurnaceStabilizationTolerance)          // ±0.1 °C
                BatchDeltaText.Foreground = new SolidColorBrush(Color.FromRgb(0x30, 0xA0, 0x30));  // зелёный
            else if (abs <= 1.0)                                // ±1 °C
                BatchDeltaText.Foreground = new SolidColorBrush(Color.FromRgb(0xE0, 0xA0, 0x30));  // оранжевый
            else                                                // > 1 °C
                BatchDeltaText.Foreground = new SolidColorBrush(Color.FromRgb(0xD0, 0x50, 0x50));  // красный
        }

        // ═══════════════════════════════════════════════════════════════════
        // ПРЕСЕТЫ ПАКЕТА
        // ═══════════════════════════════════════════════════════════════════
        private void RefreshPresetCombo()
        {
            if (BatchPresetCombo == null) return;

            BatchPresetCombo.ItemsSource = _batchPresets;
            if (_batchPresets.Count > 0)
                BatchPresetCombo.SelectedIndex = 0;
        }

        private void LoadPreset_Click(object sender, RoutedEventArgs e)
        {
            if (_batchRunning) return;
            if (BatchPresetCombo?.SelectedItem is not BatchPreset preset) return;

            // Подтверждение, если текущий список не пуст
            if (_batchJobs.Count > 0)
            {
                var r = MessageBox.Show(
                    $"Заменить текущий список заданий ({_batchJobs.Count} шт.) " +
                    $"на пресет «{preset.Name}» ({preset.Jobs.Count} шт.)?",
                    "Загрузка пресета",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;
            }

            foreach (var j in _batchJobs)
                j.PropertyChanged -= BatchJob_PropertyChanged;

            _batchJobs.Clear();

            foreach (var dto in preset.Jobs)
            {
                _batchJobs.Add(new TemperatureJob
                {
                    SetPointText = dto.SetPoint,
                    StabilizationMinutesText = dto.StabilizationMinutes,
                    IsEnabled = dto.IsEnabled,
                    IsCalibrationMode = dto.IsCalibrationMode,
                    CyclesText = dto.Cycles,
                    StartStepText = dto.StartStep
                });
            }

            RenumberBatchJobs();
            SaveSettings();
            UpdateStartButtonMode();
            AppendLog($"Загружен пресет «{preset.Name}» ({preset.Jobs.Count} заданий)", Brushes.Green);
        }

        private void SavePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_batchRunning) return;
            if (_batchJobs.Count == 0)
            {
                MessageBox.Show("Список заданий пуст — нечего сохранять.");
                return;
            }

            // Простой диалог ввода имени
            var dlg = new Window
            {
                Title = "Сохранить пресет",
                Width = 400,
                Height = 160,
                WindowStartupLocation = WindowStartupLocation.CenterOwner,
                Owner = this,
                ResizeMode = ResizeMode.NoResize,
                WindowStyle = WindowStyle.ToolWindow
            };

            var grid = new System.Windows.Controls.Grid { Margin = new Thickness(15) };
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });
            grid.RowDefinitions.Add(new System.Windows.Controls.RowDefinition { Height = GridLength.Auto });

            var lbl = new System.Windows.Controls.TextBlock
            {
                Text = "Имя пресета:",
                FontSize = 12,
                Margin = new Thickness(0, 0, 0, 6)
            };
            System.Windows.Controls.Grid.SetRow(lbl, 0);
            grid.Children.Add(lbl);

            var nameBox = new System.Windows.Controls.TextBox
            {
                Height = 26,
                FontSize = 12,
                Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center,
                Text = $"Пресет {DateTime.Now:yyyy-MM-dd HH-mm}"
            };
            System.Windows.Controls.Grid.SetRow(nameBox, 1);
            grid.Children.Add(nameBox);

            var btnPanel = new System.Windows.Controls.StackPanel
            {
                Orientation = System.Windows.Controls.Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right,
                Margin = new Thickness(0, 10, 0, 0)
            };

            var okBtn = new System.Windows.Controls.Button
            {
                Content = "Сохранить",
                Width = 100,
                Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(0x50, 0x90, 0xD0)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0),
                Margin = new Thickness(0, 0, 6, 0)
            };
            var cancelBtn = new System.Windows.Controls.Button
            {
                Content = "Отмена",
                Width = 80,
                Height = 28,
                Background = new SolidColorBrush(Color.FromRgb(0x80, 0x80, 0x80)),
                Foreground = Brushes.White,
                FontWeight = FontWeights.Bold,
                BorderThickness = new Thickness(0)
            };
            btnPanel.Children.Add(okBtn);
            btnPanel.Children.Add(cancelBtn);
            System.Windows.Controls.Grid.SetRow(btnPanel, 2);
            grid.Children.Add(btnPanel);

            bool ok = false;
            okBtn.Click += (s, ev) => { ok = true; dlg.Close(); };
            cancelBtn.Click += (s, ev) => dlg.Close();

            dlg.Content = grid;
            nameBox.Focus();
            nameBox.SelectAll();
            dlg.ShowDialog();

            if (!ok) return;

            string name = nameBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(name))
            {
                MessageBox.Show("Имя пресета не может быть пустым.");
                return;
            }

            // Проверим, нет ли пресета с таким именем
            var existing = _batchPresets.FirstOrDefault(p =>
                string.Equals(p.Name, name, StringComparison.OrdinalIgnoreCase));

            if (existing != null)
            {
                var r = MessageBox.Show(
                    $"Пресет «{name}» уже существует. Перезаписать?",
                    "Подтверждение",
                    MessageBoxButton.YesNo,
                    MessageBoxImage.Question);
                if (r != MessageBoxResult.Yes) return;

                _batchPresets.Remove(existing);
            }

            // Снимок текущего списка
            var preset = new BatchPreset
            {
                Name = name,
                CreatedAt = DateTime.Now,
                Jobs = _batchJobs.Select(j => new BatchJobDto
                {
                    SetPoint = j.SetPointText,
                    StabilizationMinutes = j.StabilizationMinutesText,
                    IsEnabled = j.IsEnabled,
                    IsCalibrationMode = j.IsCalibrationMode,
                    Cycles = j.CyclesText,
                    StartStep = j.StartStepText
                }).ToList()
            };

            // Добавляем в начало и обрезаем до 10
            _batchPresets.Insert(0, preset);
            while (_batchPresets.Count > 10)
                _batchPresets.RemoveAt(_batchPresets.Count - 1);

            RefreshPresetCombo();
            BatchPresetCombo.SelectedItem = preset;
            SaveSettings();
            AppendLog($"Сохранён пресет «{name}» ({preset.Jobs.Count} заданий)", Brushes.Green);
        }

        private void DeletePreset_Click(object sender, RoutedEventArgs e)
        {
            if (_batchRunning) return;
            if (BatchPresetCombo?.SelectedItem is not BatchPreset preset) return;

            var r = MessageBox.Show(
                $"Удалить пресет «{preset.Name}»?",
                "Подтверждение",
                MessageBoxButton.YesNo,
                MessageBoxImage.Warning);
            if (r != MessageBoxResult.Yes) return;

            _batchPresets.Remove(preset);
            RefreshPresetCombo();
            SaveSettings();
            AppendLog($"Удалён пресет «{preset.Name}»", Brushes.Gray);
        }
    }
}