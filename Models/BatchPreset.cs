using System;
using System.Collections.Generic;

namespace TERMINAL.Models
{
    public class BatchJobDto
    {
        public string SetPoint { get; set; } = "25";
        public string StabilizationMinutes { get; set; } = "120";
        public bool IsEnabled { get; set; } = true;
        public bool IsCalibrationMode { get; set; } = true;
        public string Cycles { get; set; } = "10";
        public string StartStep { get; set; } = "1";
        public string Case { get; set; } = "1";
    }

    public class BatchPreset
    {
        public string Name { get; set; } = "";
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public List<BatchJobDto> Jobs { get; set; } = new();


        public string DisplayText => $"{Name}  ({Jobs.Count} заданий)";
    }
}