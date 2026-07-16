using System.Security.Cryptography;
using System.Text;

namespace TaskTracker.Core.GitHub
{
    /// <summary>
    /// Stores the GitHub token DPAPI-encrypted (current user) on Windows.
    /// Other platforms fall back to plain base64 — callers should surface the
    /// isPlaintext flag as a warning.
    /// </summary>
    public static class TokenProtector
    {
        public static (string ProtectedValue, bool IsPlaintext) Protect(string token)
        {
            var bytes = Encoding.UTF8.GetBytes(token);
            if (OperatingSystem.IsWindows())
            {
                var encrypted = ProtectedData.Protect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                return (Convert.ToBase64String(encrypted), false);
            }
            return (Convert.ToBase64String(bytes), true);
        }

        public static string? Unprotect(string? protectedValue, bool isPlaintext)
        {
            if (string.IsNullOrEmpty(protectedValue))
                return null;
            try
            {
                var bytes = Convert.FromBase64String(protectedValue);
                if (!isPlaintext && OperatingSystem.IsWindows())
                    bytes = ProtectedData.Unprotect(bytes, optionalEntropy: null, DataProtectionScope.CurrentUser);
                else if (!isPlaintext)
                    return null; // Windows-encrypted token cannot be read elsewhere.
                return Encoding.UTF8.GetString(bytes);
            }
            catch (FormatException)
            {
                return null;
            }
            catch (CryptographicException)
            {
                return null;
            }
        }
    }
}
