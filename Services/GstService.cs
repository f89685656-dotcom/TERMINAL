using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using TERMINAL.Models;

namespace TERMINAL.Services
{
    public static class GstService
    {
        // ═══════════════════════════════════════════════════════════════
        // ВЕРСИЯ 1 (СТАРАЯ). Создаёт СВОЙ Excel. Для одиночных вызовов.
        // ═══════════════════════════════════════════════════════════════
        public static GstInfo ReadGst(string resonatorNumber, string resonatorModel)
        {
            var info = new GstInfo
            {
                ResonatorNumber = resonatorNumber,
                ResonatorModel = resonatorModel
            };

            try
            {
                string? file = FindGstFile(resonatorNumber, resonatorModel, out string? error);
                if (file == null)
                {
                    info.Error = error ?? "Не найден файл ГСТ";
                    return info;
                }

                info.FilePath = file;

                string normalizedDot = resonatorNumber.Replace('_', '.').Trim();

                byte[]? png = ExportChartAsPng(file, normalizedDot)
                           ?? ExportChartAsPng(file, resonatorNumber.Replace('.', '_'));

                if (png == null)
                {
                    info.Error = $"Не удалось прочитать график из листа '{resonatorNumber}'";
                    return info;
                }

                info.ChartPng = png;
            }
            catch (Exception ex)
            {
                info.Error = $"Ошибка чтения ГСТ: {ex.Message}";
            }

            return info;
        }

        // ═══════════════════════════════════════════════════════════════
        // ВЕРСИЯ 2 (НОВАЯ). Использует УЖЕ ОТКРЫТЫЙ Excel.
        // Вызывать ТОЛЬКО из STA-потока (например, из SummaryService.ReadAllFiles).
        // ═══════════════════════════════════════════════════════════════
        public static GstInfo ReadGst(
            dynamic xl,
            string resonatorNumber,
            string resonatorModel)
        {
            var info = new GstInfo
            {
                ResonatorNumber = resonatorNumber,
                ResonatorModel = resonatorModel
            };

            try
            {
                string? file = FindGstFile(resonatorNumber, resonatorModel, out string? error);
                if (file == null)
                {
                    info.Error = error ?? "Не найден файл ГСТ";
                    return info;
                }

                info.FilePath = file;

                string normalizedDot = resonatorNumber.Replace('_', '.').Trim();

                byte[]? png = ExportChartAsPngWithExistingExcel(xl, file, normalizedDot)
                           ?? ExportChartAsPngWithExistingExcel(xl, file, resonatorNumber.Replace('.', '_'));

                if (png == null)
                {
                    info.Error = $"Не удалось прочитать график из листа '{resonatorNumber}'";
                    return info;
                }

                info.ChartPng = png;
            }
            catch (Exception ex)
            {
                info.Error = $"Ошибка чтения ГСТ: {ex.Message}";
            }

            return info;
        }

        // ═══════════════════════════════════════════════════════════════
        // Поиск .xlsx-файла ГСТ по номеру резонатора и модели.
        // Используется обеими версиями ReadGst.
        // ═══════════════════════════════════════════════════════════════
        private static string? FindGstFile(
            string resonatorNumber,
            string resonatorModel,
            out string? error)
        {
            error = null;

            // ─── 1. Разбор номера: "203.25" → серийник=203, год=2025 ───
            string[] parts = resonatorNumber.Split('.');
            if (parts.Length != 2 || parts[1].Length != 2)
            {
                error = $"Неверный формат номера резонатора: {resonatorNumber}";
                return null;
            }

            string year = "20" + parts[1];
            string yearShort = parts[1];
            int resonatorSerial = int.Parse(parts[0]);

            // ─── 2. Базовая папка года ───
            string baseFolder = $@"\\server\pub\Иванов С.Н\ГСТ\{year}";
            if (!Directory.Exists(baseFolder))
            {
                error = $"Не найдена папка года: {baseFolder}";
                return null;
            }

            // ─── 3. Папка модели (нормализуем запятые/точки/пробелы) ───
            string normModel = resonatorModel
                .Replace(',', '.')
                .Replace(" ", "")
                .Trim();

            string? modelFolder = Directory.GetDirectories(baseFolder)
                .FirstOrDefault(d =>
                {
                    string dn = Path.GetFileName(d)
                        .Replace(',', '.')
                        .Replace(" ", "")
                        .Trim();
                    return dn.StartsWith(normModel, StringComparison.OrdinalIgnoreCase);
                });

            if (modelFolder == null)
            {
                error = $"Не найдена папка модели '{resonatorModel}' в {baseFolder}";
                return null;
            }

            // ─── 4. Перебираем ВСЕ папки диапазона, ищем файл ───
            string normalizedDot = resonatorNumber.Replace('_', '.').Trim();

            foreach (var dir in Directory.GetDirectories(modelFolder))
            {
                string name = Path.GetFileName(dir);

                // Отрезаем суффикс в скобках: "42.26_51.26(12.03.26)" → "42.26_51.26"
                int paren = name.IndexOf('(');
                if (paren > 0) name = name.Substring(0, paren);

                // Ищем пары "NNN.YY" или "NNN_YY"
                var matches = Regex.Matches(name, @"(\d+)[._](\d+)");
                var serialsInRange = new List<int>();

                foreach (Match m in matches)
                {
                    if (!int.TryParse(m.Groups[1].Value, out int serial)) continue;
                    if (!int.TryParse(m.Groups[2].Value, out int yr)) continue;

                    if (yr == int.Parse(yearShort))
                        serialsInRange.Add(serial);
                }

                if (serialsInRange.Count < 1) continue;

                int leftSerial = serialsInRange.Min();
                int rightSerial = serialsInRange.Max();

                if (resonatorSerial < leftSerial || resonatorSerial > rightSerial)
                    continue;

                // ─── Ищем файл в этой папке ───
                var candidate = Directory.GetFiles(dir, "*.xlsx")
                    .FirstOrDefault(f =>
                        Path.GetFileName(f)
                            .Contains(normalizedDot, StringComparison.OrdinalIgnoreCase));

                if (candidate != null)
                    return candidate;
            }

            error = $"Не найден .xlsx с номером {resonatorNumber} ни в одной папке диапазона";
            return null;
        }

        // ═══════════════════════════════════════════════════════════════
        // Экспорт графика в PNG. СТАРАЯ версия — создаёт свой Excel.
        // ═══════════════════════════════════════════════════════════════
        private static byte[]? ExportChartAsPng(string filePath, string sheetName)
        {
            object? xlApp = null;
            object? wb = null;
            string tempPng = Path.Combine(Path.GetTempPath(),
                $"gst_{Guid.NewGuid():N}.png");

            try
            {
                Type? excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null) return null;

                dynamic xl = Activator.CreateInstance(excelType)!;
                xlApp = xl;
                xl.Visible = false;
                xl.DisplayAlerts = false;

                wb = xl.Workbooks.Open(filePath);
                dynamic sheets = ((dynamic)wb).Sheets;

                dynamic? sheet = null;
                foreach (dynamic sh in sheets)
                {
                    if (((string)sh.Name).Equals(sheetName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        sheet = sh;
                        break;
                    }
                }

                if (sheet == null) return null;

                dynamic chartObjects = sheet.ChartObjects();
                int count = chartObjects.Count;
                if (count == 0) return null;

                dynamic chartObj = chartObjects(1);
                chartObj.Chart.Export(tempPng, "PNG");

                if (!File.Exists(tempPng)) return null;
                byte[] png = File.ReadAllBytes(tempPng);
                return png;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (wb != null)
                {
                    try { ((dynamic)wb).Close(false); } catch { }
                }
                if (xlApp != null)
                {
                    try { ((dynamic)xlApp).Quit(); } catch { }
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(xlApp); } catch { }
                }
                try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        // ═══════════════════════════════════════════════════════════════
        // Экспорт графика в PNG. НОВАЯ версия — использует переданный Excel.
        // НЕ вызывает Quit, НЕ делает GC.Collect.
        // ═══════════════════════════════════════════════════════════════
        private static byte[]? ExportChartAsPngWithExistingExcel(
            dynamic xl,
            string filePath,
            string sheetName)
        {
            dynamic? wb = null;
            string tempPng = Path.Combine(Path.GetTempPath(),
                $"gst_{Guid.NewGuid():N}.png");

            try
            {
                wb = xl.Workbooks.Open(filePath);
                dynamic sheets = ((dynamic)wb).Sheets;

                dynamic? sheet = null;
                foreach (dynamic sh in sheets)
                {
                    if (((string)sh.Name).Equals(sheetName,
                        StringComparison.OrdinalIgnoreCase))
                    {
                        sheet = sh;
                        break;
                    }
                }

                if (sheet == null) return null;

                dynamic chartObjects = sheet.ChartObjects();
                int count = chartObjects.Count;
                if (count == 0) return null;

                dynamic chartObj = chartObjects(1);
                chartObj.Chart.Export(tempPng, "PNG");

                if (!File.Exists(tempPng)) return null;
                byte[] png = File.ReadAllBytes(tempPng);
                return png;
            }
            catch
            {
                return null;
            }
            finally
            {
                if (wb != null)
                {
                    try { ((dynamic)wb).Close(false); } catch { }
                }
                try { if (File.Exists(tempPng)) File.Delete(tempPng); } catch { }
                // ★ Quit и GC.Collect НЕ вызываем — Excel общий, закроется в ReadAllFiles
            }
        }
    }
}