using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;

namespace Saas.Identity.AspNetCore.Security;

/// <summary>
/// M03.F01.I01 — saas session 记录（与 Phase 6 OAuth 端点配套）。
/// 进程内 ConcurrentDictionary + TTL；Phase 6+ 切 Redis。
/// </summary>
public sealed record SaasSession(
    Guid UserId,
    Guid TenantId,
    DateTime CreatedAt,
    DateTime ExpiresAt)
{
    public string Id { get; init; } = Guid.NewGuid().ToString("N");

    public bool IsExpired(DateTime now) => now >= ExpiresAt;
}

/// <summary>
/// 进程内 saas session 存储（key = session.Id，value = SaasSession）。
/// 默认 TTL 24h；过期 entry Get 时惰性清理（不启动 background sweeper，Phase 1 简单优先）。
/// </summary>
public sealed class SaasSessionStore
{
    private readonly TimeSpan _defaultTtl;
    private readonly ConcurrentDictionary<string, SaasSession> _store = new();

    public SaasSessionStore() : this(TimeSpan.FromHours(24)) { }

    public SaasSessionStore(TimeSpan defaultTtl) => _defaultTtl = defaultTtl;

    /// <summary>默认 TTL（用于登录写 session 时计算 ExpiresAt）。</summary>
    public TimeSpan DefaultTtl => _defaultTtl;

    /// <summary>生成新 session ID（base64 32B 随机）。</summary>
    public string GenerateId()
    {
        var bytes = RandomNumberGenerator.GetBytes(32);
        return Convert.ToBase64String(bytes)
            .Replace("=", "")
            .Replace("+", "-")
            .Replace("/", "_");
    }

    public void Put(SaasSession session)
    {
        if (session is null) throw new ArgumentNullException(nameof(session));
        _store[session.Id] = session;
    }

    /// <summary>读 session；过期返 null 并惰性删除。</summary>
    public SaasSession? Get(string? id)
    {
        if (string.IsNullOrEmpty(id)) return null;
        if (!_store.TryGetValue(id, out var session)) return null;
        var now = DateTime.UtcNow;
        if (session.IsExpired(now))
        {
            _store.TryRemove(id, out _);
            return null;
        }
        return session;
    }

    public void Delete(string id)
    {
        if (string.IsNullOrEmpty(id)) return;
        _store.TryRemove(id, out _);
    }

    public int Count => _store.Count;
}