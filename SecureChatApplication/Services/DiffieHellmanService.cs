using System.Numerics;
using System.Security.Cryptography;

namespace SecureChatApplication.Services;

public sealed class DiffieHellmanService : IDisposable
{
    private static readonly BigInteger Prime = BigInteger.Parse("00FFFFFFFFFFFFFFFFC90FDAA22168C234C4C6628B80DC1CD129024E088A67CC74020BBEA63B139B22514A08798E3404DDEF9519B3CD3A431B302B0A6DF25F14374FE1356D6D51C245E485B576625E7EC6F44C42E9A637ED6B0BFF5CB6F406B7EDEE386BFB5A899FA5AE9F24117C4B1FE649286651ECE45B3DC2007CB8A163BF0598DA48361C55D39A69163FA8FD24CF5F83655D23DCA3AD961C62F356208552BB9ED529077096966D670C354E4ABC9804F1746C08CA18217C32905E462E36CE3BE39E772C180E86039B2783A2EC07A28FB5C55DF06F4C52C9DE2BCBF6955817183995497CEA956AE515D2261898FA051015728E5A8AACAA68FFFFFFFFFFFFFFFF", System.Globalization.NumberStyles.HexNumber);
    private static readonly BigInteger Generator = new BigInteger(2);

    private readonly Dictionary<string, BigInteger> _privateKeys = new();
    private readonly Dictionary<string, string> _publicKeys = new();
    private readonly object _lock = new();
    private bool _disposed;
    private string? _ownUsername;

    public void SetOwnUsername(string username)
    {
        _ownUsername = username;
    }

    /// <summary>
    /// Generates a Diffie-Hellman key pair for a specific partner.
    /// </summary>
    /// <param name="partnerUsername">The username of the chat partner.</param>
    /// <returns>Base64-encoded public key to send to the partner.</returns>
    public string GenerateKeyPair(string partnerUsername)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (_privateKeys.ContainsKey(partnerUsername))
            {
                _privateKeys.Remove(partnerUsername);
            }

            byte[] privateKeyBytes = new byte[256];
            RandomNumberGenerator.Fill(privateKeyBytes);
            privateKeyBytes[255] &= 0x7F;
            
            BigInteger privateKey = new BigInteger(privateKeyBytes, isUnsigned: true);
            _privateKeys[partnerUsername] = privateKey;

            BigInteger publicKey = BigInteger.ModPow(Generator, privateKey, Prime);
            string publicKeyBase64 = Convert.ToBase64String(publicKey.ToByteArray());
            
            // Store our public key for this partner
            _publicKeys[partnerUsername] = publicKeyBase64;
            
            return publicKeyBase64;
        }
    }

    /// <summary>
    /// Derives a shared AES-256 key from the partner's public key.
    /// </summary>
    /// <param name="partnerUsername">The username of the chat partner.</param>
    /// <param name="partnerPublicKeyBase64">Base64-encoded public key from the partner.</param>
    /// <returns>32-byte AES-256 key derived from the shared secret.</returns>
    public byte[] DeriveSharedKey(string partnerUsername, string partnerPublicKeyBase64)
    {
        ThrowIfDisposed();

        lock (_lock)
        {
            if (!_privateKeys.TryGetValue(partnerUsername, out var ourPrivateKey))
            {
                throw new InvalidOperationException(
                    $"No private key found for partner: {partnerUsername}. Call GenerateKeyPair first.");
            }

            // Parse partner's public key
            byte[] partnerPublicKeyBytes = Convert.FromBase64String(partnerPublicKeyBase64);
            BigInteger partnerPublicKey = new BigInteger(partnerPublicKeyBytes, isUnsigned: true);

            // Validate partner's public key is in valid range
            if (partnerPublicKey <= BigInteger.One || partnerPublicKey >= Prime)
            {
                throw new ArgumentException("Invalid partner public key: out of valid range.", nameof(partnerPublicKeyBase64));
            }

            // Compute shared secret: s = (partner_public_key ^ our_private_key) mod prime
            BigInteger sharedSecret = BigInteger.ModPow(partnerPublicKey, ourPrivateKey, Prime);

            // Convert to byte array (big-endian for consistency)
            byte[] sharedSecretBytes = sharedSecret.ToByteArray(isUnsigned: true, isBigEndian: true);

            // Derive 32-byte AES key using SHA-256
            byte[] aesKey = SHA256.HashData(sharedSecretBytes);

            // Clear shared secret from memory
            CryptographicOperations.ZeroMemory(sharedSecretBytes);

            return aesKey;
        }
    }

    /// <summary>
    /// Checks if a key pair exists for the specified partner.
    /// </summary>
    /// <param name="partnerUsername">The username of the chat partner.</param>
    /// <returns>True if a key pair exists, false otherwise.</returns>
    public bool HasKeyPairFor(string partnerUsername)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            return _privateKeys.ContainsKey(partnerUsername);
        }
    }

    /// <summary>
    /// Gets the stored public key for a partner (if we already generated one).
    /// </summary>
    /// <param name="partnerUsername">The username of the chat partner.</param>
    /// <returns>Base64-encoded public key.</returns>
    public string GetPublicKey(string partnerUsername)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            if (!_publicKeys.TryGetValue(partnerUsername, out var publicKey))
            {
                throw new InvalidOperationException(
                    $"No public key found for partner: {partnerUsername}. Call GenerateKeyPair first.");
            }
            return publicKey;
        }
    }

    /// <summary>
    /// Removes the key pair for a specific partner.
    /// </summary>
    /// <param name="partnerUsername">The username of the chat partner.</param>
    public void RemoveKeyPair(string partnerUsername)
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            _privateKeys.Remove(partnerUsername);
            _publicKeys.Remove(partnerUsername);
        }
    }

    /// <summary>
    /// Clears all stored key pairs.
    /// </summary>
    public void ClearAllKeys()
    {
        ThrowIfDisposed();
        lock (_lock)
        {
            _privateKeys.Clear();
            _publicKeys.Clear();
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(DiffieHellmanService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        lock (_lock)
        {
            _privateKeys.Clear();
            _publicKeys.Clear();
            _disposed = true;
        }
    }
}
