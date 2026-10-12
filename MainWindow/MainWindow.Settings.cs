using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using TERMINAL.Models;

namespace TERMINAL
{
    public partial class MainWindow
    {
        private void LoadTargetPressures()
        {
            BatchTargetPressureCombo.Items.Add("100 кгс/см²");
            BatchTargetPressureCombo.Items.Add("200 кгс/см²");
            BatchTargetPressureCombo.Items.Add("250 кгс/см²");
            BatchTargetPressureCombo.Items.Add("300 кгс/см²");
            BatchTargetPressureCombo.Items.Add("350 кгс/см²");
            BatchTargetPressureCombo.Items.Add("350 кгс/см² (БИДТ)");
            BatchTargetPressureCombo.Items.Add("400 кгс/см²");
            BatchTargetPressureCombo.Items.Add("450 кгс/см²");
            BatchTargetPressureCombo.Items.Add("500 кгс/см²");
            BatchTargetPressureCombo.Items.Add("600 кгс/см²");
            BatchTargetPressureCombo.Items.Add("600 кгс/см² (13 точек)");
            BatchTargetPressureCombo.Items.Add("25 МПа");
            BatchTargetPressureCombo.Items.Add("60 МПа");
            BatchTargetPressureCombo.Items.Add("75 МПа");
            BatchTargetPressureCombo.Items.Add("80 МПа");
            BatchTargetPressureCombo.Items.Add("100 МПа");
            BatchTargetPressureCombo.Items.Add("110 МПа");
            BatchTargetPressureCombo.Items.Add("130 МПа");
            BatchTargetPressureCombo.Items.Add("138 МПа (БУРСЕРВИС)");
            BatchTargetPressureCombo.Items.Add("140 МПа");
            BatchTargetPressureCombo.Items.Add("160 МПа (БУРСЕРВИС)");
            BatchTargetPressureCombo.Items.Add("160 МПа (Schlumberger)");
            BatchTargetPressureCombo.Items.Add("100 МПа (New)");
            BatchTargetPressureCombo.SelectedIndex = 0;
        }

        private void LoadTimerSets()
        {
            BatchTimerSetCombo.Items.Add("Короткие таймеры");
            BatchTimerSetCombo.Items.Add("Длинные таймеры");
            BatchTimerSetCombo.Items.Add("ТЕСТ");
            BatchTimerSetCombo.SelectedIndex = 0;
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    _loadedSettings = JsonSerializer.Deserialize<Settings>(json);
                }

                if (_loadedSettings == null)
                    _loadedSettings = new Settings();

                if (string.IsNullOrWhiteSpace(_loadedSettings.SavePath))
                {
                    string docs = Environment.GetFolderPath(
                        Environment.SpecialFolder.MyDocuments);
                    _loadedSettings.SavePath = Path.Combine(docs, "TERMINAL");
                }

                _batchKosaEnabled = _loadedSettings.BatchKosaEnabled;
                _batchKosaTimerSeconds = _loadedSettings.BatchKosaTimerSeconds > 0
                    ? _loadedSettings.BatchKosaTimerSeconds : 25;

                if (_loadedSettings.BatchJobs != null && _loadedSettings.BatchJobs.Count > 0)
                {
                    _batchJobs.Clear();
                    foreach (var dto in _loadedSettings.BatchJobs)
                    {
                        _batchJobs.Add(new TemperatureJob
                        {
                            SetPointText = string.IsNullOrWhiteSpace(dto.SetPoint) ? "25" : dto.SetPoint,
                            StabilizationMinutesText = string.IsNullOrWhiteSpace(dto.StabilizationMinutes) ? "120" : dto.StabilizationMinutes,
                            IsEnabled = dto.IsEnabled,
                            IsCalibrationMode = dto.IsCalibrationMode,
                            CyclesText = string.IsNullOrWhiteSpace(dto.Cycles) ? "10" : dto.Cycles,
                            StartStepText = string.IsNullOrWhiteSpace(dto.StartStep) ? "1" : dto.StartStep,
                            CaseText = dto.Case
                        });
                    }
                }
                if (_loadedSettings.BatchPresets != null && _loadedSettings.BatchPresets.Count > 0)
                {
                    _batchPresets.Clear();
                    foreach (var p in _loadedSettings.BatchPresets)
                        _batchPresets.Add(p);
                }
            }
            catch
            {
                _loadedSettings = new Settings();
                _batchKosaEnabled = false;
                _batchKosaTimerSeconds = 25;

                string docs = Environment.GetFolderPath(
                    Environment.SpecialFolder.MyDocuments);
                _loadedSettings.SavePath = Path.Combine(docs, "TERMINAL");
            }
        }

        private void ApplyLoadedSettingsToUi()
        {
            if (_loadedSettings == null) return;

            if (_loadedSettings.BatchTargetPressureIndex >= 0 &&
                _loadedSettings.BatchTargetPressureIndex < BatchTargetPressureCombo.Items.Count)
                BatchTargetPressureCombo.SelectedIndex = _loadedSettings.BatchTargetPressureIndex;
            else
                BatchTargetPressureCombo.SelectedIndex = 0;

            if (_loadedSettings.BatchTimerSetIndex >= 0 &&
                _loadedSettings.BatchTimerSetIndex < BatchTimerSetCombo.Items.Count)
                BatchTimerSetCombo.SelectedIndex = _loadedSettings.BatchTimerSetIndex;
            else
                BatchTimerSetCombo.SelectedIndex = 0;

            if (!string.IsNullOrWhiteSpace(_loadedSettings.SavePath))
                SavePathBox.Text = _loadedSettings.SavePath;

            if (BatchKosaCheckBox != null)
                BatchKosaCheckBox.IsChecked = _batchKosaEnabled;
            if (BatchKosaTimerBox != null)
                BatchKosaTimerBox.Text = _batchKosaTimerSeconds.ToString(CultureInfo.InvariantCulture);

            if (BatchList != null)
                BatchList.ItemsSource = _batchJobs;

            if (ClientsList != null)
                ClientsList.ItemsSource = _connectedClients;

            RenumberBatchJobs();

            if (BatchTargetPressureCombo.SelectedItem != null)
            {
                _selectedTargetPressure = BatchTargetPressureCombo.SelectedItem.ToString();
                _currentStepIndex = 0;
                _completedSteps = 0;
                ShowSteps();
                UpdateMaxMinFromRange();
            }

            RefreshPresetCombo();

            if (_loadedSettings != null &&
                _loadedSettings.LastUsedPresetIndex >= 0 &&
                _loadedSettings.LastUsedPresetIndex < _batchPresets.Count &&
                BatchPresetCombo != null)
            {
                BatchPresetCombo.SelectedIndex = _loadedSettings.LastUsedPresetIndex;
            }

            UpdateStartButtonMode();
        }

        private void SaveSettings()
        {
            try
            {
                if (!_uiLoaded) return;
                if (SavePathBox == null) return;
                if (BatchTargetPressureCombo == null) return;
                if (BatchTimerSetCombo == null) return;

                if (!Directory.Exists(SettingsDir))
                    Directory.CreateDirectory(SettingsDir);

                var s = new Settings
                {
                    BatchPresets = _batchPresets.Select(p => new BatchPreset
                    {
                        Name = p.Name,
                        CreatedAt = p.CreatedAt,
                        Jobs = p.Jobs.Select(j => new BatchJobDto
                        {
                            SetPoint = j.SetPoint,
                            StabilizationMinutes = j.StabilizationMinutes,
                            IsEnabled = j.IsEnabled,
                            IsCalibrationMode = j.IsCalibrationMode,
                            Cycles = j.Cycles,
                            StartStep = j.StartStep,
                            Case = j.Case         
                        }).ToList()
                    }).ToList(),
                    LastUsedPresetIndex = BatchPresetCombo?.SelectedIndex ?? -1,

                    BatchTargetPressureIndex = BatchTargetPressureCombo.SelectedIndex,
                    BatchTimerSetIndex = BatchTimerSetCombo.SelectedIndex,
                    SavePath = SavePathBox.Text,
                    BatchKosaEnabled = _batchKosaEnabled,
                    BatchKosaTimerSeconds = _batchKosaTimerSeconds,

                    BatchJobs = _batchJobs.Select(j => new BatchJobDto
                    {
                        SetPoint = j.SetPointText,
                        StabilizationMinutes = j.StabilizationMinutesText,
                        IsEnabled = j.IsEnabled,
                        IsCalibrationMode = j.IsCalibrationMode,
                        Cycles = j.CyclesText,
                        StartStep = j.StartStepText,
                        Case = j.CaseText
                    }).ToList(),
                    WindowLeft = _loadedSettings?.WindowLeft ?? double.NaN,
                    WindowTop = _loadedSettings?.WindowTop ?? double.NaN,
                    WindowWidth = _loadedSettings?.WindowWidth ?? double.NaN,
                    WindowHeight = _loadedSettings?.WindowHeight ?? double.NaN
                };

                string json = JsonSerializer.Serialize(s,
                    new JsonSerializerOptions { WriteIndented = true });

                File.WriteAllText(SettingsFile, json);
            }
            catch { }
        }

        private void SavePathBox_TextChanged(object sender, System.Windows.Controls.TextChangedEventArgs e)
        {
            if (!_uiLoaded) return;
            SaveSettings();
        }

        private void PositionWindowRightHalf()
        {
            try
            {
                var work = SystemParameters.WorkArea;
                double halfWidth = work.Width / 2.0;

                this.WindowStartupLocation = WindowStartupLocation.Manual;
                this.Width = halfWidth;
                this.Height = work.Height;
                this.Left = work.Left + work.Width - halfWidth;
                this.Top = work.Top;
            }
            catch { }
        }

        private void ApplyWindowPositionOrFallback()
        {
            try
            {
                var work = SystemParameters.WorkArea;

                double l = _loadedSettings?.WindowLeft ?? double.NaN;
                double t = _loadedSettings?.WindowTop ?? double.NaN;
                double w = _loadedSettings?.WindowWidth ?? double.NaN;
                double h = _loadedSettings?.WindowHeight ?? double.NaN;

                bool hasSaved =
                    !double.IsNaN(l) && !double.IsNaN(t) &&
                    !double.IsNaN(w) && !double.IsNaN(h) &&
                    w >= 300 && h >= 300;

                if (hasSaved)
                {
                    bool intersects =
                        l + w > work.Left &&
                        t + h > work.Top &&
                        l < work.Left + work.Width &&
                        t < work.Top + work.Height;

                    if (intersects)
                    {
                        this.WindowStartupLocation = WindowStartupLocation.Manual;
                        this.Width = w;
                        this.Height = h;
                        this.Left = l;
                        this.Top = t;
                        return;
                    }
                }
            }
            catch { }

            PositionWindowRightHalf();
        }

        private void SaveWindowPositionToSettings()
        {
            try
            {
                if (_loadedSettings == null) return;

                var bounds = this.WindowState == WindowState.Normal
                    ? new Rect(this.Left, this.Top, this.Width, this.Height)
                    : this.RestoreBounds;

                if (bounds.Width < 100 || bounds.Height < 100) return;

                _loadedSettings.WindowLeft = bounds.Left;
                _loadedSettings.WindowTop = bounds.Top;
                _loadedSettings.WindowWidth = bounds.Width;
                _loadedSettings.WindowHeight = bounds.Height;
            }
            catch { }
        }

        private readonly Dictionary<string, double[]> _stepsByTarget = new()
        {
            { "100 кгс/см²", new double[] { 0, 20, 30, 40, 50, 60, 70, 80, 90, 100, 80, 70, 60, 50, 30, 0 } },
            { "200 кгс/см²", new double[] { 0, 30, 60, 80, 100, 120, 140, 160, 180, 200, 160, 140, 120, 100, 60, 0 } },
            { "250 кгс/см²", new double[] { 0, 30, 60, 90, 120, 150, 180, 210, 230, 250, 210, 180, 150, 120, 60, 0 } },
            { "300 кгс/см²", new double[] { 0, 50, 100, 125, 150, 175, 200, 225, 250, 300, 225, 200, 175, 150, 100, 0 } },
            { "350 кгс/см²", new double[] { 0, 50, 70, 120, 170, 220, 270, 300, 320, 350, 300, 270, 220, 170, 70, 0 } },
            { "350 кгс/см² (БИДТ)", new double[] { 0, 40, 80, 120, 160, 200, 240, 280, 320, 350, 320, 280, 200, 160, 80, 0 } },
            { "400 кгс/см²", new double[] { 0, 50, 70, 120, 170, 220, 270, 320, 370, 400, 320, 270, 220, 170, 70, 0 } },
            { "450 кгс/см²", new double[] { 0, 10, 20, 30, 40, 50, 120, 270, 370, 450, 370, 270, 120, 50, 30, 0 } },
            { "500 кгс/см²", new double[] { 0, 70, 120, 170, 220, 270, 320, 420, 470, 500, 420, 320, 270, 220, 120, 0 } },
            { "600 кгс/см²", new double[] { 0, 70, 120, 170, 220, 320, 420, 520, 570, 600, 520, 420, 320, 220, 120, 0 } },
            { "600 кгс/см² (13 точек)", new double[] { 0, 70, 120, 170, 270, 370, 470, 570, 600, 470, 270, 120, 0 } },
            { "25 МПа", new double[] { 0, 5, 7.5, 10, 12.5, 15, 17.5, 20, 22.5, 25, 20, 17.5, 15, 12.5, 7.5, 0 } },
            { "60 МПа", new double[] { 0, 7.5, 15, 22.5, 30, 37.5, 45, 50, 55, 60, 50, 45, 37.5, 30, 15, 0 } },
            { "75 МПа", new double[] { 0, 7.5, 15, 22.5, 30, 40, 50, 60, 67.5, 75, 60, 50, 40, 30, 15, 0 } },
            { "80 МПа", new double[] { 0, 7.5, 15, 22.5, 30, 40, 50, 60, 70, 80, 60, 50, 40, 30, 15, 0 } },
            { "100 МПа", new double[] { 0, 7.5, 15, 30, 45, 60, 70, 80, 90, 100, 80, 70, 60, 45, 15, 0 } },
            { "110 МПа", new double[] { 0, 7.5, 15, 30, 45, 60, 75, 90, 100, 110, 90, 75, 60, 45, 15, 0 } },
            { "130 МПа", new double[] { 0, 15, 30, 45, 60, 75, 90, 105, 120, 130, 105, 90, 75, 60, 30, 0 } },
            { "138 МПа (БУРСЕРВИС)", new double[] { 0, 27.5, 55, 85.75, 110.25, 138, 131, 103.5, 75.75, 48.25, 20.75, 2.5 } },
            { "140 МПа", new double[] { 0, 15, 30, 45, 60, 75, 90, 105, 120, 140, 105, 90, 75, 60, 30, 0 } },
            { "160 МПа (БУРСЕРВИС)", new double[] { 0, 34.5, 69, 103.5, 138, 160, 155, 120.75, 86.25, 51.75, 17.25, 2.5 } },
            { "160 МПа (Schlumberger)", new double[] { 0, 15, 30, 45, 60, 80, 100, 120, 140, 160, 120, 80, 60, 45, 15, 0 } },
            { "100 МПа (New)", new double[] { 0, 10, 20, 30, 40, 50, 60, 70, 80, 90, 100, 0 } },
        };

        // ─────────────────────────── НАСТРОЙКИ ───────────────────────────
        private class Settings
        {
            public string SavePath { get; set; } = "";
            public bool BatchKosaEnabled { get; set; } = false;
            public int BatchKosaTimerSeconds { get; set; } = 25;
            public int BatchTargetPressureIndex { get; set; } = 0;
            public int BatchTimerSetIndex { get; set; } = 0;
            public List<BatchJobDto> BatchJobs { get; set; } = new();
            public double WindowLeft { get; set; } = double.NaN;
            public double WindowTop { get; set; } = double.NaN;
            public double WindowWidth { get; set; } = double.NaN;
            public double WindowHeight { get; set; } = double.NaN;
            public List<BatchPreset> BatchPresets { get; set; } = new();
            public int LastUsedPresetIndex { get; set; } = -1;
        }

        private static readonly string SettingsDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),"TERMINAL");

        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

    }



}