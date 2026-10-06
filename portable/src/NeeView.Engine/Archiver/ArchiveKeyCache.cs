// Copyright (c) NeeLaboratory. 原进程内AES缓存；删除口令调试输出，补充并发与资源释放。
using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using System.Text;
namespace NeeView;

/// <summary>仅进程内缓存，不写入JSON、文件、诊断或系统钥匙串；AES材料每进程生成。</summary>
public sealed class ArchiveKeyCache
{
    public static ArchiveKeyCache Current { get; } = new();
    private readonly object _sync = new();
    private readonly Dictionary<string, byte[]> _map = new(StringComparer.Ordinal);
    private readonly byte[] _aesKey;
    private readonly byte[] _aesIV;
    public ArchiveKeyCache() { using var aes = Aes.Create(); _aesKey = aes.Key; _aesIV = aes.IV; }
    /// <summary>退出清理密文；原进程对象可在窗口关闭后继续复用。</summary>
    public void Clear() { lock (_sync) { foreach (var value in _map.Values) CryptographicOperations.ZeroMemory(value); _map.Clear(); } }
    /// <summary>仅接受成功验证的非空口令，覆盖时擦除旧密文。</summary>
    public void Add(string key, string value)
    {
        if (string.IsNullOrEmpty(key) || string.IsNullOrEmpty(value)) return;
        using var aes = Aes.Create(); aes.Key = _aesKey;
        var plain = Encoding.UTF8.GetBytes(value);
        byte[] encrypted;
        try { encrypted = aes.EncryptCbc(plain, _aesIV); }
        finally { CryptographicOperations.ZeroMemory(plain); }
        lock (_sync) { if (_map.Remove(key, out var previous)) CryptographicOperations.ZeroMemory(previous); _map[key] = encrypted; }
    }
    public bool Remove(string key)
    { lock (_sync) { if (!_map.Remove(key, out var value)) return false; CryptographicOperations.ZeroMemory(value); return true; } }
    public string GetValue(string key) => TryGetValue(key, out var value) ? value : "";
    /// <summary>按原逻辑路径查找；保持实际大小写，不把不同来源统一转为小写。</summary>
    public bool TryGetValue(string key, [MaybeNullWhen(false)] out string value)
    {
        lock (_sync)
        {
            if (!_map.TryGetValue(key, out var encrypted)) { value = null; return false; }
            using var aes = Aes.Create(); aes.Key = _aesKey; var plain = aes.DecryptCbc(encrypted, _aesIV);
            try { value = Encoding.UTF8.GetString(plain); return true; }
            finally { CryptographicOperations.ZeroMemory(plain); }
        }
    }
}
