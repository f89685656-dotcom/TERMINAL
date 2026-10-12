using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using TERMINAL.Models;

namespace TERMINAL.Services
{
    public static class CsvService
    {
        private const string NullMarker = "";   // пустая ячейка в CSV

        // ═══════════════════════════════════════════════════════
        // СОХРАНЕНИЕ
        // ═══════════════════════════════════════════════════════
        public static void SaveToCsv(SummaryBatch batch, string path)
        {
            var sb = new StringBuilder();

            // ─── Шапка ───
            sb.AppendLine("# ПАРТИЯ: " + batch.BatchName);
            sb.AppendLine("# СОХРАНЕНО: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            sb.AppendLine("# ДАТЧИКОВ: " + batch.Sensors.Count);
            sb.AppendLine();

            // ─── Сводка ───
            sb.AppendLine("# ═══ СВОДКА ═══");
            sb.AppendLine("Датчик;Опис_Град;Пров_1;Пров_2;Пров_3;Пров_4;Пров_5");

            foreach (var s in batch.Sensors.OrderBy(x => x.Number))
            {
                sb.Append(s.Number).Append(';');
                sb.Append(FormatNullable(s.GradDescription)).Append(';');

                for (int i = 0; i < 5; i++)
                {
                    sb.Append(FormatNullable(
                        i < s.CheckDescriptions.Length ? s.CheckDescriptions[i] : null));
                    if (i < 4) sb.Append(';');
                }
                sb.AppendLine();
            }

            sb.AppendLine();

            // ─── По каждому датчику ───
            foreach (var s in batch.Sensors.OrderBy(x => x.Number))
            {
                sb.AppendLine($"# ═══ ДАТЧИК: {s.Number} ═══");
                sb.AppendLine();

                WriteTable(sb, "# --- Гистерезис градуировки ---", s.GradHysteresis);
                sb.AppendLine();
                WriteTable(sb, "# --- Гистерезис проверки ---", s.CheckHysteresis);
                sb.AppendLine();
                WriteTable(sb, "# --- Погрешность ---", s.Errors);
                sb.AppendLine();
            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void WriteTable(StringBuilder sb, string header, TableData table)
        {
            sb.AppendLine(header);

            // Заголовок: P;t1;t2;...
            sb.Append("P");
            foreach (var t in table.ColumnHeaders)
                sb.Append(';').Append(t.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine();

            // Строки
            for (int r = 0; r < table.Values.GetLength(0); r++)
            {
                string p = r < table.RowHeaders.Count
                    ? table.RowHeaders[r].ToString("0.###", CultureInfo.InvariantCulture)
                    : "";

                sb.Append(p);

                for (int c = 0; c < table.Values.GetLength(1); c++)
                {
                    sb.Append(';').Append(FormatNullable(table.Values[r, c]));
                }
                sb.AppendLine();
            }
        }

        private static string FormatNullable(double? v)
        {
            if (!v.HasValue) return "";
            return v.Value.ToString("0.0000", CultureInfo.InvariantCulture);
        }

        // ═══════════════════════════════════════════════════════
        // ЗАГРУЗКА
        // ═══════════════════════════════════════════════════════
        public static SummaryBatch LoadFromCsv(string path)
        {
            var batch = new SummaryBatch
            {
                SavedAt = DateTime.Now
            };

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            int i = 0;

            // ─── Шапка ───
            while (i < lines.Length)
            {
                string line = lines[i].Trim();

                if (line.StartsWith("# ПАРТИЯ:"))
                    batch.BatchName = line.Substring("# ПАРТИЯ:".Length).Trim();

                if (line.StartsWith("# ═══ СВОДКА ═══"))
                {
                    i++;
                    break;
                }
                i++;
            }

            // ─── Сводка ───
            if (i < lines.Length && lines[i].StartsWith("Датчик"))
                i++;   // пропускаем заголовок

            while (i < lines.Length)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    break;

                var parts = line.Split(';');
                if (parts.Length >= 7 &&
                    int.TryParse(parts[0], out int num))
                {
                    var sensor = new SensorSummary { Number = num };

                    sensor.GradDescription = ParseNullable(parts[1]);

                    for (int k = 0; k < 5; k++)
                    {
                        int idx = k + 2;
                        sensor.CheckDescriptions[k] =
                            idx < parts.Length ? ParseNullable(parts[idx]) : null;
                    }

                    batch.Sensors.Add(sensor);
                }
                i++;
            }

            // ─── По датчикам ───
            SensorSummary? current = null;
            string currentSection = "";

            for (; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrEmpty(line)) continue;

                if (line.StartsWith("# ═══ ДАТЧИК:"))
                {
                    // "# ═══ ДАТЧИК: 1048 ═══"
                    string numStr = line
                        .Replace("# ═══ ДАТЧИК:", "")
                        .Replace("═══", "")
                        .Trim();

                    if (int.TryParse(numStr, out int num))
                    {
                        current = batch.Sensors.FirstOrDefault(s => s.Number == num);
                    }
                    currentSection = "";
                    continue;
                }

                if (line.StartsWith("# --- Гистерезис градуировки"))
                {
                    currentSection = "grad";
                    continue;
                }
                if (line.StartsWith("# --- Гистерезис проверки"))
                {
                    currentSection = "check";
                    continue;
                }
                if (line.StartsWith("# --- Погрешность"))
                {
                    currentSection = "errors";
                    continue;
                }

                if (current == null) continue;

                // Заголовок таблицы (P;t1;t2;...)
                if (line.StartsWith("P;"))
                {
                    var headers = line.Split(';')
                        .Skip(1)
                        .Select(x => ParseDouble(x))
                        .Where(x => x != 0)
                        .ToList();

                    var table = new TableData();
                    table.ColumnHeaders = headers;

                    // Читаем строки данных
                    i++;
                    while (i < lines.Length)
                    {
                        string dataLine = lines[i].Trim();
                        if (string.IsNullOrEmpty(dataLine) || dataLine.StartsWith("#"))
                        {
                            i--;
                            break;
                        }

                        var cells = dataLine.Split(';');
                        if (cells.Length > 0)
                        {
                            double p = ParseDouble(cells[0]);
                            table.RowHeaders.Add(p);

                            var rowVals = new List<double?>();
                            for (int c = 1; c < cells.Length && c <= headers.Count; c++)
                                rowVals.Add(ParseNullable(cells[c]));

                            // дополняем null
                            while (rowVals.Count < headers.Count)
                                rowVals.Add(null);

                            // сохраняем во временную структуру — соберём в конце
                            if (table.Values.Length == 0)
                            {
                                // инициализация по первому чтению
                                var tempValues = new double?[0, headers.Count];
                                table.Values = new double?[0, headers.Count];
                            }

                            // добавляем строку
                            var newVals = new double?[table.Values.GetLength(0) + 1, headers.Count];
                            for (int r = 0; r < table.Values.GetLength(0); r++)
                                for (int c = 0; c < headers.Count; c++)
                                    newVals[r, c] = table.Values[r, c];

                            for (int c = 0; c < headers.Count; c++)
                                newVals[table.Values.GetLength(0), c] = rowVals[c];

                            table.Values = newVals;
                        }
                        i++;
                    }

                    if (currentSection == "grad") current.GradHysteresis = table;
                    else if (currentSection == "check") current.CheckHysteresis = table;
                    else if (currentSection == "errors") current.Errors = table;
                }
            }

            return batch;
        }

        private static double ParseDouble(string s)
        {
            if (double.TryParse(s, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double r))
                return r;
            return 0;
        }

        private static double? ParseNullable(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            if (double.TryParse(s, NumberStyles.Float,
                CultureInfo.InvariantCulture, out double r))
                return r;
            return null;
        }
    }
}