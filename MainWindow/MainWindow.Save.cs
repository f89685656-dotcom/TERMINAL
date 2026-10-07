using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using TERMINAL.Models;

namespace TERMINAL
{
    public partial class MainWindow
    {
        private async Task<bool> SaveDataCoreAsync(bool silent)
        {
            string saveDir = SavePathBox.Text?.Trim() ?? "";
            if (string.IsNullOrWhiteSpace(saveDir))
            {
                if (!silent) MessageBox.Show("Укажите путь сохранения.");
                else SetStatusWarning("Автосохранение пропущено: не указан путь сохранения");
                return false;
            }

            if (!Directory.Exists(saveDir))
            {
                if (!silent)
                {
                    var r = MessageBox.Show($"Папка не существует:\n{saveDir}\n\nСоздать?",
                        "Папка не найдена", MessageBoxButton.YesNo, MessageBoxImage.Question);
                    if (r != MessageBoxResult.Yes) return false;
                }
                try { Directory.CreateDirectory(saveDir); }
                catch
                {
                    if (silent) SetStatusWarning("Автосохранение: не удалось создать папку");
                    return false;
                }
            }

            List<ConnectedClient> selected;
            lock (_clientsLock)
                selected = _connectedClients.Where(c => c.IsSelected && !c.IsOffline).ToList();

            if (selected.Count == 0)
            {
                if (!silent) MessageBox.Show("Нет отмеченных клиентов.");
                else SetStatusWarning("Автосохранение пропущено: нет отмеченных клиентов");
                return false;
            }

            string fileName;
            string batchSetPoint = GetCurrentBatchSetPoint();

            if (!string.IsNullOrEmpty(batchSetPoint))
            {
                // Имя из активного задания пакета: "+80.csv" или "80.csv"
                fileName = "+" + batchSetPoint + ".csv";
            }
            else
            {
                // Fallback — старое поведение (для ручного режима)
                float furnaceSetPoint = _lastValidFurnaceSetPoint;
                fileName = FormatFurnaceSetPointFileName(furnaceSetPoint);
            }

            string fullPath = Path.Combine(saveDir, fileName);

            // В SaveDataCoreAsync после формирования fullPath:
            _lastSavedFilePath = fullPath;

            // В RunExcelMacroInternal:
            string csvPath = _lastSavedFilePath;
            if (!string.IsNullOrEmpty(csvPath) && File.Exists(csvPath))
            {
                // используем именно этот файл
            }
            else
            {
                // fallback — искать по уставке
            }

            if (!silent)
            {
                if (MessageBox.Show($"Сохранить в:\n{fullPath}\n\nКлиентов: {selected.Count}",
                    "Подтверждение", MessageBoxButton.OKCancel) != MessageBoxResult.OK) return false;
            }

            lock (_saveLock) _saveResponses.Clear();
            if (!silent) SaveDataButton.IsEnabled = false;

            try
            {
                byte[] message = Encoding.UTF8.GetBytes("SAVE_DATA " + fullPath + "\n");
                foreach (var c in selected)
                {
                    try
                    {
                        c.SetStatusSaving();
                        var stream = c.TcpClient.GetStream();
                        await stream.WriteAsync(message, 0, message.Length);
                        await stream.FlushAsync();
                    }
                    catch { }
                }

                int timeoutMs = 30000;
                var start = DateTime.Now;
                while ((DateTime.Now - start).TotalMilliseconds < timeoutMs * selected.Count)
                {
                    int done;
                    lock (_saveLock)
                        done = selected.Count(c => _saveResponses.Contains(c.Sid + ":OK")
                                                || _saveResponses.Contains(c.Sid + ":FAIL"));
                    if (done >= selected.Count) break;
                    await Task.Delay(200);
                }

                int okCount;
                lock (_saveLock)
                    okCount = selected.Count(c => _saveResponses.Contains(c.Sid + ":OK"));

                if (okCount == selected.Count)
                {
                    SetStatusSuccess($"Данные сохранены во всех клиентах ({okCount}): {fileName}");
                    return true;
                }
                else
                {
                    SetStatusWarning($"Сохранено {okCount} из {selected.Count}");
                    return false;
                }
            }
            finally
            {
                if (!silent) SaveDataButton.IsEnabled = true;
            }
        }

        private async void SaveData_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning)
            {
                MessageBox.Show("Идёт градуировка/жамкать. Дождитесь завершения.");
                return;
            }
            await SaveDataCoreAsync(silent: false);
        }

        private async Task<bool> ClearDataCoreAsync(bool silent = false)
        {
            List<ConnectedClient> selected;
            lock (_clientsLock)
                selected = _connectedClients.Where(c => c.IsSelected && !c.IsOffline).ToList();

            if (selected.Count == 0)
            {
                if (!silent) MessageBox.Show("Нет отмеченных клиентов.");
                else SetStatusWarning("Автоочистка пропущена: нет отмеченных клиентов");
                return false;
            }

            lock (_clearLock) _clearResponses.Clear();
            if (!silent) ClearDataButton.IsEnabled = false;

            try
            {
                byte[] message = Encoding.UTF8.GetBytes("CLEAR_DATA\n");
                foreach (var c in selected)
                {
                    try
                    {
                        c.SetStatusClearing();
                        var stream = c.TcpClient.GetStream();
                        await stream.WriteAsync(message, 0, message.Length);
                        await stream.FlushAsync();
                    }
                    catch { }
                }

                SetStatus($"Отправлена команда «Новая калибровка» ({selected.Count} клиент(ов)). " +
                          $"Ожидание ответа...", false);

                int timeoutPerClientMs = 30_000;
                var start = DateTime.Now;
                while ((DateTime.Now - start).TotalMilliseconds < timeoutPerClientMs * selected.Count)
                {
                    int done;
                    lock (_clearLock)
                        done = selected.Count(c => _clearResponses.Contains(c.Sid + ":OK")
                                                || _clearResponses.Contains(c.Sid + ":FAIL"));
                    if (done >= selected.Count) break;
                    await Task.Delay(200);
                }

                int okCount;
                lock (_clearLock)
                    okCount = selected.Count(c => _clearResponses.Contains(c.Sid + ":OK"));

                if (okCount == selected.Count)
                {
                    SetStatusSuccess($"Данные очищены во всех клиентах ({okCount})");
                    return true;
                }
                else
                {
                    SetStatusWarning($"Очищено {okCount} из {selected.Count}");
                    return false;
                }
            }
            finally
            {
                if (!silent) ClearDataButton.IsEnabled = true;
            }
        }

        private async void ClearData_Click(object sender, RoutedEventArgs e)
        {
            if (_isRunning || _batchRunning)
            {
                MessageBox.Show("Идёт градуировка/жамкать. Дождитесь завершения.");
                return;
            }
            await ClearDataCoreAsync(silent: false);
        }

    }
}