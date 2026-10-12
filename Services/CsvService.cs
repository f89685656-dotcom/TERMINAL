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

                // ─── ТЧХ ───
                if (s.Tchx != null && s.Tchx.MainTables.Count > 0)
                {
                    sb.AppendLine($"# --- ТЧХ ({s.Tchx.ChannelCount}) ---");

                    // Основные таблицы
                    for (int i = 0; i < s.Tchx.MainTables.Count; i++)
                    {
                        WriteTchxTable(sb, $"# Основная {i + 1}", s.Tchx.MainTables[i]);
                        sb.AppendLine();
                    }

                    // Проверочные таблицы
                    for (int i = 0; i < s.Tchx.CheckTables.Count; i++)
                    {
                        WriteTchxTable(sb, $"# Проверка {i + 1}", s.Tchx.CheckTables[i]);
                        sb.AppendLine();
                    }
                }

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
                if (line.StartsWith("# --- ТЧХ"))
                {
                    currentSection = "tchx";
                    // Извлекаем ChannelCount
                    int p1 = line.IndexOf('(');
                    int p2 = line.IndexOf(')');
                    if (p1 >= 0 && p2 > p1)
                    {
                        string mode = line.Substring(p1 + 1, p2 - p1 - 1);
                        current.Tchx = new TchxData { ChannelCount = mode };
                    }
                    continue;
                }
                if (line.StartsWith("# Основная") || line.StartsWith("# Проверка"))
                {
                    bool isMain = line.StartsWith("# Основная");
                    string tchxHeader = line.Substring(2).Trim();

                    // Пропускаем строку с самим заголовком (если идёт "# Название таблицы")
                    i++;
                    if (i >= lines.Length) break;

                    // Если следующая строка — тоже "# ...", это Title таблицы
                    string title = tchxHeader;
                    if (lines[i].Trim().StartsWith("#"))
                    {
                        title = lines[i].Trim().Substring(1).Trim();
                        i++;
                        if (i >= lines.Length) break;
                    }

                    // Подписи столбцов
                    var colHeaders = lines[i].Split(';')
                        .Select(x => x.Trim())
                        .ToArray();

                    var table = new TchxTable
                    {
                        Title = title,
                        ColumnHeaders = colHeaders
                    };

                    // Читаем до 10 строк данных
                    var valuesList = new List<double?[]>();
                    i++;

                    while (i < lines.Length && valuesList.Count < 10)
                    {
                        string dataLine = lines[i].Trim();
                        if (string.IsNullOrEmpty(dataLine) || dataLine.StartsWith("#"))
                        {
                            i--;
                            break;
                        }

                        var cells = dataLine.Split(';');
                        var row = new double?[4];
                        for (int c = 0; c < 4; c++)
                        {
                            row[c] = c < cells.Length ? ParseNullable(cells[c]) : null;
                        }
                        valuesList.Add(row);
                        i++;
                    }

                    // Заполняем Values
                    table.Values = new double?[10, 4];
                    for (int r = 0; r < valuesList.Count; r++)
                    {
                        for (int c = 0; c < 4; c++)
                        {
                            table.Values[r, c] = valuesList[r][c];
                        }
                    }

                    if (current?.Tchx != null)
                    {
                        if (isMain) current.Tchx.MainTables.Add(table);
                        else current.Tchx.CheckTables.Add(table);
                    }

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

        private static void WriteTchxTable(StringBuilder sb, string header, TchxTable table)
        {
            sb.AppendLine(header);

            if (!string.IsNullOrEmpty(table.Title))
                sb.AppendLine($"# {table.Title}");

            sb.AppendLine(string.Join(";", table.ColumnHeaders));

            for (int r = 0; r < table.Values.GetLength(0); r++)
            {
                bool hasAny = false;
                for (int c = 0; c < table.Values.GetLength(1); c++)
                    if (table.Values[r, c].HasValue) { hasAny = true; break; }

                if (!hasAny) continue;

                for (int c = 0; c < table.Values.GetLength(1); c++)
                {
                    if (c > 0) sb.Append(';');
                    sb.Append(FormatNullable(table.Values[r, c]));
                }
                sb.AppendLine();
            }
        }
    }
}