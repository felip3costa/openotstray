using System.Security.Cryptography;
using System.Text;

namespace OpenOTSTray.Services;

/// <summary>
/// Encrypts/decrypts one-time secret links at rest using Windows DPAPI, scoped to the
/// current Windows user. A blob protected on one machine (or by another user account)
/// cannot be decrypted elsewhere — Unprotect returns "" instead of throwing, so a moved
/// or tampered history file just shows the link as unavailable rather than crashing.
/// </summary>
public static class LinkProtector
{
    // Extra entropy so this app's blobs can't be decrypted by some other CurrentUser-scoped
    // DPAPI consumer running as the same Windows user with no entropy of its own.
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("OpenOTSTray.LinkProtector.v1");

    public static string? Protect(string plainText)
    {
        if (string.IsNullOrEmpty(plainText))
            return null;

        var plainBytes = Encoding.UTF8.GetBytes(plainText);
        var protectedBytes = ProtectedData.Protect(plainBytes, Entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(protectedBytes);
    }

    public static string Unprotect(string? protectedBase64)
    {
        if (string.IsNullOrEmpty(protectedBase64))
            return "";

        try
        {
            var protectedBytes = Convert.FromBase64String(protectedBase64);
            var plainBytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plainBytes);
        }
        catch
        {
            return "";
        }
    }
}
