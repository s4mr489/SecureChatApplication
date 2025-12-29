using SecureChatApplication.Models;
using SecureChatApplication.Services;
using System.Security.Cryptography;

namespace SecureChatApplication.Examples;

/// <summary>
/// Complete example demonstrating Diffie-Hellman key exchange with AES encryption.
/// This shows the full workflow from key exchange to encrypted messaging.
/// </summary>
public class EncryptionExample
{
    /// <summary>
    /// Demonstrates complete end-to-end encryption between Alice and Bob.
    /// </summary>
    public static void DemonstrateCompleteWorkflow()
    {
        Console.WriteLine("=== Diffie-Hellman + AES Encryption Demo ===\n");

        // Initialize services for Alice
        using var aliceDH = new DiffieHellmanService();
        var aliceAES = new AesEncryptionService();
        aliceDH.SetOwnUsername("Alice");

        // Initialize services for Bob
        using var bobDH = new DiffieHellmanService();
        var bobAES = new AesEncryptionService();
        bobDH.SetOwnUsername("Bob");

        Console.WriteLine("Step 1: Key Exchange");
        Console.WriteLine("--------------------");

        // Alice generates her key pair for Bob
        string alicePublicKey = aliceDH.GenerateKeyPair("Bob");
        Console.WriteLine($"Alice generated public key: {alicePublicKey.Substring(0, 32)}...");

        // Bob generates his key pair for Alice
        string bobPublicKey = bobDH.GenerateKeyPair("Alice");
        Console.WriteLine($"Bob generated public key: {bobPublicKey.Substring(0, 32)}...");

        // Both derive the shared secret
        byte[] aliceSharedKey = aliceDH.DeriveSharedKey("Bob", bobPublicKey);
        byte[] bobSharedKey = bobDH.DeriveSharedKey("Alice", alicePublicKey);

        Console.WriteLine($"\nAlice's shared key: {Convert.ToBase64String(aliceSharedKey).Substring(0, 32)}...");
        Console.WriteLine($"Bob's shared key: {Convert.ToBase64String(bobSharedKey).Substring(0, 32)}...");
        Console.WriteLine($"Keys match: {aliceSharedKey.SequenceEqual(bobSharedKey)} ?\n");

        Console.WriteLine("Step 2: Alice Sends Encrypted Message to Bob");
        Console.WriteLine("---------------------------------------------");

        // Alice encrypts a message
        string aliceMessage = "Hello Bob! This is a secret message from Alice.";
        Console.WriteLine($"Alice's plaintext: \"{aliceMessage}\"");

        var (ciphertext, iv) = aliceAES.Encrypt(aliceMessage, aliceSharedKey);
        Console.WriteLine($"Encrypted ciphertext: {ciphertext.Substring(0, 40)}...");
        Console.WriteLine($"IV: {iv}");

        // Create the encrypted message packet (what goes over the network)
        var encryptedMessage = new EncryptedMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SenderUsername = "Alice",
            RecipientUsername = "Bob",
            Ciphertext = ciphertext,
            IV = iv,
            Timestamp = DateTime.UtcNow
        };

        Console.WriteLine("\n[Server relays encrypted message - cannot read it!]");

        // Bob receives and decrypts
        string decryptedMessage = bobAES.Decrypt(
            encryptedMessage.Ciphertext,
            encryptedMessage.IV,
            bobSharedKey
        );

        Console.WriteLine($"\nBob decrypted: \"{decryptedMessage}\"");
        Console.WriteLine($"Messages match: {aliceMessage == decryptedMessage} ?\n");

        Console.WriteLine("Step 3: Bob Sends Encrypted Reply to Alice");
        Console.WriteLine("------------------------------------------");

        // Bob encrypts a reply
        string bobMessage = "Hi Alice! I received your secret message.";
        Console.WriteLine($"Bob's plaintext: \"{bobMessage}\"");

        var (bobCiphertext, bobIV) = bobAES.Encrypt(bobMessage, bobSharedKey);
        Console.WriteLine($"Encrypted ciphertext: {bobCiphertext.Substring(0, 40)}...");
        Console.WriteLine($"IV: {bobIV}");

        // Alice receives and decrypts
        string aliceDecrypted = aliceAES.Decrypt(bobCiphertext, bobIV, aliceSharedKey);
        Console.WriteLine($"\nAlice decrypted: \"{aliceDecrypted}\"");
        Console.WriteLine($"Messages match: {bobMessage == aliceDecrypted} ?\n");

        Console.WriteLine("Step 4: Security Verification");
        Console.WriteLine("-----------------------------");

        // Show that each message has a unique IV
        var (msg1Cipher, msg1IV) = aliceAES.Encrypt("Message 1", aliceSharedKey);
        var (msg2Cipher, msg2IV) = aliceAES.Encrypt("Message 1", aliceSharedKey);
        Console.WriteLine($"Same plaintext, different IVs: {msg1IV != msg2IV} ?");
        Console.WriteLine($"Same plaintext, different ciphertexts: {msg1Cipher != msg2Cipher} ?");

        // Demonstrate key isolation (Alice can't decrypt messages meant for another user)
        using var charlieDH = new DiffieHellmanService();
        charlieDH.SetOwnUsername("Charlie");
        string charliePublicKey = charlieDH.GenerateKeyPair("Bob");
        byte[] charlieSharedKey = charlieDH.DeriveSharedKey("Bob", bobPublicKey);

        Console.WriteLine($"\nCharlie's shared key (with Bob): {Convert.ToBase64String(charlieSharedKey).Substring(0, 32)}...");
        Console.WriteLine($"Charlie's key != Alice's key: {!charlieSharedKey.SequenceEqual(aliceSharedKey)} ?");

        try
        {
            // Charlie tries to decrypt Alice's message - will fail!
            var charlieAES = new AesEncryptionService();
            string attemptedDecrypt = charlieAES.Decrypt(ciphertext, iv, charlieSharedKey);
            Console.WriteLine("Charlie SHOULD NOT be able to decrypt!");
        }
        catch (CryptographicException)
        {
            Console.WriteLine("Charlie cannot decrypt Alice's message: ? (Expected!)");
        }

        // Cleanup - zero out keys from memory
        CryptographicOperations.ZeroMemory(aliceSharedKey);
        CryptographicOperations.ZeroMemory(bobSharedKey);
        CryptographicOperations.ZeroMemory(charlieSharedKey);

        Console.WriteLine("\n=== Demo Complete ===");
        Console.WriteLine("\nKey Takeaways:");
        Console.WriteLine("? Alice and Bob derive the same shared key without transmitting it");
        Console.WriteLine("? All messages are encrypted with AES-256-CBC");
        Console.WriteLine("? Each message has a unique random IV");
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

        using var aliceDH = new DiffieHellmanService();
        var aliceAES = new AesEncryptionService();
        aliceDH.SetOwnUsername("Alice");

        using var bobDH = new DiffieHellmanService();
        bobDH.SetOwnUsername("Bob");

        // Step 1: Key Exchange Messages (transmitted in plaintext, but that's OK!)
        string alicePublicKey = aliceDH.GenerateKeyPair("Bob");
        string bobPublicKey = bobDH.GenerateKeyPair("Alice");

        var keyExchangeMessage = new KeyExchangeMessage
        {
            SenderUsername = "Alice",
            RecipientUsername = "Bob",
            PublicKey = alicePublicKey,
            Timestamp = DateTime.UtcNow
        };

        Console.WriteLine("Key Exchange Message (visible to server):");
        Console.WriteLine($"  From: {keyExchangeMessage.SenderUsername}");
        Console.WriteLine($"  To: {keyExchangeMessage.RecipientUsername}");
        Console.WriteLine($"  Public Key: {keyExchangeMessage.PublicKey.Substring(0, 50)}...");
        Console.WriteLine($"  (Server can see this, but cannot derive shared key!)\n");

        // Step 2: Encrypted Message (content is hidden!)
        byte[] sharedKey = aliceDH.DeriveSharedKey("Bob", bobPublicKey);
        var (ciphertext, iv) = aliceAES.Encrypt("This is my secret message!", sharedKey);

        var encryptedMessage = new EncryptedMessage
        {
            MessageId = Guid.NewGuid().ToString(),
            SenderUsername = "Alice",
            RecipientUsername = "Bob",
            Ciphertext = ciphertext,
            IV = iv,
            Timestamp = DateTime.UtcNow
        };

        Console.WriteLine("Encrypted Message (transmitted to server):");
        Console.WriteLine($"  Message ID: {encryptedMessage.MessageId}");
        Console.WriteLine($"  From: {encryptedMessage.SenderUsername}");
        Console.WriteLine($"  To: {encryptedMessage.RecipientUsername}");
        Console.WriteLine($"  Ciphertext: {encryptedMessage.Ciphertext}");
        Console.WriteLine($"  IV: {encryptedMessage.IV}");
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
