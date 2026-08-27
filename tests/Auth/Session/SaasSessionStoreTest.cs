using System;
using System.Threading;
using Saas.Identity.AspNetCore.Security;
using Xunit;

namespace Saas.Identity.AspNetCore.Tests.Auth.Session;

/// <summary>
/// M03.F01.I01 — SaasSessionStore（进程内 ConcurrentDictionary + TTL 24h）。
///
/// 复现 ADR-0013 路线 A：saas OAuth 端点改造的 session 存储层。Phase 6+ 切 Redis。
/// 本任务：进程内实现 + TTL 过期 + 单点 ID 生成。
/// </summary>
public class SaasSessionStoreTest
{
    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public void Put_thenGet_returnsSameSession()
    {
        var store = new SaasSessionStore(TimeSpan.FromMinutes(5));
        var session = new SaasSession(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        store.Put(session);
        var got = store.Get(session.Id);
        Assert.NotNull(got);
        Assert.Equal(session.UserId, got!.UserId);
        Assert.Equal(session.TenantId, got.TenantId);
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public void Get_unknownId_returnsNull()
    {
        var store = new SaasSessionStore();
        Assert.Null(store.Get("never-existed"));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public void Get_expiredSession_returnsNullAndRemoves()
    {
        // TTL 100ms — put 后等 200ms 再 get，应当作过期
        var store = new SaasSessionStore(TimeSpan.FromMilliseconds(100));
        var session = new SaasSession(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddMilliseconds(100));
        store.Put(session);
        Thread.Sleep(200);
        Assert.Null(store.Get(session.Id));
        // 过期后再次 Get 不报错），且不再占内存
        Assert.Null(store.Get(session.Id));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public void Delete_removesSession()
    {
        var store = new SaasSessionStore();
        var session = new SaasSession(Guid.NewGuid(), Guid.NewGuid(), DateTime.UtcNow, DateTime.UtcNow.AddMinutes(5));
        store.Put(session);
        store.Delete(session.Id);
        Assert.Null(store.Get(session.Id));
    }

    [Fact]
    [Trait("Fn", "M03.F01.I01")]
    public void GenerateId_returnsUniqueIds()
    {
        var store = new SaasSessionStore();
        var id1 = store.GenerateId();
        var id2 = store.GenerateId();
        Assert.NotEqual(id1, id2);
        Assert.True(id1.Length >= 32); // base64 至少 32 字符
    }
}