

namespace EntityFrameworkCore.Mysql.CPA.Models
{
    public class SchemaDifference
    {
        public DifferenceType Type { get; set; }
        public string TableName { get; set; }
        public ColumnInfo OldColumn { get; set; }
        public ColumnInfo NewColumn { get; set; }
    }
}
