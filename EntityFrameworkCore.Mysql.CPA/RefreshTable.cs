using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace EntityFrameworkCore.Mysql.CPA
{
    public static class RefreshTableHelper
    {
        private static SchemaUpdate _schemaUpdate = new SchemaUpdate();

        public static async Task<DbContext> RefreshTableAsync(this DbContext context)
        {
            await _schemaUpdate.StartAsync(context);
            return context;
        }
        /**
         
          在 Program.cs 中
         var app = builder.Build();
         在应用启动时更新数据库结构
         await app.Services.RefreshTableAsync(
            typeof(YourFirstDbContext),
            typeof(YourSecondDbContext)
         );
         
         */




        /// <summary>
        /// 添加数据库表结构自动更新
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <param name="dbContextTypes">要更新的DbContext类型集合</param>
        /// <returns></returns>
        public static async Task RefreshTableAsync(this IServiceProvider serviceProvider, params Type[] dbContextTypes)
        {
            using var scope = serviceProvider.CreateScope();
            foreach (var contextType in dbContextTypes)
            {
                if (scope.ServiceProvider.GetService(contextType) is DbContext context)
                {
                    await context.RefreshTableAsync();
                }
            }
        }



        /// <summary>
        /// 添加数据库表结构自动更新（同步方法）
        /// </summary>
        /// <param name="serviceProvider">服务提供者</param>
        /// <param name="dbContextTypes">要更新的DbContext类型集合</param>
        public static void RefreshTable(this IServiceProvider serviceProvider, params Type[] dbContextTypes)
        {
            RefreshTableAsync(serviceProvider, dbContextTypes).GetAwaiter().GetResult();
        }
    }
}
