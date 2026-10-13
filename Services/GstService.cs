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
        public static GstInfo ReadGst(string resonatorNumber, string resonatorModel)
        {
            var info = new GstInfo
            {
                ResonatorNumber = resonatorNumber,
                ResonatorModel = resonatorModel
            };

            try
            {
                // ─── 1. Разбор номера: "203.25" → серийник=203, год=2025 ───
                string[] parts = resonatorNumber.Split('.');
                if (parts.Length != 2 || parts[1].Length != 2)
                {
                    info.Error = $"Неверный формат номера резонатора: {resonatorNumber}";
                    return info;
                }

                string year = "20" + parts[1];              // "2025"
                string yearShort = parts[1];                     // "25"
                int resonatorSerial = int.Parse(parts[0]);        // 203

                // ─── 2. Базовая папка года ───
                string baseFolder = $@"\\server\pub\Иванов С.Н\ГСТ\{year}";
                if (!Directory.Exists(baseFolder))
                {
                    info.Error = $"Не найдена папка года: {baseFolder}";
                    return info;
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
                    info.Error = $"Не найдена папка модели '{resonatorModel}' в {baseFolder}";
                    return info;
                }

                // ─── 4+5. Перебираем ВСЕ папки диапазона, ищем файл ───
                string? file = null;
                string? foundRangeFolder = null;

                // "203.25" — как есть (с точкой) — используется для поиска файла и листа
                string normalizedDot = resonatorNumber.Replace('_', '.').Trim();

                foreach (var dir in Directory.GetDirectories(modelFolder))
                {
                    string name = Path.GetFileName(dir);

                    // Отрезаем суффикс в скобках: "42.26_51.26(12.03.26)" → "42.26_51.26"
                    int paren = name.IndexOf('(');
                    if (paren > 0) name = name.Substring(0, paren);

                    // Ищем ВСЕ пары "NNN.YY" или "NNN_YY" (серийник + год)
                    // [._] — точка ИЛИ подчёркивание между ними
                    var matches = Regex.Matches(name, @"(\d+)[._](\d+)");

                    var serialsInRange = new List<int>();

                    foreach (Match m in matches)
                    {
                        if (!int.TryParse(m.Groups[1].Value, out int serial)) continue;
                        if (!int.TryParse(m.Groups[2].Value, out int yr)) continue;

                        // Год должен совпадать с годом резонатора ("25" или "26")
                        if (yr == int.Parse(yearShort))
                        {
                            serialsInRange.Add(serial);
                        }
                    }

                    if (serialsInRange.Count < 1) continue;

                    int leftSerial = serialsInRange.Min();
                    int rightSerial = serialsInRange.Max();

                    if (resonatorSerial < leftSerial || resonatorSerial > rightSerial)
                        continue;

                    // ─── Ищем файл в этой папке ───
                    // Имена файлов: "103.26_104.26_105.26_106.26.xlsx" — точки ВНУТРИ номера,
                    // подчёркивания МЕЖДУ номерами. Ищем подстроку "NNN.YY".
                    var candidate = Directory.GetFiles(dir, "*.xlsx")
                        .FirstOrDefault(f =>
                            Path.GetFileName(f)
                                .Contains(normalizedDot, StringComparison.OrdinalIgnoreCase));

                    if (candidate != null)
                    {
                        file = candidate;
                        foundRangeFolder = dir;
                        break;
                    }
                }

                if (file == null)
                {
                    info.Error = $"Не найден .xlsx с номером {resonatorNumber} ни в одной папке диапазона";
                    return info;
                }
                info.FilePath = file;

                // ─── 6. График в PNG (лист Excel = номер резонатора) ───
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
        // Экспорт графика из Excel в PNG
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

                // Найти первый ChartObject
                dynamic chartObjects = sheet.ChartObjects();
                int count = chartObjects.Count;
                if (count == 0) return null;

                dynamic chartObj = chartObjects(1);

                // ⚠️ При необходимости можно уменьшить график перед экспортом:
                // chartObj.Width  = 360;
                // chartObj.Height = 240;

                // Экспорт графика в PNG
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
                if (wb != null) { try { ((dynamic)wb).Close(false); } catch { } }
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
    }
}