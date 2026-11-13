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
        private const string NullMarker = "";

        // ═══════════════════════════════════════════════════════
        // СОХРАНЕНИЕ
        // ═══════════════════════════════════════════════════════
        public static void SaveToCsv(SummaryBatch batch, string path)
        {
            var sb = new StringBuilder();

            sb.AppendLine("# ПАРТИЯ: " + batch.BatchName);
            sb.AppendLine("# СОХРАНЕНО: " + DateTime.Now.ToString("dd.MM.yyyy HH:mm"));
            sb.AppendLine("# ДАТЧИКОВ: " + batch.Sensors.Count);
            sb.AppendLine();

            sb.AppendLine("# ═══ СВОДКА ═══");
            sb.AppendLine("Датчик;Опис_Град;Пров_1;Пров_2;Пров_3;Пров_4;Пров_5");

            foreach (var s in batch.Sensors)
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

            foreach (var s in batch.Sensors)
            {
                sb.AppendLine($"# ═══ ДАТЧИК: {s.Number} ═══");
                sb.AppendLine();

                WriteTable(sb, "# --- Гистерезис градуировки ---", s.GradHysteresis);
                sb.AppendLine();
                WriteTable(sb, "# --- Гистерезис проверки ---", s.CheckHysteresis);
                sb.AppendLine();

                if (s.Tchx != null && s.Tchx.MainTables.Count > 0)
                {
                    sb.AppendLine($"# --- ТЧХ ({s.Tchx.ChannelCount}) ---");

                    for (int i = 0; i < s.Tchx.MainTables.Count; i++)
                    {
                        WriteTchxTable(sb, $"# Основная {i + 1}", s.Tchx.MainTables[i]);
                        sb.AppendLine();
                    }

                    for (int i = 0; i < s.Tchx.CheckTables.Count; i++)
                    {
                        WriteTchxTable(sb, $"# Проверка {i + 1}", s.Tchx.CheckTables[i]);
                        sb.AppendLine();
                    }
                }

                WriteTable(sb, "# --- Погрешность ---", s.Errors);

                if (s.VerificationTable != null)
                {
                    WriteTable(sb, "# --- Проверка ---", s.VerificationTable);
                    sb.AppendLine();
                }

                sb.AppendLine();



                // ─── ГСТ (Base64) ───
                if (s.Gst != null && s.Gst.IsOk &&
                    s.Gst.ChartPng != null && s.Gst.ChartPng.Length > 0)
                {
                    sb.AppendLine("# --- ГСТ ---");
                    sb.AppendLine("# Резонатор: " + (s.Gst.ResonatorNumber ?? ""));
                    sb.AppendLine("# Модель: " + (s.Gst.ResonatorModel ?? ""));
                    sb.AppendLine("# Файл: " + (s.Gst.FilePath ?? ""));
                    sb.AppendLine("# PNG_BASE64");
                    sb.AppendLine(Convert.ToBase64String(s.Gst.ChartPng));
                    sb.AppendLine();
                }

            }

            File.WriteAllText(path, sb.ToString(), Encoding.UTF8);
        }

        private static void WriteTable(StringBuilder sb, string header, TableData table)
        {
            sb.AppendLine(header);

            sb.Append("P");
            foreach (var t in table.ColumnHeaders)
                sb.Append(';').Append(t.ToString("0.###", CultureInfo.InvariantCulture));
            sb.AppendLine();

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
            var batch = new SummaryBatch { SavedAt = DateTime.Now };

            var lines = File.ReadAllLines(path, Encoding.UTF8);
            int i = 0;

            // ─── Читаем шапку ───
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

            if (i < lines.Length && lines[i].StartsWith("Датчик"))
                i++;

            // ─── Читаем сводную таблицу датчиков ───
            while (i < lines.Length)
            {
                string line = lines[i].Trim();
                if (string.IsNullOrEmpty(line) || line.StartsWith("#"))
                    break;

                var parts = line.Split(';');
                if (parts.Length >= 7 && int.TryParse(parts[0], out int num))
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

            SensorSummary? current = null;
            string currentSection = "";
            int sensorIndex = 0;

            // ─── Читаем детали по каждому датчику ───
            for (; i < lines.Length; i++)
            {
                string line = lines[i].Trim();

                if (string.IsNullOrEmpty(line)) continue;

                // Новый датчик
                if (line.StartsWith("# ═══ ДАТЧИК:"))
                {
                    if (sensorIndex < batch.Sensors.Count)
                    {
                        current = batch.Sensors[sensorIndex];
                        sensorIndex++;
                    }
                    else
                    {
                        current = null;
                    }
                    currentSection = "";
                    continue;
                }

                // Заголовки таблиц (Гистерезис)
                if (line.StartsWith("# --- Гистерезис градуировки")) { currentSection = "grad"; continue; }
                if (line.StartsWith("# --- Гистерезис проверки")) { currentSection = "check"; continue; }

                // ТЧХ
                if (line.StartsWith("# --- ТЧХ"))
                {
                    currentSection = "tchx";
                    int p1 = line.IndexOf('(');
                    int p2 = line.IndexOf(')');
                    if (p1 >= 0 && p2 > p1)
                    {
                        string mode = line.Substring(p1 + 1, p2 - p1 - 1);
                        if (current != null)
                            current.Tchx = new TchxData { ChannelCount = mode };
                    }
                    continue;
                }

                // Таблицы ТЧХ (Основная / Проверка)
                if (line.StartsWith("# Основная") || line.StartsWith("# Проверка"))
                {
                    bool isMain = line.StartsWith("# Основная");
                    string tchxHeader = line.Substring(2).Trim();

                    i++;
                    if (i >= lines.Length) break;

                    string title = tchxHeader;
                    if (lines[i].Trim().StartsWith("#"))
                    {
                        title = lines[i].Trim().Substring(1).Trim();
                        i++;
                        if (i >= lines.Length) break;
                    }

                    var colHeaders = lines[i].Split(';').Select(x => x.Trim()).ToArray();

                    var tchxTable = new TchxTable
                    {
                        Title = title,
                        ColumnHeaders = colHeaders
                    };

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
                            row[c] = c < cells.Length ? ParseNullable(cells[c]) : null;
                        valuesList.Add(row);
                        i++;
                    }

                    tchxTable.Values = new double?[10, 4];
                    for (int r = 0; r < valuesList.Count; r++)
                        for (int c = 0; c < 4; c++)
                            tchxTable.Values[r, c] = valuesList[r][c];

                    if (current?.Tchx != null)
                    {
                        if (isMain) current.Tchx.MainTables.Add(tchxTable);
                        else current.Tchx.CheckTables.Add(tchxTable);
                    }
                    continue;
                }

                // ★ ИСПРАВЛЕНО: раздельные проверки для Погрешности и Проверки
                if (line.StartsWith("# --- Погрешность"))
                {
                    currentSection = "errors";
                    continue;
                }

                if (line.StartsWith("# --- Проверка"))
                {
                    currentSection = "verification";
                    continue;
                }

                // ГСТ
                if (line.StartsWith("# --- ГСТ ---"))
                {
                    currentSection = "gst";
                    continue;
                }

                // Разбор ГСТ
                if (currentSection == "gst")
                {
                    if (line.StartsWith("# PNG_BASE64"))
                    {
                        i++;
                        if (i < lines.Length)
                        {
                            string base64 = lines[i].Trim();
                            try
                            {
                                if (current != null)
                                {
                                    if (current.Gst == null) current.Gst = new GstInfo();
                                    current.Gst.ChartPng = Convert.FromBase64String(base64);
                                }
                            }
                            catch (Exception ex)
                            {
                                System.Diagnostics.Debug.WriteLine(
                                    $"[CsvService] Base64 decode error: {ex.Message}");
                            }
                            i++;
                        }
                        continue;
                    }
                    else if (line.StartsWith("# Резонатор:"))
                    {
                        if (current != null)
                        {
                            if (current.Gst == null) current.Gst = new GstInfo();
                            current.Gst.ResonatorNumber = line.Substring("# Резонатор:".Length).Trim();
                        }
                    }
                    else if (line.StartsWith("# Модель:"))
                    {
                        if (current != null)
                        {
                            if (current.Gst == null) current.Gst = new GstInfo();
                            current.Gst.ResonatorModel = line.Substring("# Модель:".Length).Trim();
                        }
                    }
                    else if (line.StartsWith("# Файл:"))
                    {
                        if (current != null)
                        {
                            if (current.Gst == null) current.Gst = new GstInfo();
                            current.Gst.FilePath = line.Substring("# Файл:".Length).Trim();
                        }
                    }
                    continue;
                }

                if (current == null) continue;

                // ★ ИСПРАВЛЕНО: Объявление table перенесено сюда, чтобы не было ошибки области видимости
                if (line.StartsWith("P;"))
                {
                    var headers = line.Split(';')
                        .Skip(1)
                        .Select(x => ParseDouble(x))
                        .ToList();

                    // ★ Если заголовки пустые или сломаны, подставляем индексы
                    if (headers.Count == 0 || headers.All(h => h == 0))
                    {
                        for (int h = 0; h < 4; h++) headers.Add(h + 1);
                    }

                    var table = new TableData { ColumnHeaders = headers };

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

                            while (rowVals.Count < headers.Count)
                                rowVals.Add(null);

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

                    // ★ ИСПРАВЛЕНО: Добавлено сохранение VerificationTable и убраны дубликаты
                    if (currentSection == "grad") current.GradHysteresis = table;
                    else if (currentSection == "check") current.CheckHysteresis = table;
                    else if (currentSection == "errors") current.Errors = table;
                    else if (currentSection == "verification") current.VerificationTable = table;
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