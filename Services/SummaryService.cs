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

                        // ← ЯВНЫЙ ТИП + приведение к object
                        SensorSummary sensor = ReadSensorFromOpenWorkbook((object)wb);
                        batch.Sensors.Add(sensor);

                        // Температуры — из первого датчика
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
                        if (wb != null)
                        {
                            try { ((dynamic)wb).Close(false); } catch { }
                        }
                    }
                }
            }
            finally
            {
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

            return sensor;
        }
        private static TableData ReadTable(
            dynamic ws,
            int tempRow, int tempColStart, int tempColCount,
            int pressRowStart, int pressRowCount, int pressCol,
            int dataRowStart, int dataColStart,
            int dataRows, int dataCols)
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
    }
}