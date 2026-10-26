namespace ProcessManagerLab.Models
{
    // Один столбец простой диаграммы (загрузка ядра или память процесса)
    public class ChartBar
    {
        public string Label { get; set; }      // подпись (Ядро 0, chrome ...)
        public double Value { get; set; }      // значение (%, МБ)
        public string ValueText { get; set; }  // значение для вывода
        public double BarLength { get; set; }  // длина столбца в пикселях
    }
}
