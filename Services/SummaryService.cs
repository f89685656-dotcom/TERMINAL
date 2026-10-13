using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using TERMINAL.Models;

namespace TERMINAL.Services
{
    public static class SummaryService
    {
        /// <summary>
        /// Читает ВСЕ .xlsm файлы из папки.
        /// Каждый файл = один датчик.
        /// </summary>
        public static SummaryBatch ReadAllSensorsFromFolder(string dataFolder, string batchName)
        {
            var batch = new SummaryBatch
            {
                BatchName = batchName,
                SavedAt = DateTime.Now
            };

            var files = Directory.GetFiles(dataFolder, "*.xlsm")
                .OrderBy(f => f)
                .ToList();

            if (files.Count == 0)
                throw new Exception($"В папке нет .xlsm файлов:\n{dataFolder}");

            object? xlApp = null;

            try
            {
                Type? excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                    throw new Exception("Excel не установлен на этой машине");

                dynamic xl = Activator.CreateInstance(excelType)!;
                xlApp = xl;
                xl.Visible = false;
                xl.DisplayAlerts = false;

                foreach (var file in files)
                {
                    dynamic wb = null;
                    try
                    {
                        wb = xl.Workbooks.Open(file);

                        SensorSummary sensor = ReadSensorFromOpenWorkbook((object)wb);
                        batch.Sensors.Add(sensor);

                        if (batch.Temperatures.Count == 0 &&
                            sensor.Errors.ColumnHeaders.Count > 0)
                        {
                            batch.Temperatures = sensor.Errors.ColumnHeaders
                                .Where(t => t != 0)
                                .ToList();
                        }
                    }
                    finally
                    {
                        // Закрываем ТОЛЬКО книгу, без Quitif (line.StartsWith("# Основная") || line.StartsWith("# Проверка"))
                        if (wb != null)
                        {
                            try { ((dynamic)wb).Close(false); } catch { }
                        }
                    }
                }
            }
            finally
            {
                // ← Quit и Release ТОЛЬКО ЗДЕСЬ, один раз
                if (xlApp != null)
                {
                    try { ((dynamic)xlApp).Quit(); } catch { }
                    try { System.Runtime.InteropServices.Marshal.ReleaseComObject(xlApp); } catch { }
                }
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }

            return batch;
        }

        /// <summary>
        /// Читает один .xlsm файл (один датчик).
        /// Открывает Excel, читает, закрывает. Для прогресс-бара.
        /// </summary>
        public static SensorSummary ReadSingleFile(string filePath)
        {
            object? xlApp = null;
            dynamic? wb = null;

            try
            {
                Type? excelType = Type.GetTypeFromProgID("Excel.Application");
                if (excelType == null)
                    throw new Exception("Excel не установлен на этой машине");

                dynamic xl = Activator.CreateInstance(excelType)!;
                xlApp = xl;
                xl.Visible = false;
                xl.DisplayAlerts = false;

                wb = xl.Workbooks.Open(filePath);
                return ReadSensorFromOpenWorkbook((object)wb);
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
                GC.Collect();
                GC.WaitForPendingFinalizers();
            }
        }

        /// <summary>
        /// Читает данные одного датчика из открытой книги.
        /// </summary>
        public static SensorSummary ReadSensorFromOpenWorkbook(dynamic workbook)
        {
            var sensor = new SensorSummary();

            try
            {
                dynamic wsData = workbook.Sheets["Данные"];
                dynamic wsBCH = workbook.Sheets["БЧХ"];
                dynamic wsTerminal = workbook.Sheets["TERMINAL"];

                // Номер датчика
                try
                {
                    var numVal = wsData.Range("F1").Value;
                    if (numVal != null)
                        sensor.Number = (int)Math.Round(Convert.ToDouble(numVal));
                }
                catch (Exception ex) { throw new Exception("Ошибка чтения F1: " + ex.Message); }

                // ─── Номер резонатора (F2) и модель (F3, объединённая с G3) ───
                try
                {
                    sensor.ResonatorNumber = (wsData.Range("F2").Value?.ToString() ?? "").Trim();

                    dynamic mergedCell = wsData.Range("F3").MergeArea;
                    sensor.ResonatorModel = (mergedCell.Cells[1, 1].Value?.ToString() ?? "").Trim();
                }
                catch (Exception ex) { throw new Exception("Ошибка чтения F2/F3: " + ex.Message); }

                // Описываемость
                try
                {
                    sensor.GradDescription = ToNullableDouble(wsBCH.Range("J18").Value);
                    for (int i = 0; i < 5; i++)
                    {
                        string cell = "J" + (19 + i);
                        sensor.CheckDescriptions[i] = ToNullableDouble(wsBCH.Range(cell).Value);
                    }
                }
                catch (Exception ex) { throw new Exception("Ошибка чтения БЧХ.J18:J23: " + ex.Message); }

                // Погрешность
                try
                {
                    sensor.Errors = ReadTable(
                        wsTerminal,
                        tempRow: 3, tempColStart: 3, tempColCount: 10,
                        pressRowStart: 4, pressRowCount: 16, pressCol: 2,
                        dataRowStart: 4, dataColStart: 3,
                        dataRows: 16, dataCols: 10);
                }
                catch (Exception ex) { throw new Exception("Ошибка чтения Погрешности: " + ex.Message); }

                // Гистерезис градуировки
                try
                {
                    sensor.GradHysteresis = ReadTable(
                        wsTerminal,
                        tempRow: 4, tempColStart: 15, tempColCount: 10,
                        pressRowStart: 5, pressRowCount: 6, pressCol: 14,
                        dataRowStart: 5, dataColStart: 15,
                        dataRows: 6, dataCols: 10);
                }
                catch (Exception ex) { throw new Exception("Ошибка чтения Гистерезиса градуировки: " + ex.Message); }

                // Гистерезис проверки
                try
                {
                    sensor.CheckHysteresis = ReadTable(
                        wsTerminal,
                        tempRow: 14, tempColStart: 15, tempColCount: 5,
                        pressRowStart: 15, pressRowCount: 6, pressCol: 14,
                        dataRowStart: 15, dataColStart: 15,
                        dataRows: 6, dataCols: 5);
                }
                catch (Exception ex) { throw new Exception("Ошибка чтения Гистерезиса проверки: " + ex.Message); }
            }
            catch (Exception ex)
            {
                throw new Exception("Датчик: " + ex.Message, ex);
            }
            sensor.Tchx = ReadTchx(workbook);

            return sensor;
        }
        
        
        private static TableData ReadTable(dynamic ws,int tempRow, int tempColStart, int tempColCount,int pressRowStart, int pressRowCount, int pressCol,int dataRowStart, int dataColStart,int dataRows, int dataCols)
        {
            var table = new TableData();

            // Заголовки температур
            for (int c = 0; c < tempColCount; c++)
            {
                var v = ws.Cells[tempRow, tempColStart + c].Value;
                table.ColumnHeaders.Add(ToDouble(v));
            }

            // Заголовки давлений
            for (int r = 0; r < pressRowCount; r++)
            {
                var v = ws.Cells[pressRowStart + r, pressCol].Value;
                table.RowHeaders.Add(ToDouble(v));
            }

            // Значения
            table.Values = new double?[dataRows, dataCols];
            for (int r = 0; r < dataRows; r++)
            {
                for (int c = 0; c < dataCols; c++)
                {
                    var v = ws.Cells[dataRowStart + r, dataColStart + c].Value;
                    table.Values[r, c] = ToNullableDouble(v);
                }
            }

            return table;
        }

        private static double ToDouble(object? v)
        {
            if (v == null) return 0;
            if (v is double d) return d;
            if (v is int i) return i;
            if (double.TryParse(v.ToString(), out double r)) return r;
            return 0;
        }

        private static double? ToNullableDouble(object? v)
        {
            if (v == null) return null;
            if (v is double d) return d;
            if (v is int i) return i;
            if (double.TryParse(v.ToString(), out double r)) return r;
            return null;
        }

        private static TchxData? ReadTchx(dynamic workbook)
        {
            // Условие читаем с листа БЧХ
            dynamic wsBCH = workbook.Sheets["БЧХ"];
            string mode = (wsBCH.Range("I11").Value?.ToString() ?? "").Trim();

            if (mode.IndexOf("Без термоканала", StringComparison.OrdinalIgnoreCase) >= 0)
                return null;

            var tchx = new TchxData { ChannelCount = mode };

            // Таблицы читаем с листа ТЧХ
            dynamic wsTCH = workbook.Sheets["ТЧХ"];

            var mainCols = new List<(int col, int colEnd)>();
            var checkCols = new List<(int col, int colEnd)>();

            if (mode.IndexOf("Один", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mainCols.Add((1, 4));
                checkCols.Add((1, 4));
            }
            else if (mode.IndexOf("Два", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mainCols.Add((1, 4));
                mainCols.Add((6, 9));
                checkCols.Add((1, 4));
                checkCols.Add((6, 9));
            }
            else if (mode.IndexOf("Три", StringComparison.OrdinalIgnoreCase) >= 0)
            {
                mainCols.Add((1, 4));
                mainCols.Add((6, 9));
                mainCols.Add((11, 14));
                checkCols.Add((1, 4));
                checkCols.Add((6, 9));
                checkCols.Add((11, 14));
            }
            else
            {
                return null;
            }

            // ← читаем с wsTCH, а не wsBCH
            foreach (var (col, colEnd) in mainCols)
            {
                tchx.MainTables.Add(ReadTchxTable(wsTCH, 3, col, colEnd));
            }

            foreach (var (col, colEnd) in checkCols)
            {
                tchx.CheckTables.Add(ReadTchxTable(wsTCH, 37, col, colEnd));
            }

            return tchx;
        }

        private static TchxTable ReadTchxTable(dynamic ws, int rowStart, int col, int colEnd)
        {
            var table = new TchxTable();

            // Заголовок (объединённая ячейка)
            table.Title = ws.Cells[rowStart, col].Value?.ToString()?.Trim() ?? "";

            // Подписи столбцов (строка +1)
            table.ColumnHeaders = new string[4];
            for (int c = 0; c < 4; c++)
            {
                table.ColumnHeaders[c] = ws.Cells[rowStart + 1, col + c]
                                            .Value?.ToString()?.Trim() ?? "";
            }

            // Данные (10 строк × 4 столбца, начиная со строки +2)
            table.Values = new double?[10, 4];
            for (int r = 0; r < 10; r++)
            {
                for (int c = 0; c < 4; c++)
                {
                    var v = ws.Cells[rowStart + 2 + r, col + c].Value;
                    table.Values[r, c] = ToNullableDouble(v);
                }
            }

            return table;
        }

    }
}