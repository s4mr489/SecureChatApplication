using System.Security.Cryptography;
using System.Text;

namespace SecureChatApplication.Services;

public sealed class CryptoService
{
    private const int KeySize = 32;
    private const int NonceSize = 12;
    private const int TagSize = 16;

    public (string Ciphertext, string Nonce, string Tag) Encrypt(string plaintext, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(plaintext);
        ValidateKey(key);

        var nonce = RandomNumberGenerator.GetBytes(NonceSize);
        var plaintextBytes = Encoding.UTF8.GetBytes(plaintext);
        var ciphertext = new byte[plaintextBytes.Length];
        var tag = new byte[TagSize];

        try
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Encrypt(nonce, plaintextBytes, ciphertext, tag);

            return (
                Ciphertext: Convert.ToBase64String(ciphertext),
                Nonce: Convert.ToBase64String(nonce),
                Tag: Convert.ToBase64String(tag));
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    public string Decrypt(string ciphertextBase64, string nonceBase64, string tagBase64, byte[] key)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(ciphertextBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(nonceBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(tagBase64);
        ValidateKey(key);

        var ciphertext = Convert.FromBase64String(ciphertextBase64);
        var nonce = Convert.FromBase64String(nonceBase64);
        var tag = Convert.FromBase64String(tagBase64);

        if (nonce.Length != NonceSize)
        {
            throw new ArgumentException("Nonce must be 12 bytes for AES-GCM.", nameof(nonceBase64));
        }

        if (tag.Length != TagSize)
        {
            throw new ArgumentException("Tag must be 16 bytes for AES-GCM.", nameof(tagBase64));
        }

        var plaintextBytes = new byte[ciphertext.Length];

        try
        {
            using var aesGcm = new AesGcm(key, TagSize);
            aesGcm.Decrypt(nonce, ciphertext, tag, plaintextBytes);
            return Encoding.UTF8.GetString(plaintextBytes);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(plaintextBytes);
        }
    }

    private static void ValidateKey(byte[] key)
    {
        ArgumentNullException.ThrowIfNull(key);
        if (key.Length != KeySize)
        {
            throw new ArgumentException($"Key must be exactly {KeySize} bytes.", nameof(key));
        }
    }
}

public sealed class AesEncryptionService
{
    private readonly CryptoService _cryptoService = new();

    public (string Ciphertext, string Nonce, string Tag) Encrypt(string plaintext, byte[] key) => _cryptoService.Encrypt(plaintext, key);

    public string Decrypt(string ciphertextBase64, string nonceBase64, string tagBase64, byte[] key) =>
        _cryptoService.Decrypt(ciphertextBase64, nonceBase64, tagBase64, key);
}
