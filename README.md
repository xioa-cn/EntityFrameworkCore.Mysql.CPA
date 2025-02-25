# EntityFrameworkCore.Mysql.CPA

EntityFrameworkCore Mysql 表结构自动更新扩展，用于在应用启动时自动检查并更新数据库表结构。

## 功能特点

- 自动检测并创建数据库（如果不存在）
- 自动创建新表
- 自动更新表结构（添加、修改、删除列）
- 支持多个 DbContext
- 支持同步和异步调用
- 安全的类型转换处理
- 保留现有数据

## 安装

```bash
dotnet add package EntityFrameworkCore.Mysql.CPA
```

## 使用方法

1. 在你的 `Program.cs` 中，在构建应用后调用扩展方法：
```csharp
var app = builder.Build();
// 异步方式
await app.Services.RefreshTableAsync(
typeof(YourDbContext),
typeof(AnotherDbContext)
);
// 或者同步方式
app.Services.RefreshTable(
typeof(YourDbContext),
typeof(AnotherDbContext)
);
app.Run();
```

2. 也可以单独对某个 DbContext 实例进行更新：
```csharp
using var context = new YourDbContext();
await context.RefreshTableAsync();
```

## 注意事项

- 建议在应用启动时执行表结构更新
- 更新过程会自动处理类型转换的兼容性
- 支持的类型转换包括：
  - int -> long, decimal
  - float -> double
  - short -> int, long
  - DateTime -> DateTimeOffset
- 字符串类型的长度变化会被安全处理

## 许可证
MIT

## 贡献

欢迎提交 Issue 和 Pull Request
