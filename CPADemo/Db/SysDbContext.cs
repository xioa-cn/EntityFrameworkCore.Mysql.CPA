using Microsoft.EntityFrameworkCore;

using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;


namespace CPADemo.Db
{
    [Table("Temp")]
    public class Model
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }

        [MaxLength(1000)]
        [Column("Con")]
        public string? Context { get; set; }
        
        [Required]
        [MaxLength(10)]
        public string Temp { get; set; }
    }

    public class SysDbContext : DbContext
    {
        public DbSet<Model> MyProperty { get; set; }

        public SysDbContext() : base()
        {
        }

        public SysDbContext(DbContextOptions<SysDbContext> options) : base(options)
        {
        }

        protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
        {
            var connectString = "Data Source=127.0.0.1;Database=Test;User ID=root;Password=123456;pooling=true;CharSet=utf8;port=3306;sslmode=none;";
            var serverVersion = ServerVersion.AutoDetect(connectString);
            optionsBuilder.UseMySql(connectString, serverVersion, options =>
            {
                // 配置MySQL重试策略
                options.EnableRetryOnFailure(
                    maxRetryCount: 3, // 最多重试3次
                    maxRetryDelay: TimeSpan.FromSeconds(10), // 重试延迟10秒
                    errorNumbersToAdd: null); // 需要重试的错误码
            });
            //默认禁用实体跟踪
            optionsBuilder = optionsBuilder.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            base.OnConfiguring(optionsBuilder);
        }

       
    }
}
