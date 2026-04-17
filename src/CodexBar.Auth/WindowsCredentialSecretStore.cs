using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using CodexBar.Core;

namespace CodexBar.Auth;

public sealed class WindowsCredentialSecretStore : ISecretStore, IOAuthTokenStore
{
    private const int CredTypeGeneric = 1;
    private const int CredPersistLocalMachine = 2;
    private const string Prefix = "CodexBarWin:";
    private const string ChunkMarker = "chunked:";
    private const int ChunkSize = 1800;
    private const int MaxChunkCleanup = 32;

    public Task WriteSecretAsync(string credentialRef, string secret, CancellationToken cancellationToken = default)
    {
        WriteCredential(credentialRef, secret);
        return Task.CompletedTask;
    }

    public Task<string?> ReadSecretAsync(string credentialRef, CancellationToken cancellationToken = default)
        => Task.FromResult(ReadCredential(credentialRef));

    public Task DeleteSecretAsync(string credentialRef, CancellationToken cancellationToken = default)
    {
        DeleteCredential(credentialRef);
        return Task.CompletedTask;
    }

    public Task WriteTokensAsync(string credentialRef, OAuthTokens tokens, CancellationToken cancellationToken = default)
    {
        WriteCredential(credentialRef, JsonSerializer.Serialize(tokens));
        return Task.CompletedTask;
    }

    public Task<OAuthTokens?> ReadTokensAsync(string credentialRef, CancellationToken cancellationToken = default)
    {
        var json = ReadCredential(credentialRef);
        return Task.FromResult(json is null ? null : JsonSerializer.Deserialize<OAuthTokens>(json));
    }

    public Task DeleteTokensAsync(string credentialRef, CancellationToken cancellationToken = default)
        => DeleteSecretAsync(credentialRef, cancellationToken);

    private static void WriteCredential(string credentialRef, string secret)
    {
        var bytes = Encoding.UTF8.GetBytes(secret);

        if (bytes.Length <= ChunkSize)
        {
            WriteSingleCredential(credentialRef, bytes);
            CleanupChunks(credentialRef, startIndex: 0);
            return;
        }

        var chunks = bytes.Chunk(ChunkSize).ToArray();
        WriteSingleCredential(credentialRef, Encoding.UTF8.GetBytes(ChunkMarker + chunks.Length));
        for (var i = 0; i < chunks.Length; i++)
        {
            WriteSingleCredential(ChunkRef(credentialRef, i), Encoding.UTF8.GetBytes(Convert.ToBase64String(chunks[i])));
        }

        CleanupChunks(credentialRef, chunks.Length);
    }

    private static void WriteSingleCredential(string credentialRef, byte[] bytes)
    {
        if (bytes.Length > 2560)
        {
            throw new InvalidOperationException("Credential chunk is too large for Windows Credential Manager.");
        }

        var credential = new Credential
        {
            Type = CredTypeGeneric,
            TargetName = Target(credentialRef),
            CredentialBlobSize = (uint)bytes.Length,
            CredentialBlob = Marshal.AllocCoTaskMem(bytes.Length),
            Persist = CredPersistLocalMachine,
            UserName = Environment.UserName
        };

        try
        {
            Marshal.Copy(bytes, 0, credential.CredentialBlob, bytes.Length);
            if (!CredWrite(ref credential, 0))
            {
                throw new InvalidOperationException($"CredWrite failed: {Marshal.GetLastWin32Error()}");
            }
        }
        finally
        {
            Marshal.FreeCoTaskMem(credential.CredentialBlob);
        }
    }

    private static string? ReadCredential(string credentialRef)
    {
        var payload = ReadSingleCredential(credentialRef);
        if (payload is null || !payload.StartsWith(ChunkMarker, StringComparison.Ordinal))
        {
            return payload;
        }

        if (!int.TryParse(payload[ChunkMarker.Length..], out var count) || count < 0)
        {
            return null;
        }

        using var stream = new MemoryStream();
        for (var i = 0; i < count; i++)
        {
            var chunk = ReadSingleCredential(ChunkRef(credentialRef, i));
            if (chunk is null)
            {
                return null;
            }

            var bytes = Convert.FromBase64String(chunk);
            stream.Write(bytes, 0, bytes.Length);
        }

        return Encoding.UTF8.GetString(stream.ToArray());
    }

    private static string? ReadSingleCredential(string credentialRef)
    {
        if (!CredRead(Target(credentialRef), CredTypeGeneric, 0, out var credentialPtr))
        {
            return null;
        }

        try
        {
            var credential = Marshal.PtrToStructure<Credential>(credentialPtr);
            if (credential.CredentialBlob == IntPtr.Zero || credential.CredentialBlobSize == 0)
            {
                return string.Empty;
            }

            var bytes = new byte[credential.CredentialBlobSize];
            Marshal.Copy(credential.CredentialBlob, bytes, 0, bytes.Length);
            return Encoding.UTF8.GetString(bytes);
        }
        finally
        {
            CredFree(credentialPtr);
        }
    }

    private static void DeleteCredential(string credentialRef)
    {
        CredDelete(Target(credentialRef), CredTypeGeneric, 0);
        CleanupChunks(credentialRef, startIndex: 0);
    }

    private static void CleanupChunks(string credentialRef, int startIndex)
    {
        for (var i = startIndex; i < MaxChunkCleanup; i++)
        {
            CredDelete(Target(ChunkRef(credentialRef, i)), CredTypeGeneric, 0);
        }
    }

    private static string ChunkRef(string credentialRef, int index)
        => $"{credentialRef}:chunk:{index}";

    private static string Target(string credentialRef)
        => credentialRef.StartsWith(Prefix, StringComparison.Ordinal) ? credentialRef : Prefix + credentialRef;

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredWrite([In] ref Credential userCredential, [In] uint flags);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredRead(string target, int type, int reservedFlag, out IntPtr credentialPtr);

    [DllImport("advapi32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool CredDelete(string target, int type, int flags);

    [DllImport("advapi32.dll", SetLastError = true)]
    private static extern void CredFree([In] IntPtr buffer);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct Credential
    {
        public uint Flags;
        public int Type;
        public string TargetName;
        public string? Comment;
        public System.Runtime.InteropServices.ComTypes.FILETIME LastWritten;
        public uint CredentialBlobSize;
        public IntPtr CredentialBlob;
        public uint Persist;
        public uint AttributeCount;
        public IntPtr Attributes;
        public string? TargetAlias;
        public string UserName;
    }
}
