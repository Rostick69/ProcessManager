using System;

namespace ProcessManagerLab.Models
{
    // Информация об одном потоке процесса (для таблицы потоков)
    public class ThreadInfo
    {
        public int Id { get; set; }
        public string Priority { get; set; }
        public string State { get; set; }
        public string CpuTime { get; set; }
    }
}
