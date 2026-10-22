using System;
using System.Collections.Generic;
using System.IO;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using GitlabCICDNotify.Core;

namespace GitlabCICDNotify.App;

public sealed class StateStore(string? path = null)
{
    private readonly string _path = path ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GitlabCICDNotify", "accounts.dat");
    public async Task<List<Account>> LoadAsync(CancellationToken ct)
    {
        if (!File.Exists(_path)) return [];
        var encrypted = await File.ReadAllBytesAsync(_path, ct);
        var clear = ProtectedData.Unprotect(encrypted, null, DataProtectionScope.CurrentUser);
        try { return JsonSerializer.Deserialize<List<Account>>(clear) ?? throw new InvalidDataException("Neplatný súbor účtov."); }
        finally { CryptographicOperations.ZeroMemory(clear); }
    }

    public async Task SaveAsync(List<Account> accounts, CancellationToken ct)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
        var clear = JsonSerializer.SerializeToUtf8Bytes(accounts);
        byte[] encrypted;
        try { encrypted = ProtectedData.Protect(clear, null, DataProtectionScope.CurrentUser); }
        finally { CryptographicOperations.ZeroMemory(clear); }
        var temporary = _path + ".tmp";
        await File.WriteAllBytesAsync(temporary, encrypted, ct);
        File.Move(temporary, _path, true);
    }
}
