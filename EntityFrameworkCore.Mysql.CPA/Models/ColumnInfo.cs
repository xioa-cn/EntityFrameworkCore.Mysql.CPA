using System.ComponentModel.DataAnnotations.Schema;


namespace EntityFrameworkCore.Mysql.CPA.Models
{
    public class ColumnInfo
    {
        public string Name { get; set; }
        public Type Type { get; set; }
        public bool IsNullable { get; set; }
        public bool IsPrimaryKey { get; set; }
        public bool IsAutoIncrement { get; set; }
        public int? MaxLength { get; set; }
        public string ColumnType { get; set; }
        public bool IsRequired { get; set; }
        public string DefaultValue { get; set; }
        public bool IsUnique { get; set; }
        public string Collation { get; set; }
        public int? Precision { get; set; }
        public int? Scale { get; set; }
        // 添加数据库生成选项
        public DatabaseGeneratedOption? DatabaseGenerated { get; set; }
    }
}
