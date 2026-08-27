using System;
using System.Collections.Concurrent;

namespace Saas.Identity.AspNetCore.Security;

/// <summary>
/// M03.F01.I02 — 失败锁定异常（连续 5 次密码错 -> 锁定 15min）。
/// 抛此异常时 Program.cs 异常映射返 423 LOCKED 状态码。
/// </summary>
public sealed class AccountLockedException : Exception
{
    public DateTime UnlockAt { get; }
    public AccountLockedException(DateTime unlockAt)
        : base($"account locked until {unlockAt:O}")
    {
        UnlockAt = unlockAt;
    }
}

/// <summary>
/// 进程内失败登录计数器 + 锁定。
/// 键 userId（也支持 username，作为弱关联）；值 attempts + lockedUntil。
/// 锁定过期后下次 EnsureNotLocked 自动解锁（不后台扫，惰性检查）。
/// </summary>
public sealed class FailedLoginStore
{
    private sealed record State(int Attempts, DateTime? LockedUntil);

    private readonly ConcurrentDictionary<string, State> _store = new();
    private readonly int _maxAttempts;
    private readonly TimeSpan _lockoutDuration;

    public FailedLoginStore() : this(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15)) { }

    public FailedLoginStore(int maxAttempts, TimeSpan lockoutDuration)
    {
        if (maxAttempts < 1) throw new ArgumentOutOfRangeException(nameof(maxAttempts));
        _maxAttempts = maxAttempts;
        _lockoutDuration = lockoutDuration;
    }

    /// <summary>记录一次失败；达到阈值时锁定。</summary>
    public void RecordFailure(string userId)
    {
        var now = DateTime.UtcNow;
        _store.AddOrUpdate(userId,
            _ => new State(1, null),
            (_, prev) =>
            {
                // 锁定已过期 — 重置计数
                if (prev.LockedUntil is { } until && now >= until)
                    return new State(1, null);
                var next = prev.Attempts + 1;
                return next >= _maxAttempts
                    ? new State(next, now + _lockoutDuration)
                    : new State(next, prev.LockedUntil);
            });
    }

    /// <summary>登录成功 — 清计数与锁定。</summary>
    public void ResetSuccess(string userId) => _store.TryRemove(userId, out _);

    public int GetAttempts(string userId) =>
        _store.TryGetValue(userId, out var s) ? s.Attempts : 0;

    public bool IsLocked(string userId)
    {
        if (!_store.TryGetValue(userId, out var s)) return false;
        return s.LockedUntil is { } until && DateTime.UtcNow < until;
    }

    /// <summary>登录前检查；锁定中抛 AccountLockedException（Program.cs 异常映射 423）。</summary>
    public void EnsureNotLocked(string userId)
    {
        if (!_store.TryGetValue(userId, out var s)) return;
        if (s.LockedUntil is { } until && DateTime.UtcNow < until)
            throw new AccountLockedException(until);
    }
}