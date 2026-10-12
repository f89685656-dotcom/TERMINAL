using System;
using System.Threading;
using System.Windows;

namespace TERMINAL.Services
{
    public static class ClipboardHelper
    {
        /// <summary>
        /// Безопасно кладёт текст в буфер обмена, повторяя попытки при CLIPBRD_E_CANT_OPEN.
        /// </summary>
        public static bool SetText(string text, int retries = 5, int delayMs = 50)
        {
            for (int i = 0; i < retries; i++)
            {
                try
                {
                    Clipboard.SetText(text);
                    return true;
                }
                catch (Exception)
                {
                    // Пауза и повтор
                    Thread.Sleep(delayMs);
                }
            }

            // Не удалось после всех попыток
            return false;
        }
    }
}