using System;
using System.Threading;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Auth.Session;

/// <summary>
/// M03.F01.I02 — 失败锁定存储：连续 5 次密码错 -> 锁定 15min。
/// 进程内 ConcurrentDictionary；Phase 6+ 切 Redis / shared store。
/// 锁定状态：键=userId, value={Attempts, LockedUntil}。
/// </summary>
public class FailedLoginStoreTest
{
    [Fact]
    [Trait("Fn", "M03.F01.I02")]
    public void Record_thenGet_attemptsIncrements()
    {
        var store = new FailedLoginStore();
        store.RecordFailure("alice");
        store.RecordFailure("alice");
        Assert.Equal(2, store.GetAttempts("alice"));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I02")]
    public void LockedUser_throwsLockedException()
    {
        var store = new FailedLoginStore(maxAttempts: 5, lockoutDuration: TimeSpan.FromMilliseconds(100));
        for (int i = 0; i < 5; i++) store.RecordFailure("bob");
        // 第 5 次后立即锁定
        Assert.True(store.IsLocked("bob"));
        Assert.Throws<AccountLockedException>(() => store.EnsureNotLocked("bob"));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I02")]
    public void LockoutExpires_afterDuration()
    {
        var store = new FailedLoginStore(maxAttempts: 5, lockoutDuration: TimeSpan.FromMilliseconds(100));
        for (int i = 0; i < 5; i++) store.RecordFailure("carol");
        Assert.True(store.IsLocked("carol"));
        Thread.Sleep(200);
        Assert.False(store.IsLocked("carol"));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I02")]
    public void ResetSuccess_clearsCounter()
    {
        var store = new FailedLoginStore();
        store.RecordFailure("dave");
        store.RecordFailure("dave");
        store.ResetSuccess("dave");
        Assert.Equal(0, store.GetAttempts("dave"));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I02")]
    public void BelowThreshold_notLocked()
    {
        var store = new FailedLoginStore(maxAttempts: 5, lockoutDuration: TimeSpan.FromMinutes(15));
        for (int i = 0; i < 4; i++) store.RecordFailure("eve");
        Assert.False(store.IsLocked("eve"));
        Assert.Equal(4, store.GetAttempts("eve"));
    }
}