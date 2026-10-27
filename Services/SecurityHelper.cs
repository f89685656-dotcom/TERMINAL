using System;
using System.Security.Cryptography;
using System.Text;

namespace TERMINAL
{
    public static class SecurityHelper
    {
        // Дополнительная соль для шифрования (можно поменять на любую строку)
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("TERMINAL_Secret_Key_2026");

        public static string EncryptString(string plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return "";
            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] cipherBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
                return Convert.ToBase64String(cipherBytes);
            }
            catch
            {
                return plainText; // Если не удалось зашифровать, возвращаем как есть
            }
        }

        public static string DecryptString(string cipherText)
        {
            if (string.IsNullOrEmpty(cipherText)) return "";
            try
            {
                // Проверяем, зашифрован ли текст (Base64 обычно длиннее 20 символов)
                // Если нет, возвращаем как есть (для обратной совместимости со старыми файлами)
                if (cipherText.Length < 20) return cipherText;

                byte[] cipherBytes = Convert.FromBase64String(cipherText);
                byte[] plainBytes = ProtectedData.Unprotect(cipherBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(plainBytes);
            }
            catch
            {
                return cipherText; // Если не удалось расшифровать (например, файл со старого ПК), возвращаем как есть
            }
        }

        public static byte[] EncryptBytes(byte[] data)
        {
            return ProtectedData.Protect(data, Entropy, DataProtectionScope.CurrentUser);
        }

        public static byte[] DecryptBytes(byte[] data)
        {
            return ProtectedData.Unprotect(data, Entropy, DataProtectionScope.CurrentUser);
        }
    }
}