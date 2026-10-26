using ProcessManagerLab.Models;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.Linq;
using System.Management;

namespace ProcessManagerLab.Services
{
    // Работа с процессами через System.Diagnostics.Process.
    // Методы сервиса не показывают окна сами — ошибки возвращаются вызывающему коду,
    // а он уже решает, что показать пользователю
    public class ProcessService
    {
        // Процессорное время каждого процесса при прошлом обновлении — для расчёта загрузки CPU в %
        private readonly Dictionary<int, TimeSpan> _previousCpu = new Dictionary<int, TimeSpan>();
        private DateTime _previousTime = DateTime.UtcNow;

        public int CoreCount => Environment.ProcessorCount;

        // Список всех процессов. Вызывается в фоновом потоке.
        // Процессы без доступа (System, csrss и т. п.) тоже попадают в список, просто без части полей
        public List<ProcessInfo> GetAllProcesses()
        {
            var list = new List<ProcessInfo>();
            var parents = ProcessParentHelper.GetAllParents(); // один WMI-запрос на все процессы

            DateTime now = DateTime.UtcNow;
            double elapsedMs = Math.Max((now - _previousTime).TotalMilliseconds, 1);
            _previousTime = now;

            foreach (Process p in Process.GetProcesses())
            {
                using (p)
                {
                    var info = new ProcessInfo
                    {
                        // Эти поля берутся из общего снимка системы и доступны для любого процесса
                        Id = p.Id,
                        Name = p.ProcessName,
                        SessionId = SafeGet(() => p.SessionId, 0),
                        MemoryMB = SafeGet(() => p.WorkingSet64, 0L) / 1024 / 1024,
                        ThreadCount = SafeGet(() => p.Threads.Count, 0),
                        ParentId = parents.TryGetValue(p.Id, out int parent) ? parent : 0
                    };

                    // Для этих полей нужно открыть процесс — у системных процессов будет отказ в доступе
                    try { info.Priority = p.PriorityClass; } catch (Win32Exception) { } catch (InvalidOperationException) { }
                    try { info.ProcessorAffinity = p.ProcessorAffinity; } catch (Win32Exception) { } catch (InvalidOperationException) { }
                    try { info.HasWindow = p.MainWindowHandle != IntPtr.Zero; } catch (InvalidOperationException) { }

                    try
                    {
                        TimeSpan cpu = p.TotalProcessorTime;
                        info.CpuTime = cpu;
                        // Загрузка = процессорное время за интервал / (интервал * число ядер)
                        if (_previousCpu.TryGetValue(p.Id, out TimeSpan prev))
                            info.CpuPercent = Math.Min(100, (cpu - prev).TotalMilliseconds / (elapsedMs * CoreCount) * 100);
                        _previousCpu[p.Id] = cpu;
                    }
                    catch (Win32Exception) { }
                    catch (InvalidOperationException) { }

                    list.Add(info);
                }
            }

            // Удаляем из словаря завершившиеся процессы
            var alive = new HashSet<int>(list.Select(x => x.Id));
            foreach (int pid in _previousCpu.Keys.Where(k => !alive.Contains(k)).ToList())
                _previousCpu.Remove(pid);

            return list;
        }

        private static T SafeGet<T>(Func<T> getter, T fallback)
        {
            try { return getter(); }
            catch { return fallback; }
        }

        // Изменение приоритета. Возвращает null при успехе или текст ошибки
        public string SetPriority(int pid, ProcessPriorityClass priority)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    p.PriorityClass = priority;
                    // Без прав администратора Windows может молча заменить RealTime на High — проверяем
                    p.Refresh();
                    if (priority == ProcessPriorityClass.RealTime && p.PriorityClass != ProcessPriorityClass.RealTime)
                        return $"Windows установила приоритет {p.PriorityClass} вместо RealTime (нужны права администратора)";
                }
                return null;
            }
            catch (Win32Exception ex) { return "Недостаточно прав: " + ex.Message; }
            catch (InvalidOperationException) { return "Процесс уже завершился"; }
            catch (ArgumentException) { return "Процесс с таким PID не найден"; }
        }

        // Изменение CPU Affinity. Возвращает null при успехе или текст ошибки
        public string SetAffinity(int pid, IntPtr mask)
        {
            if (mask == IntPtr.Zero)
                return "Нужно выбрать хотя бы одно ядро";
            try
            {
                using (var p = Process.GetProcessById(pid))
                    p.ProcessorAffinity = mask;
                return null;
            }
            catch (Win32Exception ex) { return "Недостаточно прав: " + ex.Message; }
            catch (InvalidOperationException) { return "Процесс уже завершился"; }
            catch (ArgumentException) { return "Процесс с таким PID не найден"; }
        }

        // Завершение процесса. Возвращает null при успехе или текст ошибки
        public string Kill(int pid)
        {
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    p.Kill();
                    p.WaitForExit(3000);
                }
                return null;
            }
            catch (Win32Exception ex) { return "Недостаточно прав: " + ex.Message; }
            catch (InvalidOperationException) { return "Процесс уже завершился"; }
            catch (ArgumentException) { return "Процесс с таким PID не найден"; }
        }

        // Список потоков процесса
        public List<ThreadInfo> GetThreads(int pid)
        {
            var result = new List<ThreadInfo>();
            try
            {
                using (var p = Process.GetProcessById(pid))
                {
                    foreach (ProcessThread t in p.Threads)
                    {
                        var ti = new ThreadInfo { Id = t.Id, State = t.ThreadState.ToString() };
                        // Приоритет и время потока тоже могут быть недоступны без прав
                        try { ti.Priority = t.PriorityLevel.ToString(); } catch { ti.Priority = "нет доступа"; }
                        try { ti.CpuTime = t.TotalProcessorTime.ToString(@"hh\:mm\:ss\.ff"); } catch { ti.CpuTime = "—"; }
                        if (t.ThreadState == ThreadState.Wait)
                            ti.State += $" ({t.WaitReason})";
                        result.Add(ti);
                    }
                }
            }
            catch
            {
                // Процесс завершился или нет доступа — вернём пустой список
            }
            return result;
        }

        // Загрузка каждого логического ядра в процентах (WMI, класс Win32_PerfFormattedData_PerfOS_Processor)
        public List<double> GetCoreLoads()
        {
            var loads = new double[CoreCount];
            try
            {
                using (var searcher = new ManagementObjectSearcher(
                    "SELECT Name, PercentProcessorTime FROM Win32_PerfFormattedData_PerfOS_Processor"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        string name = obj["Name"]?.ToString();
                        if (int.TryParse(name, out int core) && core < loads.Length)
                            loads[core] = Convert.ToDouble(obj["PercentProcessorTime"]);
                    }
                }
            }
            catch
            {
                // Счётчики недоступны — покажем нули
            }
            return loads.ToList();
        }
    }
}
