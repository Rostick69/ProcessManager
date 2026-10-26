using System;
using System.Collections.Generic;
using System.Diagnostics;

namespace ProcessManagerLab.Models
{
    // Модель данных для хранения информации о процессе.
    // Заполняется в фоновом потоке и потом отображается в таблице и дереве
    public class ProcessInfo
    {
        public int Id { get; set; }                    // PID — идентификатор процесса
        public string Name { get; set; }               // Имя (например chrome, explorer)
        public int ParentId { get; set; }              // PID родительского процесса (0 — нет данных)
        public int SessionId { get; set; }             // Сессия: 0 — системные процессы и службы

        // Приоритет. null — нет прав на чтение (системные процессы)
        public ProcessPriorityClass? Priority { get; set; }
        public string PriorityString => Priority.HasValue ? Priority.Value.ToString() : "нет доступа";

        public long MemoryMB { get; set; }             // Рабочий набор, МБ
        public int ThreadCount { get; set; }           // Количество потоков
        public TimeSpan? CpuTime { get; set; }         // Процессорное время (null — нет доступа)
        public string CpuTimeString => CpuTime.HasValue ? CpuTime.Value.ToString(@"hh\:mm\:ss") : "—";
        public double CpuPercent { get; set; }         // Загрузка CPU за последний интервал, %

        public bool HasWindow { get; set; }            // Есть ли у процесса главное окно (GUI)
        public bool IsSystem => SessionId == 0;        // Системный процесс (служба, ядро)

        // CPU Affinity: null — нет прав на чтение маски
        public IntPtr? ProcessorAffinity { get; set; }

        // Дочерние процессы (заполняются при построении дерева)
        public List<ProcessInfo> Children { get; } = new List<ProcessInfo>();
        public int ChildCount => Children.Count;
    }
}
