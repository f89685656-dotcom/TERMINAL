using System;
using System.Net;
using System.Windows;
using System.Windows.Controls;

namespace TERMINAL.Windows
{
    public partial class DeviceSettingsWindow : Window
    {
        public DeviceSettingsWindow(int initialTab = 0)
        {
            InitializeComponent();

            DeviceTabs.SelectedIndex = initialTab;
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
            ShowFurnaceCardCheck.IsChecked = mw.ShowFurnaceCard;

            // АГК
            AgkIpBox.Text = mw.DeviceIp;
            AgkPortBox.Text = mw.DevicePort.ToString();
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
        }

        private void Default_Click(object sender, RoutedEventArgs e)
        {
            FurnaceIpBox.Text = "192.168.0.167";
            FurnacePortBox.Text = "502";
            FurnaceSetPointRegBox.Text = "201";
            FurnaceCurrentTempRegBox.Text = "205";
            FurnaceModeRegBox.Text = "200";
            ShowFurnaceCardCheck.IsChecked = true;

            AgkIpBox.Text = "192.168.0.159";
            AgkPortBox.Text = "8888";
            ShowAgkCardCheck.IsChecked = true;

            SktbFilterScriptCombo.SelectedIndex = 3;
            ShowSktbCardCheck.IsChecked = true;
        }

        private void Save_Click(object sender, RoutedEventArgs e)
        {
            if (!Validate()) return;

            var mw = MainWindow.Current;
            if (mw == null) { DialogResult = false; return; }

            // Один вызов — всё делает сам
            mw.ApplyDeviceSettings(
                FurnaceIpBox.Text.Trim(),
                int.Parse(FurnacePortBox.Text.Trim()),
                ushort.Parse(FurnaceSetPointRegBox.Text.Trim()),
                ushort.Parse(FurnaceCurrentTempRegBox.Text.Trim()),
                ushort.Parse(FurnaceModeRegBox.Text.Trim()),
                ShowFurnaceCardCheck.IsChecked == true,

                AgkIpBox.Text.Trim(),
                int.Parse(AgkPortBox.Text.Trim()),
                ShowAgkCardCheck.IsChecked == true,

                (SktbFilterScriptCombo.SelectedItem as ComboBoxItem)?.Content?.ToString()
                    ?? "Filter_for_KOSA_GUI",
                ShowSktbCardCheck.IsChecked == true
            );

            DialogResult = true;
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
    }
}