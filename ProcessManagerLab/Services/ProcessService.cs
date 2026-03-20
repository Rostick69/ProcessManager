using ProcessManagerLab.Models;
using ProcessManagerLab.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Windows;

namespace ProcessManagerLab.Services
{
    public class ProcessService
    {
        // Главный метод — возвращает список всех процессов, которые мы можем увидеть
        public List<ProcessInfo> GetAllProcesses()
        {
            var list = new List<ProcessInfo>();

            // Получаем ВСЕ процессы в системе
            Process[] processes = Process.GetProcesses();

            foreach (Process p in processes)
            {
                try
                {
                    // Некоторые системные процессы мы не можем прочитать — будет исключение
                    // Поэтому оборачиваем в try-catch и просто пропускаем проблемные
                    list.Add(new ProcessInfo
                    {
                        Id = p.Id,
                        Name = p.ProcessName,
                        Priority = p.PriorityClass,  // Теперь настоящий enum
                        MemoryUsage = p.WorkingSet64 / 1024 / 1024, // переводим байты в мегабайты — удобнее читать
                        ThreadCount = p.Threads.Count,
                        CpuTime = p.TotalProcessorTime,         // уже готовый TimeSpan
                        CoreCount = Environment.ProcessorCount,
                        ProcessorAffinity = p.ProcessorAffinity,
                        AffinityMaskString = Utilities.AffinityHelper.GetMaskAsString(p.ProcessorAffinity, Environment.ProcessorCount)
                    });
                }
                catch
                {
                    // Просто пропускаем процесс, если нет доступа
                    // (часто это системные процессы типа csrss, smss и т.д.)
                }
            }

            return list;
        }
        public bool SetProcessPriority(int processId, ProcessPriorityClass newPriority)
        {
            try
            {
                var process = Process.GetProcessById(processId);  // Находим процесс по PID
                process.PriorityClass = newPriority;             // Меняем приоритет
                return true;                                     // Успех
            }
            catch (Exception ex)
            {
                // Ошибки: обычно Win32Exception (нет прав) или InvalidOperation (процесс умер)
                MessageBox.Show($"Ошибка изменения приоритета: {ex.Message}");
                return false;
            }
        }
        // Получаем текущую маску и количество ядер
        public (IntPtr affinity, int coreCount) GetProcessorAffinity(int processId)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                return (process.ProcessorAffinity, Environment.ProcessorCount);
            }
            catch
            {
                return (new IntPtr(0), Environment.ProcessorCount);
            }
        }

        // Меняем CPU Affinity
        public bool SetProcessorAffinity(int processId, IntPtr newMask)
        {
            try
            {
                var process = Process.GetProcessById(processId);
                process.ProcessorAffinity = newMask;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка изменения CPU Affinity: {ex.Message}\n\n(Часто это значит, что нет прав)");
                return false;
            }
        }
        public bool SetPriority(int pid, ProcessPriorityClass priority)
        {
            try
            {
                Process.GetProcessById(pid).PriorityClass = priority;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка изменения приоритета:\n{ex.Message}");
                return false;
            }
        }

        public bool SetAffinity(int pid, IntPtr mask)
        {
            try
            {
                Process.GetProcessById(pid).ProcessorAffinity = mask;
                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show($"Ошибка изменения Affinity:\n{ex.Message}");
                return false;
            }
        }
    }
}