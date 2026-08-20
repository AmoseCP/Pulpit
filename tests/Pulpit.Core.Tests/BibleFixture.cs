using System;
using System.IO;
using Pulpit.Core.Data;
using Pulpit.Core.Parsing;
using Xunit;

namespace Pulpit.Core.Tests;

/// <summary>
/// 整个测试程序集共用一个只读仓储。
/// </summary>
/// <remarks>
/// 用真库（<c>bible_cuv.db</c> 由 csproj 复制到输出目录）。DEVELOPMENT_PLAN §6
/// 的期望值全部是在这个库上实测得来的，换成假库就失去了回归意义。
/// </remarks>
public sealed class BibleFixture : IDisposable
{
    public BibleFixture()
    {
        DatabasePath = Path.Combine(AppContext.BaseDirectory, "bible_cuv.db");
        Repository = new BibleRepository(DatabasePath);
        Parser = new ReferenceParser(Repository);
    }

    public string DatabasePath { get; }

    public BibleRepository Repository { get; }

    public ReferenceParser Parser { get; }

    public void Dispose() => Repository.Dispose();
}

[CollectionDefinition(Name)]
public sealed class BibleCollection : ICollectionFixture<BibleFixture>
{
    public const string Name = "bible";
}
