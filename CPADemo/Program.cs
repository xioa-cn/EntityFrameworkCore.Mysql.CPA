// See https://aka.ms/new-console-template for more information
using CPADemo.Db;
using EntityFrameworkCore.Mysql.CPA;

SysDbContext sysDbContext = new SysDbContext();
await sysDbContext.RefreshTableAsync();
Console.WriteLine();
