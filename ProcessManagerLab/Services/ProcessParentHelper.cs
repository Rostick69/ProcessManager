using System;
using System.Collections.Generic;
using System.Management;

namespace ProcessManagerLab.Services
{
    // Получение родительских процессов через WMI (класс Win32_Process)
    public static class ProcessParentHelper
    {
        // ID родителя одного процесса (как в методичке). Если не получилось — 0
        public static int GetParentProcessId(int processId)
        {
            try
            {
                string query = $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}";
                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                        return Convert.ToInt32(obj["ParentProcessId"]);
                }
            }
            catch { }
            return 0;
        }

        // ID родителей сразу для всех процессов одним WMI-запросом.
        // Это намного быстрее, чем делать отдельный запрос на каждый процесс
        public static Dictionary<int, int> GetAllParents()
        {
            var result = new Dictionary<int, int>();
            try
            {
                using (var searcher = new ManagementObjectSearcher("SELECT ProcessId, ParentProcessId FROM Win32_Process"))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        int pid = Convert.ToInt32(obj["ProcessId"]);
                        int parent = Convert.ToInt32(obj["ParentProcessId"]);
                        result[pid] = parent;
                    }
                }
            }
            catch
            {
                // WMI недоступен — дерево будет плоским
            }
            return result;
        }
    }
}
