# Secure Chat Application

A complete end-to-end encrypted chat system built with .NET 8, WPF, ASP.NET Core, and SignalR.

**The server NEVER sees plaintext messages - all encryption/decryption happens client-side.**

## ?? Security Architecture

### End-to-End Encryption Overview

```
???????????????                    ???????????????                    ???????????????
?   Alice     ?                    ?   Server    ?                    ?    Bob      ?
?  (Client)   ?                    ?  (Relay)    ?                    ?  (Client)   ?
???????????????                    ???????????????                    ???????????????
       ?                                  ?                                  ?
       ?  1. Generate ECDH Key Pair       ?                                  ?
       ?  (Private + Public)              ?                                  ?
       ?                                  ?                                  ?
       ????? 2. Send Public Key ?????????>?                                  ?
       ?                                  ????? 3. Relay Public Key ????????>?
       ?                                  ?                                  ?
       ?                                  ?      4. Generate ECDH Key Pair   ?
       ?                                  ?      (Private + Public)          ?
       ?                                  ?                                  ?
       ?                                  ?<???? 5. Send Public Key ??????????
       ?<???? 6. Relay Public Key ?????????                                  ?
       ?                                  ?                                  ?
       ?  7. Derive Shared Secret         ?                                  ?  7. Derive Shared Secret
       ?  ECDH(Alice.Priv, Bob.Pub)       ?                                  ?  ECDH(Bob.Priv, Alice.Pub)
       ?           ?                      ?                                  ?           ?
       ?           ???????????????????????????????????????????????????????????           ?
       ?              SAME SHARED SECRET  ?  (Server cannot compute this)                ?
       ?                                  ?                                  ?
       ?  8. Derive AES-256 Key (HKDF)    ?                                  ?  8. Derive AES-256 Key (HKDF)
       ?                                  ?                                  ?
       ?  9. Encrypt: AES-GCM(message)    ?                                  ?
       ????? 10. Send Ciphertext ????????>?                                  ?
       ?                                  ????? 11. Relay Ciphertext ???????>?
       ?                                  ?                                  ?  12. Decrypt: AES-GCM(ciphertext)
       ?                                  ?                                  ?
```

### Cryptographic Components

| Component | Algorithm | Key Size | Purpose |
|-----------|-----------|----------|---------|
| Key Exchange | ECDH (P-256/secp256r1) | 256-bit | Establish shared secret |
| Key Derivation | HKDF-SHA256 | N/A | Derive AES key from shared secret |
| Encryption | AES-256-GCM | 256-bit | Encrypt messages |
| Nonce | Random | 96-bit (12 bytes) | Unique per message |
| Auth Tag | GCM | 128-bit (16 bytes) | Message integrity |

## ?? Diffie-Hellman Key Exchange Flow

### Step-by-Step Process

1. **Alice wants to chat with Bob**
   - Alice generates an ECDH key pair (private key stays local)
   - Alice sends her PUBLIC key to Bob via the server

2. **Server relays the public key**
   - Server receives Alice's public key
   - Server forwards it to Bob
   - **Server CANNOT derive the shared secret** (needs a private key)

3. **Bob receives and responds**
   - Bob generates his own ECDH key pair
   - Bob computes: `SharedSecret = ECDH(Bob.Private, Alice.Public)`
   - Bob sends his PUBLIC key back to Alice via server

4. **Alice completes the exchange**
   - Alice receives Bob's public key
   - Alice computes: `SharedSecret = ECDH(Alice.Private, Bob.Public)`
   - Both arrive at the **SAME shared secret** (DH mathematical property)

5. **Key Derivation**
   - Both use HKDF-SHA256 to derive a 256-bit AES key from the shared secret
   - The raw shared secret is securely erased from memory

### Why ECDH P-256?

- **128-bit security level** - Considered secure by NIST
- **Widely audited** - Extensively analyzed by cryptographers
- **Efficient** - Smaller key sizes than RSA with equivalent security
- **Built into .NET** - Uses OS-level cryptographic providers

## ?? Encryption/Decryption Process

### Encryption (Before Sending)

```csharp
// 1. Generate random 12-byte nonce (CRITICAL: Never reuse!)
byte[] nonce = new byte[12];
RandomNumberGenerator.Fill(nonce);

// 2. Encrypt with AES-256-GCM
using var aesGcm = new AesGcm(sharedKey, 16);
aesGcm.Encrypt(nonce, plaintext, ciphertext, authTag);

// 3. Send: {ciphertext, nonce, authTag} to server
```

### Decryption (After Receiving)

```csharp
// 1. Extract components from received message
// 2. Verify authentication tag (integrity check)
// 3. If valid, decrypt; if invalid, reject (tampering detected)

using var aesGcm = new AesGcm(sharedKey, 16);
aesGcm.Decrypt(nonce, ciphertext, authTag, plaintext);
// If authTag doesn't match, CryptographicException is thrown
```

### Why AES-GCM?

- **AEAD (Authenticated Encryption with Associated Data)** - Provides both confidentiality AND integrity
- **No separate HMAC needed** - Auth tag is built into the mode
- **Fast** - Hardware-accelerated on modern CPUs (AES-NI)
- **NIST approved** - Recommended for government use

## ?? Project Structure

```
SecureChatApplication/
??? SecureChatApplication/          # WPF Client
?   ??? Converters/
?   ?   ??? Converters.cs           # XAML value converters
?   ??? Models/
?   ?   ??? ChatMessage.cs          # Decrypted message model
?   ?   ??? ChatPartner.cs          # User with shared key
?   ?   ??? EncryptedMessage.cs     # Encrypted payload
?   ?   ??? KeyExchangeMessage.cs   # DH public key exchange
?   ??? Services/
?   ?   ??? AesEncryptionService.cs # AES-256-GCM encryption
?   ?   ??? DiffieHellmanService.cs # ECDH key exchange
?   ?   ??? SignalRChatService.cs   # SignalR client
?   ??? ViewModels/
?   ?   ??? ChatViewModel.cs        # Chat logic + crypto orchestration
?   ?   ??? LoginViewModel.cs       # Login/connection logic
?   ?   ??? RelayCommand.cs         # ICommand implementation
?   ?   ??? ViewModelBase.cs        # INotifyPropertyChanged base
?   ??? Views/
?   ?   ??? ChatView.xaml           # Chat UI
?   ?   ??? ChatView.xaml.cs
?   ?   ??? LoginView.xaml          # Login UI
?   ?   ??? LoginView.xaml.cs
?   ??? App.xaml                    # Application resources
?   ??? App.xaml.cs                 # DI configuration
?   ??? MainWindow.xaml             # Main window shell
?   ??? MainWindow.xaml.cs          # Navigation logic
?   ??? SecureChatApplication.csproj
?
??? SecureChatServer/               # ASP.NET Core Server
?   ??? Hubs/
?   ?   ??? ChatHub.cs              # SignalR hub (relay only!)
?   ??? Models/
?   ?   ??? ConnectedUser.cs
?   ?   ??? EncryptedMessage.cs
?   ?   ??? KeyExchangeMessage.cs
?   ??? Properties/
?   ?   ??? launchSettings.json
?   ??? appsettings.json
?   ??? appsettings.Development.json
?   ??? Program.cs                  # Server entry point
?   ??? SecureChatServer.csproj
?
??? README.md                       # This file
```

## ?? Getting Started

### Prerequisites

- .NET 8 SDK
- Visual Studio 2022 or VS Code

### Running the Server

```bash
cd SecureChatServer
dotnet run
```

Server starts at: `http://localhost:5000`
SignalR Hub endpoint: `http://localhost:5000/chathub`

### Running the Client

```bash
cd SecureChatApplication
dotnet run
```

Or open in Visual Studio and press F5.

### Quick Test

1. Start the server
2. Launch two client instances
3. Enter different usernames (e.g., "Alice" and "Bob")
4. Connect both to `http://localhost:5000/chathub`
5. Select the other user in the sidebar
6. Wait for "?? End-to-end encrypted" status
7. Send messages - they're encrypted!

## ?? NuGet Packages

### Client (WPF)
- `Microsoft.AspNetCore.SignalR.Client` - SignalR client
- `Microsoft.Extensions.DependencyInjection` - DI container

### Server (ASP.NET Core)
- Built-in SignalR (included in ASP.NET Core)

## ??? Security Model

### What the Server CAN See
- ? Who is online (usernames)
- ? Who is talking to whom (metadata)
- ? Message timestamps
- ? Encrypted ciphertext (useless without keys)
- ? Public keys (safe to share)

### What the Server CANNOT See
- ? Message content (plaintext)
- ? Private keys (never leave the client)
- ? Shared secrets (computed client-side only)
- ? AES encryption keys (derived from shared secret)

### Trust Model
- **Zero trust in server** - Server is just a relay
- **Trust in clients** - Key generation happens locally
- **Trust in cryptographic primitives** - ECDH, AES-GCM, HKDF

## ?? Security Limitations

### Current Implementation Limitations

1. **No Identity Verification**
   - Users can claim any username
   - No authentication of public keys (vulnerable to MITM if server is compromised)
   - **Mitigation**: Implement certificate pinning or key fingerprint verification

2. **No Perfect Forward Secrecy (PFS)**
   - Same key used for entire session
   - If key is compromised, all session messages can be decrypted
   - **Mitigation**: Implement Double Ratchet (like Signal Protocol)

3. **No Message Persistence**
   - Messages are not stored (ephemeral)
   - Lost if client closes
   - **Mitigation**: Implement encrypted local storage

4. **In-Memory Key Storage**
   - Keys are in process memory
   - Could be dumped by malware
   - **Mitigation**: Use hardware security modules (HSM) or secure enclaves

5. **No Key Rotation**
   - Keys don't automatically rotate
   - **Mitigation**: Implement periodic key renegotiation

6. **Metadata Not Protected**
   - Server sees who talks to whom
   - **Mitigation**: Use onion routing (like Tor)

### Production Recommendations

For a production system, consider:

1. **Add user authentication** (OAuth, certificates)
2. **Implement Signal Protocol** (Double Ratchet for PFS)
3. **Add key fingerprint verification** (manual or TOFU)
4. **Use TLS 1.3** for transport security
5. **Implement certificate pinning** in the client
6. **Add rate limiting** on the server
7. **Enable audit logging** (metadata only)
8. **Consider HSM** for key storage

## ?? Security Testing

### Verify End-to-End Encryption

1. **Network inspection**: Use Wireshark to capture traffic
   - You should see only ciphertext, nonces, and auth tags
   - No plaintext messages should be visible

2. **Server inspection**: Add logging to ChatHub
   - Log received messages - they should be encrypted
   - You cannot decrypt them without client keys

3. **Tampering test**: Modify a ciphertext byte in transit
   - Decryption should fail with CryptographicException
   - This proves authentication tag verification works

## ?? License

MIT License - See LICENSE file for details.

## ?? Acknowledgments

- .NET Cryptography APIs
- SignalR team for real-time communication
- NIST for cryptographic standards (SP 800-38D, SP 800-56A)
