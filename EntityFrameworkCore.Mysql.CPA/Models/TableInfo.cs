

namespace EntityFrameworkCore.Mysql.CPA.Models
{
    public class TableInfo
    {
        public string Name { get; set; }
        public List<ColumnInfo> Columns { get; set; } = new();
    }
}
