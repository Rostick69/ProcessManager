using System;
using System.Collections.Generic;
using System.Diagnostics;


namespace ProcessManagerLab.Models
{
    // Это простая "коробочка" для хранения информации об одном процессе
    // Мы её будем показывать в таблице
    public class ProcessInfo
    {
        public int Id { get; set; }              // PID — номер процесса
        public string Name { get; set; }         // Имя (например chrome, explorer)
        public ProcessPriorityClass Priority { get; set; }  // Теперь это enum (настоящий тип из .NET)
        public string PriorityString => Priority.ToString();  // Вспомогательное свойство для показа в таблице (как строка)
        public long MemoryUsage { get; set; }    // Сколько памяти жрёт (в байтах)
        public int ThreadCount { get; set; }     // Сколько потоков внутри процесса
        public TimeSpan CpuTime { get; set; }    // Сколько времени процессор тратил на этот процесс
        // === Новое для CPU Affinity ===
        public IntPtr ProcessorAffinity { get; set; }          // текущая маска (IntPtr)
        public string AffinityMaskString { get; set; }         // строка для отображения (binary + hex)
        public int CoreCount { get; set; }                     // сколько ядер всего в системе
                                                               // Поля для дерева процессов
        public int ParentId { get; set; } = 0;
        public List<ProcessInfo> Children { get; set; } = new List<ProcessInfo>();

        // Для сохранения состояния при обновлении
        public IntPtr SavedAffinity { get; set; } = IntPtr.Zero;
        public bool[] SavedCoreSelection { get; set; } // сохраним галочки
    }
}