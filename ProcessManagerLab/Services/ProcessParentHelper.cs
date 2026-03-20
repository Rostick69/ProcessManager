using System;
using System.Management;

namespace ProcessManagerLab.Services
{
    public static class ProcessParentHelper
    {
        // Возвращает ID родительского процесса
        // Если не получилось (нет прав) — возвращаем 0
        public static int GetParentProcessId(int processId)
        {
            try
            {
                string query = $"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}";

                using (var searcher = new ManagementObjectSearcher(query))
                {
                    foreach (ManagementObject obj in searcher.Get())
                    {
                        return Convert.ToInt32(obj["ParentProcessId"]);
                    }
                }
            }
            catch { }
            return 0;
        }
    }
}