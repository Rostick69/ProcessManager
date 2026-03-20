using System;

namespace ProcessManagerLab.Utilities
{
    // Этот класс помогает работать с битовой маской CPU Affinity
    public static class AffinityHelper
    {
        // Проверяет, включено ли конкретное ядро в маске
        public static bool IsCoreEnabled(IntPtr mask, int coreIndex)
        {
            long maskValue = mask.ToInt64();
            return (maskValue & (1L << coreIndex)) != 0;
        }

        // Создаёт новую маску из массива чекбоксов (true = ядро включено)
        public static IntPtr SetCoreMask(bool[] selectedCores)
        {
            long mask = 0;
            for (int i = 0; i < selectedCores.Length; i++)
            {
                if (selectedCores[i])
                {
                    mask |= (1L << i);   // ставим 1 в нужный бит
                }
            }
            return new IntPtr(mask);
        }

        // Превращает маску в красивую строку для показа (например: 00001111 (0xF))
        public static string GetMaskAsString(IntPtr mask, int coreCount)
        {
            long value = mask.ToInt64();
            string binary = Convert.ToString(value, 2).PadLeft(coreCount, '0');
            string hex = "0x" + value.ToString("X");
            return $"{binary} ({hex})";
        }
    }
}