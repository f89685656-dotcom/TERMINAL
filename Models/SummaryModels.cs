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
    }

    public class TableData
    {
        public List<double> RowHeaders { get; set; } = new();      // давления
        public List<double> ColumnHeaders { get; set; } = new();   // температуры
        public double?[,] Values { get; set; } = new double?[0, 0];
    }
}