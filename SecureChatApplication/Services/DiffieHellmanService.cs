using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;

namespace SecureChatApplication.Services;

public sealed class KeyExchangeService : IDisposable
{
    private readonly ConcurrentDictionary<string, ECDiffieHellman> _privateKeysByPartner = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, string> _publicKeysByPartner = new(StringComparer.Ordinal);
    private bool _disposed;

    public string GeneratePublicKey(string partnerUsername)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerUsername);

        var ecdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);

        if (_privateKeysByPartner.TryRemove(partnerUsername, out var previousKey))
        {
            previousKey.Dispose();
        }

        _privateKeysByPartner[partnerUsername] = ecdh;

        var publicKey = Convert.ToBase64String(ecdh.ExportSubjectPublicKeyInfo());
        _publicKeysByPartner[partnerUsername] = publicKey;
        return publicKey;
    }

    public byte[] DeriveSharedKey(string partnerUsername, string partnerPublicKeyBase64, string localUsername)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerUsername);
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerPublicKeyBase64);
        ArgumentException.ThrowIfNullOrWhiteSpace(localUsername);

        if (!_privateKeysByPartner.TryGetValue(partnerUsername, out var localEcdh))
        {
            throw new InvalidOperationException($"No local key material exists for partner '{partnerUsername}'.");
        }

        var partnerPublicKeyBytes = Convert.FromBase64String(partnerPublicKeyBase64);
        using var partnerEcdh = ECDiffieHellman.Create(ECCurve.NamedCurves.nistP256);
        partnerEcdh.ImportSubjectPublicKeyInfo(partnerPublicKeyBytes, out _);

        var sharedSecret = localEcdh.DeriveKeyMaterial(partnerEcdh.PublicKey);
        try
        {
            return DeriveAesKey(sharedSecret, localUsername, partnerUsername);
        }
        finally
        {
            CryptographicOperations.ZeroMemory(sharedSecret);
        }
    }

    public bool HasKeyPairFor(string partnerUsername)
    {
        ThrowIfDisposed();
        return _privateKeysByPartner.ContainsKey(partnerUsername);
    }

    public string GetPublicKey(string partnerUsername)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerUsername);

        if (_publicKeysByPartner.TryGetValue(partnerUsername, out var publicKey))
        {
            return publicKey;
        }

        throw new InvalidOperationException($"No public key exists for partner '{partnerUsername}'.");
    }

    public static string ComputePublicKeyFingerprint(string publicKeyBase64)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(publicKeyBase64);

        var publicKeyBytes = Convert.FromBase64String(publicKeyBase64);
        var hash = SHA256.HashData(publicKeyBytes);
        return Convert.ToBase64String(hash);
    }

    public void RemoveKeyPair(string partnerUsername)
    {
        ThrowIfDisposed();
        ArgumentException.ThrowIfNullOrWhiteSpace(partnerUsername);

        if (_privateKeysByPartner.TryRemove(partnerUsername, out var privateKey))
        {
            privateKey.Dispose();
        }

        _publicKeysByPartner.TryRemove(partnerUsername, out _);
    }

    public void ClearAllKeys()
    {
        ThrowIfDisposed();

        foreach (var partner in _privateKeysByPartner.Keys.ToList())
        {
            RemoveKeyPair(partner);
        }
    }

    private static byte[] DeriveAesKey(byte[] sharedSecret, string userA, string userB)
    {
        var pair = string.CompareOrdinal(userA, userB) <= 0
            ? $"{userA}|{userB}"
            : $"{userB}|{userA}";

        var info = Encoding.UTF8.GetBytes($"SecureChat:AESGCM:{pair}");
        var salt = SHA256.HashData(Encoding.UTF8.GetBytes(pair));

        using var extract = new HMACSHA256(salt);
        var prk = extract.ComputeHash(sharedSecret);

        try
        {
            using var expand = new HMACSHA256(prk);
            var output = expand.ComputeHash([.. info, 0x01]);
            return output.AsSpan(0, 32).ToArray();
        }
        finally
        {
            CryptographicOperations.ZeroMemory(prk);
            CryptographicOperations.ZeroMemory(salt);
        }
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
        {
            throw new ObjectDisposedException(nameof(KeyExchangeService));
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        foreach (var key in _privateKeysByPartner.Values)
        {
            key.Dispose();
        }

        _privateKeysByPartner.Clear();
        _publicKeysByPartner.Clear();
        _disposed = true;
    }
}
