using System.Security.Cryptography;

namespace SecureChatApplication.Services;

/// <summary>
/// Service for AES-CBC encryption.
/// 
/// SECURITY OVERVIEW:
/// - Uses AES-256-CBC (Cipher Block Chaining mode)
/// - Requires separate IV (Initialization Vector) for each encryption
/// - IV must be random and unique for each message
/// - Pads messages to block size using PKCS#7 padding
/// 
/// AES-CBC ENCRYPTION PROCESS:
/// 1. Generate random 16-byte IV using secure RNG
/// 2. Encrypt plaintext with AES-256-CBC using IV and key
/// 3. Package: IV || Ciphertext
/// 
/// AES-CBC DECRYPTION PROCESS:
/// 1. Extract IV and ciphertext from package
/// 2. Decrypt ciphertext with AES-256-CBC using IV and key
/// 3. Remove padding to obtain plaintext
/// </summary>
public sealed class AesEncryptionService
{
    // AES-256 key size: 32 bytes (256 bits)
    private const int KeySize = 32;

    /// <summary>
    /// Encrypts a plaintext message using AES-256-CBC.
    /// </summary>
    /// <param name="plaintext">The message to encrypt.</param>
    /// <param name="key">The AES-256 key (32 bytes).</param>
    /// <returns>Tuple of (ciphertext, iv) both Base64-encoded.</returns>
    /// <exception cref="ArgumentException">If key is not 32 bytes.</exception>
    public (string Ciphertext, string IV) Encrypt(string plaintext, byte[] key)
    {
        ValidateKey(key);

        // Convert plaintext to bytes using UTF-8 encoding
        byte[] plaintextBytes = System.Text.Encoding.UTF8.GetBytes(plaintext);

        using var aes = Aes.Create();
        aes.Key = key;
        aes.Mode = CipherMode.CBC;
        aes.Padding = PaddingMode.PKCS7;
        aes.GenerateIV();

        byte[] iv = aes.IV;

        using var encryptor = aes.CreateEncryptor();
        byte[] ciphertext = encryptor.TransformFinalBlock(plaintextBytes, 0, plaintextBytes.Length);

        // Clear plaintext from memory
        CryptographicOperations.ZeroMemory(plaintextBytes);

        // Return Base64-encoded components
        return (
            Ciphertext: Convert.ToBase64String(ciphertext),
            IV: Convert.ToBase64String(iv)
        );
    }

    /// <summary>
    /// Decrypts an AES-256-CBC encrypted message.
    /// </summary>
    /// <param name="ciphertextBase64">Base64-encoded ciphertext.</param>
    /// <param name="ivBase64">Base64-encoded IV (16 bytes).</param>
    /// <param name="key">The AES-256 key (32 bytes).</param>
    /// <returns>The decrypted plaintext message.</returns>
    /// <exception cref="ArgumentException">If key is not 32 bytes.</exception>
    /// <exception cref="CryptographicException">If decryption or padding removal fails.</exception>
    public string Decrypt(string ciphertextBase64, string ivBase64, byte[] key)
    {
        ValidateKey(key);

        // Decode Base64 inputs
        byte[] ciphertext = Convert.FromBase64String(ciphertextBase64);
        byte[] iv = Convert.FromBase64String(ivBase64);

        // Validate IV size
        if (iv.Length != 16)
        {
            throw new ArgumentException("IV must be 16 bytes.", nameof(ivBase64));
        }

        // Allocate buffer for decrypted plaintext
        byte[] plaintext = new byte[ciphertext.Length];

        try
        {
            using var aes = Aes.Create();
            aes.Key = key;
            aes.Mode = CipherMode.CBC;
            aes.Padding = PaddingMode.PKCS7;
            aes.IV = iv;

            using var decryptor = aes.CreateDecryptor();
            plaintext = decryptor.TransformFinalBlock(ciphertext, 0, ciphertext.Length);

            // Convert decrypted bytes back to string
            string result = System.Text.Encoding.UTF8.GetString(plaintext);

            return result;
        }
        finally
        {
            // Clear sensitive data from memory
            if (plaintext != null)
            {
                CryptographicOperations.ZeroMemory(plaintext);
            }
        }
    }

    /// <summary>
    /// Validates that the key is the correct size for AES-256.
    /// </summary>
    private static void ValidateKey(byte[] key)
    {
        if (key == null)
        {
            throw new ArgumentNullException(nameof(key), "Encryption key cannot be null.");
        }
        if (key.Length != KeySize)
        {
            throw new ArgumentException(
                $"Key must be exactly {KeySize} bytes (256 bits) for AES-256. Got {key.Length} bytes.",
                nameof(key));
        }
    }

    /// <summary>
    /// Generates a cryptographically secure random AES-256 key.
    /// Use this only for testing - in production, use DiffieHellmanService to derive keys.
    /// </summary>
    /// <returns>A random 32-byte key.</returns>
    public static byte[] GenerateRandomKey()
    {
        byte[] key = new byte[KeySize];
        RandomNumberGenerator.Fill(key);
        return key;
    }
}
