using System;

namespace ProcessManagerLab.Utilities
{
    // Работа с битовой маской CPU Affinity.
    // Бит i = 1 означает, что процессу разрешено выполняться на ядре i
    public static class AffinityHelper
    {
        // Включено ли ядро coreIndex в маске
        public static bool IsCoreEnabled(IntPtr mask, int coreIndex)
        {
            long maskValue = mask.ToInt64();
            return (maskValue & (1L << coreIndex)) != 0;
        }

        // Маска из массива чекбоксов (true = ядро включено)
        public static IntPtr SetCoreMask(bool[] selectedCores)
        {
            long mask = 0;
            for (int i = 0; i < selectedCores.Length; i++)
            {
                if (selectedCores[i])
                    mask |= (1L << i);   // ставим 1 в нужный бит
            }
            return new IntPtr(mask);
        }

        // Маска в двоичном виде, например 00001111
        public static string ToBinary(IntPtr mask, int coreCount)
        {
            return Convert.ToString(mask.ToInt64(), 2).PadLeft(coreCount, '0');
        }

        // Маска в шестнадцатеричном виде, например 0xF
        public static string ToHex(IntPtr mask)
        {
            return "0x" + mask.ToInt64().ToString("X");
        }

        // Строка для показа: 00001111 (0xF)
        public static string GetMaskAsString(IntPtr mask, int coreCount)
        {
            return $"{ToBinary(mask, coreCount)} ({ToHex(mask)})";
        }
    }
}
