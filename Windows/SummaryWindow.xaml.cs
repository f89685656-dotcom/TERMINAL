using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using TERMINAL.Models;
using TERMINAL.Services;

namespace TERMINAL.Windows
{
    public partial class SummaryWindow : Window
    {
        private SummaryBatch? _currentBatch;
        private bool _isLoadedFromCsv = false;

        public SummaryWindow()
        {
            InitializeComponent();
        }

        // ─── Открыть CSV прошлой партии ───
        private void OpenCsv_Click(object sender, RoutedEventArgs e)
        {
            var dlg = new OpenFileDialog
            {
                Filter = "CSV файлы (*.csv)|*.csv|Все файлы (*.*)|*.*",
                Title = "Открыть сводку"
            };

            if (dlg.ShowDialog() != true) return;

            try
            {
                _currentBatch = CsvService.LoadFromCsv(dlg.FileName);
                _isLoadedFromCsv = true;

                // Блокируем «Обновить» — данные из архива
                RefreshButton.IsEnabled = false;

                BatchNameText.Text = $"Партия: {_currentBatch.BatchName}";
                UpdateSummaryGrid();
                UpdateDetailPanel();
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка загрузки CSV:\n{ex.Message}",
                    "Ошибка", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        // ─── Сохранить CSV ───
        private void SaveCsv_Click(object sender, RoutedEventArgs e)
        {
            if (_currentBatch == null)
            {
                MessageBox.Show("Нет данных для сохранения.");
                return;
            }

            // Путь: та же папка, что SavePathBox + имя партии
            // TODO: получить SavePathBox из MainWindow — см. ниже
            MessageBox.Show("Сохранение CSV — TODO (нужно получить путь из MainWindow).");
        }

        // ─── Обновить (перечитать .xlsx) ───
        private void Refresh_Click(object sender, RoutedEventArgs e)
        {
            if (_isLoadedFromCsv)
            {
                MessageBox.Show("Обновление недоступно для архивных данных.");
                return;
            }

            // TODO: перечитать .xlsx — вызов из MainWindow
            MessageBox.Show("Обновление — TODO.");
        }

        // ─── Печать ───
        private void Print_Click(object sender, RoutedEventArgs e)
        {
            var pd = new System.Windows.Controls.PrintDialog();
            if (pd.ShowDialog() == true)
            {
                // TODO: сформировать документ для печати
                MessageBox.Show("Печать — TODO.");
            }
        }

        // ─── Выбор датчика в сводке ───
        private void SummaryGrid_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        // ─── Переключение вкладки деталей ───
        private void DetailTabs_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateDetailPanel();
        }

        // ─── Вспомогательные ───
        private void UpdateSummaryGrid()
        {
            // TODO: заполнить SummaryGrid
        }

        private void UpdateDetailPanel()
        {
            // TODO: заполнить DetailContent по выбранному датчику
        }
    }
}