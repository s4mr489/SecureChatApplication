using SecureChatApplication.Models;
using SecureChatApplication.Services;
using System.Security.Cryptography;

namespace SecureChatApplication.Examples;

public static class EncryptionExample
{
    /// <summary>
    /// Demonstrates complete end-to-end encryption between Alice and Bob.
    /// </summary>
    public static void DemonstrateCompleteWorkflow()
    {
        Console.WriteLine("=== ECDH + HKDF + AES-GCM Encryption Demo ===\n");

        using var aliceKeyExchange = new KeyExchangeService();
        var aliceCrypto = new CryptoService();

        using var bobKeyExchange = new KeyExchangeService();
        var bobCrypto = new CryptoService();

        Console.WriteLine("Step 1: Key Exchange");
        Console.WriteLine("--------------------");

        // Alice generates her key pair for Bob
        var alicePublicKey = aliceKeyExchange.GeneratePublicKey("Bob");
        Console.WriteLine($"Alice generated public key: {alicePublicKey.Substring(0, 32)}...");

        // Bob generates his key pair for Alice
        var bobPublicKey = bobKeyExchange.GeneratePublicKey("Alice");
        Console.WriteLine($"Bob generated public key: {bobPublicKey.Substring(0, 32)}...");

        // Both derive the shared secret
        var aliceKey = aliceKeyExchange.DeriveSharedKey("Bob", bobPublicKey, "Alice");
        var bobKey = bobKeyExchange.DeriveSharedKey("Alice", alicePublicKey, "Bob");

        Console.WriteLine($"\nAlice's shared key: {Convert.ToBase64String(aliceKey).Substring(0, 32)}...");
        Console.WriteLine($"Bob's shared key: {Convert.ToBase64String(bobKey).Substring(0, 32)}...");
        Console.WriteLine($"Keys match: {aliceKey.SequenceEqual(bobKey)} ?\n");

        Console.WriteLine("Step 2: Alice Sends Encrypted Message to Bob");
        Console.WriteLine("---------------------------------------------");

        // Alice encrypts a message
        string aliceMessage = "Hello Bob, this is authenticated and encrypted.";
        Console.WriteLine($"Alice's plaintext: \"{aliceMessage}\"");

        var encrypted = aliceCrypto.Encrypt(aliceMessage, aliceKey);
        Console.WriteLine($"Encrypted ciphertext: {encrypted.Ciphertext.Substring(0, 40)}...");
        Console.WriteLine($"Nonce: {encrypted.Nonce}");
        Console.WriteLine($"Tag: {encrypted.Tag}");

        // Create the encrypted message packet (what goes over the network)
        var payload = new EncryptedMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SenderUsername = "Alice",
            RecipientUsername = "Bob",
            Ciphertext = encrypted.Ciphertext,
            Nonce = encrypted.Nonce,
            Tag = encrypted.Tag,
            Timestamp = DateTime.UtcNow
        };

        Console.WriteLine("\n[Server relays encrypted message - cannot read it!]");

        // Bob receives and decrypts
        string decryptedMessage = bobCrypto.Decrypt(payload.Ciphertext, payload.Nonce, payload.Tag, bobKey);

        Console.WriteLine($"\nBob decrypted: \"{decryptedMessage}\"");
        Console.WriteLine($"Messages match: {aliceMessage == decryptedMessage} ?\n");

        Console.WriteLine("Step 3: Bob Sends Encrypted Reply to Alice");
        Console.WriteLine("------------------------------------------");

        // Bob encrypts a reply
        string bobMessage = "Hi Alice! I received your secret message.";
        Console.WriteLine($"Bob's plaintext: \"{bobMessage}\"");

        var bobEncrypted = bobCrypto.Encrypt(bobMessage, bobKey);
        Console.WriteLine($"Encrypted ciphertext: {bobEncrypted.Ciphertext.Substring(0, 40)}...");
        Console.WriteLine($"Nonce: {bobEncrypted.Nonce}");
        Console.WriteLine($"Tag: {bobEncrypted.Tag}");

        // Alice receives and decrypts
        string aliceDecrypted = aliceCrypto.Decrypt(bobEncrypted.Ciphertext, bobEncrypted.Nonce, bobEncrypted.Tag, aliceKey);
        Console.WriteLine($"\nAlice decrypted: \"{aliceDecrypted}\"");
        Console.WriteLine($"Messages match: {bobMessage == aliceDecrypted} ?\n");

        Console.WriteLine("Step 4: Security Verification (All good! GCM provides integrity)");
        Console.WriteLine("-----------------------------");

        // Demonstrate key isolation (Alice can't decrypt messages meant for another user)
        using var charlieKeyExchange = new KeyExchangeService();
        var charliePublicKey = charlieKeyExchange.GeneratePublicKey("Bob");
        var charlieKey = charlieKeyExchange.DeriveSharedKey("Bob", bobPublicKey, "Charlie");

        Console.WriteLine($"\nCharlie's shared key (with Bob): {Convert.ToBase64String(charlieKey).Substring(0, 32)}...");
        Console.WriteLine($"Charlie's key != Alice's key: {!charlieKey.SequenceEqual(aliceKey)} ?");

        try
        {
            // Charlie tries to decrypt Alice's message - will fail!
            var charlieCrypto = new CryptoService();
            string attemptedDecrypt = charlieCrypto.Decrypt(encrypted.Ciphertext, encrypted.Nonce, encrypted.Tag, charlieKey);
            Console.WriteLine("Charlie SHOULD NOT be able to decrypt!");
        }
        catch (CryptographicException)
        {
            Console.WriteLine("Charlie cannot decrypt Alice's message: ? (Expected!)");
        }

        // Cleanup - zero out keys from memory
        CryptographicOperations.ZeroMemory(aliceKey);
        CryptographicOperations.ZeroMemory(bobKey);
        CryptographicOperations.ZeroMemory(charlieKey);

        Console.WriteLine("\n=== Demo Complete ===");
        Console.WriteLine("\nKey Takeaways:");
        Console.WriteLine("? Alice and Bob derive the same shared key without transmitting it");
        Console.WriteLine("? All messages are encrypted with AES-GCM");
        Console.WriteLine("? Server cannot decrypt messages (only relays ciphertext)");
        Console.WriteLine("? Each user pair has a unique shared key");
        Console.WriteLine("? Sensitive keys are zeroed from memory after use");
    }

    /// <summary>
    /// Shows what data is actually transmitted over the network.
    /// </summary>
    public static void ShowNetworkData()
    {
        Console.WriteLine("\n=== Network Transmission Example ===\n");

        using var aliceKeyExchange = new KeyExchangeService();
        var aliceCrypto = new CryptoService();

        using var bobKeyExchange = new KeyExchangeService();

        // Step 1: Key Exchange Messages (transmitted in plaintext, but that's OK!)
        var alicePublicKey = aliceKeyExchange.GeneratePublicKey("Bob");
        var bobPublicKey = bobKeyExchange.GeneratePublicKey("Alice");

        var keyExchangeMessage = new KeyExchangeMessage
        {
            SenderUserId = "Alice",
            SenderUsername = "Alice",
            RecipientUsername = "Bob",
            PublicKey = alicePublicKey,
            PublicKeyFingerprint = KeyExchangeService.ComputePublicKeyFingerprint(alicePublicKey),
            Timestamp = DateTime.UtcNow
        };

        Console.WriteLine("Key Exchange Message (visible to server):");
        Console.WriteLine($"  From: {keyExchangeMessage.SenderUsername}");
        Console.WriteLine($"  To: {keyExchangeMessage.RecipientUsername}");
        Console.WriteLine($"  Public Key: {keyExchangeMessage.PublicKey.Substring(0, 50)}...");
        Console.WriteLine($"  (Server can see this, but cannot derive shared key!)\n");

        // Step 2: Encrypted Message (content is hidden!)
        var sharedKey = aliceKeyExchange.DeriveSharedKey("Bob", bobPublicKey, "Alice");
        var encrypted = aliceCrypto.Encrypt("This is my secret message!", sharedKey);

        var encryptedMessage = new EncryptedMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SenderUsername = "Alice",
            RecipientUsername = "Bob",
            Ciphertext = encrypted.Ciphertext,
            Nonce = encrypted.Nonce,
            Tag = encrypted.Tag,
            Timestamp = DateTime.UtcNow
        };

        Console.WriteLine("Encrypted Message (transmitted to server):");
        Console.WriteLine($"  Message ID: {encryptedMessage.MessageId}");
        Console.WriteLine($"  From: {encryptedMessage.SenderUsername}");
        Console.WriteLine($"  To: {encryptedMessage.RecipientUsername}");
        Console.WriteLine($"  Ciphertext: {encryptedMessage.Ciphertext}");
        Console.WriteLine($"  Nonce: {encryptedMessage.Nonce}");
        Console.WriteLine($"  Tag: {encryptedMessage.Tag}");
        Console.WriteLine($"  Timestamp: {encryptedMessage.Timestamp}");
        Console.WriteLine($"\n  ?? Server CANNOT read the actual message content!");
        Console.WriteLine($"  ? Only Bob can decrypt this using his shared key\n");

        CryptographicOperations.ZeroMemory(sharedKey);
    }

    /// <summary>
    /// Runs all examples. Call this from your application startup or tests.
    /// </summary>
    public static void RunAllExamples()
    {
        DemonstrateCompleteWorkflow();
        ShowNetworkData();
    }
}
