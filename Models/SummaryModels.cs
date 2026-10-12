using System;
using System.Collections.Generic;

namespace TERMINAL.Models
{
    public class SummaryBatch
    {
        public string BatchName { get; set; } = "";
        public DateTime SavedAt { get; set; } = DateTime.Now;
        public List<double> Temperatures { get; set; } = new();
        public List<SensorSummary> Sensors { get; set; } = new();
    }

    public class SensorSummary
    {
        public int Number { get; set; }
        public double? GradDescription { get; set; }
        public double?[] CheckDescriptions { get; set; } = new double?[5];

        public TableData GradHysteresis { get; set; } = new();
        public TableData CheckHysteresis { get; set; } = new();
        public TableData Errors { get; set; } = new();
        public TchxData? Tchx { get; set; }
    }

    public class TableData
    {
        public List<double> RowHeaders { get; set; } = new();      // давления
        public List<double> ColumnHeaders { get; set; } = new();   // температуры
        public double?[,] Values { get; set; } = new double?[0, 0];
    }
    public class SummaryRow
    {
        public int Number { get; set; }
        public double? GradDescription { get; set; }
        public double? Check1 { get; set; }
        public double? Check2 { get; set; }
        public double? Check3 { get; set; }
        public double? Check4 { get; set; }
        public double? Check5 { get; set; }
        public SensorSummary Source { get; set; } = null!;
    }

    public class TchxTable
    {
        public string Title { get; set; } = "";
        public string[] ColumnHeaders { get; set; } = new string[4];
        public double?[,] Values { get; set; } = new double?[10, 4];
    }

    public class TchxData
    {
        public string ChannelCount { get; set; } = "";
        public List<TchxTable> MainTables { get; set; } = new();
        public List<TchxTable> CheckTables { get; set; } = new();
    }
}